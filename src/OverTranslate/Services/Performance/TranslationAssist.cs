using System.IO;
using NLog;
using OverTranslate.Models;
using OverTranslate.Services.Providers;

namespace OverTranslate.Services.Performance;

/// <summary>
/// Sorts a translation request into what can be answered locally — the game glossary, or a line an
/// engine has already answered — and what has to be sent, and puts the answers back in order.
/// </summary>
/// <remarks>
/// <para>It wraps the one call every translation path in the application goes through
/// (<see cref="TranslationService.TranslateAsync"/>), so a screenshot translation, a realtime session
/// and the other callers all get the same cache and glossary without any of them changing.</para>
///
/// <para>What it will not do, to keep the original behaviour where the answer depends on more than the
/// line: it leaves a request that carries <see cref="TranslationContext"/> (the OpenAI-compatible
/// engine translating with the screen around the line) exactly as it was, and it never answers from
/// the cache when the source language is automatic, because the detected language is part of what a
/// caller gets back and a cached line has none. A line no engine translated is never stored.</para>
/// </remarks>
internal static class TranslationAssist
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private static readonly object Gate = new();
    private static TranslationCacheStore? _cache;
    private static GameGlossary _glossary = GameGlossary.Empty;
    private static string _glossaryKey = "";
    private static string _glossaryStamp = "";

    /// <summary>Where the persistent translation cache lives (next to the settings file).</summary>
    public static string CachePath { get; } = Path.Combine(AppDataPaths.Root, "translation-cache.json");

    /// <summary>The user's list of game terms.</summary>
    public static string GlossaryPath { get; } = Path.Combine(AppDataPaths.Root, "glossary.txt");

    public static int CachedLines => Cache.Count;

    private static TranslationCacheStore Cache
    {
        get
        {
            lock (Gate)
            {
                var profile = PerformanceManager.Current;
                if (_cache is null)
                    _cache = new TranslationCacheStore(CachePath, profile.PersistentCacheEntries, profile.PersistentCacheTtl);
                else
                    _cache.Configure(profile.PersistentCacheEntries, profile.PersistentCacheTtl);
                return _cache;
            }
        }
    }

    /// <summary>Forgets every cached translation, in memory and on disk.</summary>
    public static void ClearCache()
    {
        lock (Gate) _cache?.Clear();
        if (_cache is null) new TranslationCacheStore(CachePath, 16, TimeSpan.Zero).Clear();
        Log.Info("Translation cache cleared");
    }

    /// <summary>Writes the cache to disk if it changed. Called on exit.</summary>
    public static void Flush()
    {
        TranslationCacheStore? cache;
        lock (Gate) cache = _cache;
        cache?.Flush();
    }

    /// <summary>Creates <c>glossary.txt</c> with a few commented examples if there is none, and returns its path.</summary>
    public static string EnsureGlossaryFile()
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.Root);
            if (!File.Exists(GlossaryPath))
                File.WriteAllText(GlossaryPath, GlossaryTemplate, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not create the glossary file");
        }

        return GlossaryPath;
    }

    internal const string GlossaryTemplate =
        "# OverTranslate — từ điển game (UTF-8). Mỗi dòng: Từ gốc = Bản dịch\n" +
        "# Dòng bắt đầu bằng # là ghi chú. Lưu file rồi dịch lại là có hiệu lực (không cần khởi động lại).\n" +
        "# [VI] ở một dòng riêng: các mục bên dưới chỉ áp dụng khi dịch sang tiếng Việt; [*] áp dụng cho mọi ngôn ngữ.\n" +
        "#\n" +
        "# Health = Máu\n" +
        "# Inventory = Túi đồ\n" +
        "# Quest = Nhiệm vụ\n" +
        "# Respawn = Hồi sinh\n" +
        "#\n" +
        "# Tên nhân vật, địa danh, vật phẩm cũng ghi cùng cách:\n" +
        "# Health Potion = Bình máu\n";

    /// <summary>
    /// Translates <paramref name="blocks"/>, asking <paramref name="send"/> only for what the glossary and
    /// the cache could not answer.
    /// </summary>
    public static async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> RunAsync(
        List<OcrTextBlock> blocks,
        string sourceLang,
        string targetLang,
        TranslationProvider engine,
        TranslationContext? context,
        Func<List<OcrTextBlock>, Task<(List<TranslatedBlock> Blocks, string DetectedLang)>> send,
        Action<bool> engineUsed)
    {
        var settings = SettingsService.Instance.Current.Performance;
        var automaticSource = string.IsNullOrWhiteSpace(sourceLang)
            || sourceLang.Equals(LanguageData.AutomaticSourceLanguage, StringComparison.OrdinalIgnoreCase);
        var useCache = settings.UseTranslationCache && context is null && !automaticSource;
        var glossary = settings.UseGameGlossary && context is null ? GlossaryFor(targetLang) : GameGlossary.Empty;

        if (blocks.Count == 0 || (!useCache && glossary.Count == 0))
            return await Pass(send, blocks, engineUsed);

        var engineName = engine.ToString();
        var prefix = glossary.Count > 0 ? $"{engineName}|g{_glossaryStamp}" : engineName;

        var resolved = new string?[blocks.Count];
        var keys = new string?[blocks.Count];
        var toSend = new List<OcrTextBlock>();
        var sendIndex = new List<int>();
        int hits = 0;

        for (var i = 0; i < blocks.Count; i++)
        {
            var text = blocks[i].Text;

            if (glossary.TryTranslateWhole(text, out var whole))
            {
                resolved[i] = whole;
                hits++;
                continue;
            }

            if (useCache && TranslationCacheStore.IsCacheable(text))
            {
                var key = TranslationCacheStore.Key(prefix, sourceLang, targetLang, text);
                keys[i] = key;
                if (Cache.TryGet(key, out var cached))
                {
                    resolved[i] = cached;
                    hits++;
                    continue;
                }
            }

            var substituted = glossary.Count > 0 ? glossary.SubstituteTerms(text) : text;
            toSend.Add(substituted == text ? blocks[i] : blocks[i] with { Text = substituted });
            sendIndex.Add(i);
        }

        PerfCounters.RecordCache(hits, toSend.Count);

        // Nothing to ask an engine, but only possible to answer without it when the caller does not
        // need a detected language back.
        if (toSend.Count == 0 && automaticSource)
            return await Pass(send, blocks, engineUsed);

        var detected = sourceLang;
        List<TranslatedBlock> answered = [];
        if (toSend.Count > 0)
        {
            var (list, lang) = await Pass(send, toSend, engineUsed);
            answered = list;
            if (!string.IsNullOrEmpty(lang)) detected = lang;
        }
        else
        {
            engineUsed(false);
        }

        var merged = new List<TranslatedBlock>(blocks.Count);
        var next = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (resolved[i] is { } local)
            {
                merged.Add(Local(blocks[i], local));
                continue;
            }

            var slot = next++;
            if (slot >= answered.Count)
            {
                // A short reply: degrade this line to untranslated rather than losing the rest.
                merged.Add(Local(blocks[i], blocks[i].Text) with { Untranslated = true });
                continue;
            }

            // The engine saw the substituted text; the overlay shows the line as it was read.
            var result = answered[slot] with { OriginalText = blocks[i].Text };
            merged.Add(result);

            if (keys[i] is { } storeKey && !result.Untranslated && !string.IsNullOrWhiteSpace(result.TranslatedText))
                Cache.Set(storeKey, result.TranslatedText);
        }

        return (merged, detected);
    }

    private static async Task<(List<TranslatedBlock> Blocks, string DetectedLang)> Pass(
        Func<List<OcrTextBlock>, Task<(List<TranslatedBlock> Blocks, string DetectedLang)>> send,
        List<OcrTextBlock> blocks,
        Action<bool> engineUsed)
    {
        engineUsed(true);
        PerfCounters.TranslationStarted();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return await send(blocks);
        }
        finally
        {
            PerfCounters.TranslationFinished((int)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private static TranslatedBlock Local(OcrTextBlock block, string text) =>
        new(block.Text, text, block.Bounds, block.SourceLineBounds, block.RenderGlyphHeight)
        {
            RunsAcross = block.RunsAcross,
            FontGlyphHeight = block.FontGlyphHeight,
            Tilt = block.Tilt,
        };

    // The glossary for a target language, re-read when a file or the target changes: the global
    // glossary.txt with the active game's own file (glossaries\<name>) laid over it, so an entry for the
    // same word in the game's file wins. Two stat calls per translation request, which is nothing next
    // to the request.
    private static GameGlossary GlossaryFor(string targetLang)
    {
        try
        {
            var gameFile = PerformanceManager.ActiveGameProfile?.Glossary;
            var gamePath = gameFile is null ? null : Path.Combine(GameProfileStore.GlossaryFolder, gameFile);
            var hasGlobal = File.Exists(GlossaryPath);
            var hasGame = gamePath is not null && File.Exists(gamePath);
            if (!hasGlobal && !hasGame) return GameGlossary.Empty;

            var stamp = (hasGlobal ? File.GetLastWriteTimeUtc(GlossaryPath).Ticks.ToString() : "0")
                + "-" + (hasGame ? File.GetLastWriteTimeUtc(gamePath!).Ticks.ToString() : "0");
            lock (Gate)
            {
                if (_glossaryKey == targetLang && _glossaryStamp == stamp) return _glossary;

                // "[*]" between them: the game's file starts from "all targets" whatever section the
                // global file ended in.
                var text = (hasGlobal ? File.ReadAllText(GlossaryPath) : "")
                    + "\n[*]\n" + (hasGame ? File.ReadAllText(gamePath!) : "");
                _glossary = GameGlossary.Parse(text, targetLang);
                _glossaryKey = targetLang;
                _glossaryStamp = stamp;
                Log.Info("Game glossary loaded for {Target}: {Count} entries{Game}",
                    targetLang, _glossary.Count, hasGame ? $" (with {gameFile})" : "");
                return _glossary;
            }
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not read the glossary; continuing without it");
            return GameGlossary.Empty;
        }
    }
}
