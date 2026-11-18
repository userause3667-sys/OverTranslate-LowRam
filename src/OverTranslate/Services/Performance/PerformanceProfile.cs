using OverTranslate.Models;

namespace OverTranslate.Services.Performance;

/// <summary>The three resource levels. Ultra Low is the one designed for a 4 GB machine.</summary>
internal enum PerformanceLevel
{
    UltraLow,
    Balanced,
    Quality,
}

/// <summary>
/// The numbers a performance level stands for. A pure value: nothing here reads the machine, the
/// settings or the clock, so every row of the table below can be a test.
/// </summary>
/// <remarks>
/// <para><b>Quality</b> is the original application's behaviour — the 1 minute model release, no
/// slow-down on a still screen, the 400 line session cache — so choosing it (or having a machine
/// big enough to be given it) changes nothing. The other two trade latency for memory, and only in
/// the places where the cost is small: a model that is not needed is let go sooner, and a region
/// that has shown nothing for a few seconds is looked at a little less often.</para>
///
/// <para>The values are decisions, not measurements. They have not been measured on the 4 GB target
/// machine, and PERFORMANCE_REPORT.md says so; the settings file can override the one that matters
/// most (<see cref="PerformanceSettings.StaticScanMaxDelayMs"/>).</para>
/// </remarks>
internal sealed record PerformanceProfile(
    PerformanceLevel Level,
    TimeSpan OcrIdleRelease,
    bool TrimAfterRelease,
    int BackoffStartPolls,
    TimeSpan BackoffStep,
    TimeSpan BackoffMax,
    TimeSpan InactivePollDelay,
    TimeSpan InactiveReleaseAfter,
    int SessionCacheLimit,
    int PersistentCacheEntries,
    TimeSpan PersistentCacheTtl,
    bool LowerPriority = false)
{
    /// <summary>Installed RAM below which <see cref="PerformanceMode.Auto"/> chooses Ultra Low (6 GiB).</summary>
    public const long UltraLowBelowBytes = 6L * 1024 * 1024 * 1024;

    /// <summary>Installed RAM below which <see cref="PerformanceMode.Auto"/> chooses Balanced (12 GiB).</summary>
    public const long BalancedBelowBytes = 12L * 1024 * 1024 * 1024;

    /// <summary>The longest <see cref="PerformanceSettings.StaticScanMaxDelayMs"/> is allowed to ask for.</summary>
    public const int MaxStaticDelayMs = 2000;

    public static readonly PerformanceProfile UltraLow = new(
        PerformanceLevel.UltraLow,
        OcrIdleRelease: TimeSpan.FromSeconds(20),
        TrimAfterRelease: true,
        BackoffStartPolls: 20,
        BackoffStep: TimeSpan.FromMilliseconds(75),
        BackoffMax: TimeSpan.FromMilliseconds(450),
        InactivePollDelay: TimeSpan.FromMilliseconds(1000),
        InactiveReleaseAfter: TimeSpan.FromSeconds(30),
        SessionCacheLimit: 300,
        PersistentCacheEntries: 1500,
        PersistentCacheTtl: TimeSpan.FromDays(14),
        LowerPriority: true);

    public static readonly PerformanceProfile Balanced = new(
        PerformanceLevel.Balanced,
        OcrIdleRelease: TimeSpan.FromSeconds(45),
        TrimAfterRelease: true,
        BackoffStartPolls: 40,
        BackoffStep: TimeSpan.FromMilliseconds(50),
        BackoffMax: TimeSpan.FromMilliseconds(250),
        InactivePollDelay: TimeSpan.FromMilliseconds(750),
        InactiveReleaseAfter: TimeSpan.FromSeconds(60),
        SessionCacheLimit: 400,
        PersistentCacheEntries: 3000,
        PersistentCacheTtl: TimeSpan.FromDays(30),
        LowerPriority: true);

    public static readonly PerformanceProfile Quality = new(
        PerformanceLevel.Quality,
        OcrIdleRelease: TimeSpan.FromMinutes(1),
        TrimAfterRelease: false,
        BackoffStartPolls: int.MaxValue,
        BackoffStep: TimeSpan.Zero,
        BackoffMax: TimeSpan.Zero,
        InactivePollDelay: TimeSpan.FromMilliseconds(500),
        InactiveReleaseAfter: TimeSpan.FromMinutes(2),
        SessionCacheLimit: 400,
        PersistentCacheEntries: 5000,
        PersistentCacheTtl: TimeSpan.FromDays(60),
        LowerPriority: false);

    public static PerformanceProfile For(PerformanceLevel level) => level switch
    {
        PerformanceLevel.UltraLow => UltraLow,
        PerformanceLevel.Balanced => Balanced,
        _ => Quality,
    };

    /// <summary>
    /// The level a machine with this much RAM is given under <see cref="PerformanceMode.Auto"/>.
    /// Zero or negative — the question could not be answered — is Balanced rather than a guess in
    /// either direction.
    /// </summary>
    public static PerformanceLevel LevelForRam(long totalPhysicalBytes) =>
        totalPhysicalBytes <= 0 ? PerformanceLevel.Balanced :
        totalPhysicalBytes < UltraLowBelowBytes ? PerformanceLevel.UltraLow :
        totalPhysicalBytes < BalancedBelowBytes ? PerformanceLevel.Balanced :
        PerformanceLevel.Quality;

    /// <summary>The level the user's setting resolves to on this machine.</summary>
    public static PerformanceLevel Resolve(PerformanceMode mode, long totalPhysicalBytes) => mode switch
    {
        PerformanceMode.UltraLow => PerformanceLevel.UltraLow,
        PerformanceMode.Balanced => PerformanceLevel.Balanced,
        PerformanceMode.Quality => PerformanceLevel.Quality,
        _ => LevelForRam(totalPhysicalBytes),
    };

    /// <summary>
    /// The profile for a settings value on a machine of this size, with the user's own still-screen
    /// delay laid over it when they gave one.
    /// </summary>
    public static PerformanceProfile From(PerformanceSettings settings, long totalPhysicalBytes)
    {
        var profile = For(Resolve(settings.Mode, totalPhysicalBytes));
        if (settings.StaticScanMaxDelayMs is not { } ms) return profile;

        ms = Math.Clamp(ms, 0, MaxStaticDelayMs);
        if (ms == 0) return profile with { BackoffMax = TimeSpan.Zero };

        // The slow-down has to be able to start for a user who asks for one on the Quality profile,
        // which has it switched off; the step is a sixth of the ceiling so it still ramps rather
        // than jumping.
        return profile with
        {
            BackoffMax = TimeSpan.FromMilliseconds(ms),
            BackoffStartPolls = Math.Min(profile.BackoffStartPolls, 40),
            BackoffStep = profile.BackoffStep > TimeSpan.Zero
                ? profile.BackoffStep
                : TimeSpan.FromMilliseconds(Math.Max(10, ms / 6)),
        };
    }
}
