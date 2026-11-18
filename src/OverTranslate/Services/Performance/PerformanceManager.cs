using System.Diagnostics;
using System.Runtime;
using NLog;
using OverTranslate.Models;

namespace OverTranslate.Services.Performance;

/// <summary>
/// The profile the application is running with, resolved from the user's setting and the installed
/// RAM, and the one memory-trimming step the profiles allow.
/// </summary>
internal static class PerformanceManager
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // At most one trim per this long, however often the model is released.
    private static readonly TimeSpan MinTrimInterval = TimeSpan.FromSeconds(60);

    private static readonly object Gate = new();
    private static PerformanceProfile? _current;
    private static long _lastTrim;
    private static GameProfile? _game;
    private static bool _priorityLowered;

    /// <summary>The active profile. Resolved on first use, then only by <see cref="Refresh"/>.</summary>
    public static PerformanceProfile Current => _current ?? Refresh();

    /// <summary>The per-game profile in force while a single window is being translated, or null.</summary>
    public static GameProfile? ActiveGameProfile => _game;

    /// <summary>The Performance settings with the active game profile's choices laid over them.</summary>
    public static PerformanceSettings EffectiveSettings =>
        GameProfiles.Apply(SettingsService.Instance.Current.Performance, _game);

    /// <summary>
    /// Makes <paramref name="profile"/> (or none) the game profile and resolves everything again.
    /// Called when a realtime session starts on a window, and with null when it ends.
    /// </summary>
    public static void SetGameProfile(GameProfile? profile)
    {
        if (ReferenceEquals(_game, profile)) return;

        _game = profile;
        if (profile is null) Log.Info("Game profile cleared");
        else Log.Info("Game profile \"{Name}\" applied", profile.Name);
        Refresh();
    }

    /// <summary>Raised when <see cref="Refresh"/> lands on a different profile.</summary>
    public static event EventHandler? Changed;

    /// <summary>Resolves the profile at startup and writes what it chose, and why, to the log.</summary>
    public static void Initialize()
    {
        var profile = Refresh();
        Log.Info(
            "Performance profile {Level} (mode={Mode}, installed RAM={Ram} MB, idle release={Idle}s, " +
            "still-screen slow-down up to {Backoff}ms)",
            profile.Level,
            SettingsService.Instance.Current.Performance.Mode,
            SystemMemory.TotalPhysicalBytes / (1024 * 1024),
            (int)profile.OcrIdleRelease.TotalSeconds,
            (int)profile.BackoffMax.TotalMilliseconds);
    }

    /// <summary>Resolves the profile again from the settings as they are now.</summary>
    public static PerformanceProfile Refresh()
    {
        var settings = EffectiveSettings;
        var profile = PerformanceProfile.From(settings, SystemMemory.TotalPhysicalBytes);

        PerformanceProfile? previous;
        lock (Gate)
        {
            previous = _current;
            _current = profile;
        }

        ApplyProcessPriority(settings.LowerProcessPriority ?? profile.LowerPriority);

        if (previous is not null && previous != profile)
        {
            Log.Info("Performance profile changed to {Level}", profile.Level);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return profile;
    }

    /// <summary>
    /// Below-normal CPU priority for this process, so a game that wants every core gets them first.
    /// Only ever moves a process that is at normal priority down, and only puts back what it moved:
    /// a priority the user chose (a shortcut that starts the program at High, say) is left alone.
    /// Never High or Realtime — the only direction here is down.
    /// </summary>
    private static void ApplyProcessPriority(bool lower)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            if (lower && process.PriorityClass == ProcessPriorityClass.Normal)
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                _priorityLowered = true;
                Log.Info("Process priority lowered to BelowNormal");
            }
            else if (!lower && _priorityLowered && process.PriorityClass == ProcessPriorityClass.BelowNormal)
            {
                process.PriorityClass = ProcessPriorityClass.Normal;
                _priorityLowered = false;
                Log.Info("Process priority restored to Normal");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not change the process priority");
        }
    }

    /// <summary>
    /// After the recognition model has been let go from idleness: one compacting collection, so the
    /// managed side of what was just freed (bitmaps, buffers, the engine's own objects) goes too and
    /// the large-object heap is not left fragmented around the hole.
    /// </summary>
    /// <remarks>
    /// Done where memory is actually being given back, once per release and no more than once a
    /// minute, and only on the profiles that ask for it — never on a timer, and never while a
    /// recognition is running (the caller has just disposed an idle model). This is the opposite of
    /// calling <c>GC.Collect</c> to make a number look smaller: it runs when nothing is being
    /// translated and costs one blocking pass over a heap that has just been emptied.
    /// </remarks>
    public static void TrimAfterModelRelease()
    {
        if (!Current.TrimAfterRelease) return;

        var now = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            if (_lastTrim != 0 && Stopwatch.GetElapsedTime(_lastTrim, now) < MinTrimInterval) return;
            _lastTrim = now;
        }

        try
        {
            var before = GC.GetTotalMemory(false);
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Log.Debug("Trim after model release: managed heap {Before} -> {After} KB",
                before / 1024, GC.GetTotalMemory(false) / 1024);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Trim after model release failed");
        }
    }
}
