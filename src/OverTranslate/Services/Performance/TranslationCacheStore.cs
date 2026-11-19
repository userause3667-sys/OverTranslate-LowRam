using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OverTranslate.Services.Performance;

/// <summary>
/// Translations kept between sessions, so a line an engine has already answered is not asked again.
/// Bounded by entry count and by age, normalised before comparing, and cleared on demand.
/// </summary>
/// <remarks>
/// <para><b>Memory.</b> The file is read on first use, not at startup, and holds at most
/// <see cref="PerformanceProfile.PersistentCacheEntries"/> short lines (texts over
/// <see cref="MaxTextLength"/> characters are not kept) — a few hundred KB at the Ultra Low size.
/// There is no timer while nothing has changed: a write schedules one save a little later, and the
/// timer is gone once it has run.</para>
///
/// <para><b>Keys.</b> Engine, source language, target language and the text after
/// <see cref="Normalize"/> — Unicode compatibility form, runs of whitespace collapsed, ends trimmed.
/// Case is kept: "HEALTH" and "Health" are drawn differently in a game and a translation is not
/// always the same for both.</para>
///
/// <para><b>Eviction.</b> When the count passes the limit the least recently used tenth is dropped in
/// one go, so an over-full cache is trimmed once per tenth of its size rather than on every write.</para>
/// </remarks>
internal sealed class TranslationCacheStore
{
    /// <summary>Texts longer than this are never kept: a paragraph rarely comes back, and it is the bulk of the file.</summary>
    public const int MaxTextLength = 500;

    private const int FileVersion = 1;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(20);

    private sealed class Entry
    {
        public required string Value { get; set; }
        public long CreatedTicks { get; init; }
        public long UsedTicks { get; set; }
    }

    private sealed class FileEntry
    {
        public string K { get; set; } = "";
        public string V { get; set; } = "";
        public long C { get; set; }
        public long U { get; set; }
    }

    private sealed class FileShape
    {
        public int Version { get; set; }
        public List<FileEntry> Entries { get; set; } = [];
    }

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _maxEntries;
    private TimeSpan _ttl;
    private bool _loaded;
    private bool _dirty;
    private System.Threading.Timer? _saveTimer;

    public TranslationCacheStore(string path, int maxEntries, TimeSpan ttl, TimeProvider? clock = null)
    {
        _path = path;
        _maxEntries = Math.Max(16, maxEntries);
        _ttl = ttl;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The number of entries held (loads the file first).</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _entries.Count;
            }
        }
    }

    /// <summary>Changes the limits, trimming straight away if they got tighter.</summary>
    public void Configure(int maxEntries, TimeSpan ttl)
    {
        lock (_gate)
        {
            maxEntries = Math.Max(16, maxEntries);
            if (maxEntries == _maxEntries && ttl == _ttl) return;

            _maxEntries = maxEntries;
            _ttl = ttl;
            if (!_loaded) return;
            if (PurgeExpired() | Trim()) MarkDirty();
        }
    }

    /// <summary>The text as it is compared: compatibility-normalised, whitespace collapsed, trimmed.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var normalised = text.IsNormalized(NormalizationForm.FormKC) ? text : text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalised.Length);
        var pendingSpace = false;
        foreach (var c in normalised)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }

    public static string Key(string engine, string sourceLanguage, string targetLanguage, string text) =>
        string.Join('\u001f', engine, sourceLanguage, targetLanguage, Normalize(text));

    /// <summary>Whether a text is worth keeping at all.</summary>
    public static bool IsCacheable(string text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;

    public bool TryGet(string key, out string value)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_entries.TryGetValue(key, out var entry))
            {
                var now = _clock.GetUtcNow().UtcTicks;
                if (IsExpired(entry, now))
                {
                    _entries.Remove(key);
                    MarkDirty();
                }
                else
                {
                    entry.UsedTicks = now;
                    value = entry.Value;
                    return true;
                }
            }
        }

        value = "";
        return false;
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        lock (_gate)
        {
            EnsureLoaded();
            var now = _clock.GetUtcNow().UtcTicks;
            _entries[key] = new Entry { Value = value, CreatedTicks = now, UsedTicks = now };
            Trim();
            MarkDirty();
        }
    }

    /// <summary>Forgets everything, in memory and on disk.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _loaded = true;
            _entries.Clear();
            _dirty = false;
            _saveTimer?.Dispose();
            _saveTimer = null;
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (Exception)
            {
                // Nothing to clean up if the file is locked; the next save replaces it.
                _dirty = true;
            }
        }
    }

    /// <summary>Writes the file now if anything changed since it was read or last written.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
            if (!_dirty || !_loaded) return;
            Save();
        }
    }

    private bool IsExpired(Entry entry, long nowTicks) =>
        _ttl > TimeSpan.Zero && nowTicks - entry.CreatedTicks > _ttl.Ticks;

    private bool PurgeExpired()
    {
        if (_ttl <= TimeSpan.Zero) return false;

        var now = _clock.GetUtcNow().UtcTicks;
        var expired = _entries.Where(pair => IsExpired(pair.Value, now)).Select(pair => pair.Key).ToList();
        foreach (var key in expired) _entries.Remove(key);
        return expired.Count > 0;
    }

    // Caller holds _gate. Drops the least recently used tenth once over the limit.
    private bool Trim()
    {
        if (_entries.Count <= _maxEntries) return false;

        var drop = Math.Max(_entries.Count - _maxEntries, _maxEntries / 10);
        foreach (var key in _entries.OrderBy(pair => pair.Value.UsedTicks).Take(drop).Select(pair => pair.Key).ToList())
            _entries.Remove(key);
        return true;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(_path)) return;

            using var stream = File.OpenRead(_path);
            var shape = JsonSerializer.Deserialize<FileShape>(stream);
            if (shape is null || shape.Version != FileVersion) return;

            foreach (var entry in shape.Entries)
            {
                if (string.IsNullOrEmpty(entry.K) || string.IsNullOrWhiteSpace(entry.V)) continue;
                _entries[entry.K] = new Entry { Value = entry.V, CreatedTicks = entry.C, UsedTicks = entry.U };
            }

            if (PurgeExpired() | Trim()) MarkDirty();
        }
        catch (Exception)
        {
            // A damaged cache file is only a cache: start empty and let the next save replace it.
            _entries.Clear();
            _dirty = true;
        }
    }

    // Caller holds _gate.
    private void MarkDirty()
    {
        _dirty = true;
        _saveTimer ??= new System.Threading.Timer(_ => Flush(), null, SaveDelay, Timeout.InfiniteTimeSpan);
    }

    // Caller holds _gate. Written beside the file and moved over it, so a crash mid-write leaves the
    // previous cache intact.
    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var shape = new FileShape
            {
                Version = FileVersion,
                Entries = _entries.Select(pair => new FileEntry
                {
                    K = pair.Key,
                    V = pair.Value.Value,
                    C = pair.Value.CreatedTicks,
                    U = pair.Value.UsedTicks,
                }).ToList(),
            };

            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, shape);
            File.Move(temp, _path, overwrite: true);
            _dirty = false;
        }
        catch (Exception)
        {
            // Left dirty: the next change or the exit flush tries again.
        }
    }
}
