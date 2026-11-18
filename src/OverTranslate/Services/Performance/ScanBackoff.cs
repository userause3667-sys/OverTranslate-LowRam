namespace OverTranslate.Services.Performance;

/// <summary>
/// How much longer than usual a realtime region waits before its next look, given how long it has
/// had nothing to read.
/// </summary>
/// <remarks>
/// One per region loop, touched only by that loop. Every poll that finds the picture unchanged, or
/// finds only a picture the cheap text gate turns away, counts as idle; after
/// <see cref="PerformanceProfile.BackoffStartPolls"/> of them in a row the loop adds a growing wait,
/// capped at <see cref="PerformanceProfile.BackoffMax"/>. The first poll that has something to read
/// puts it back to the normal rhythm at once, so what this costs is bounded by the cap: the first
/// look at new text arrives at most that much later than it would have. It never makes a look
/// happen that would not have, so it cannot change what is read — only when.
/// </remarks>
internal sealed class ScanBackoff(PerformanceProfile profile)
{
    private int _idlePolls;

    /// <summary>Consecutive idle polls so far.</summary>
    public int IdlePolls => _idlePolls;

    /// <summary>A poll found nothing to read. Returns how long to wait on top of the usual interval.</summary>
    public TimeSpan OnIdlePoll()
    {
        if (_idlePolls < int.MaxValue) _idlePolls++;

        if (profile.BackoffMax <= TimeSpan.Zero || _idlePolls < profile.BackoffStartPolls)
            return TimeSpan.Zero;

        var steps = (long)_idlePolls - profile.BackoffStartPolls + 1;
        var ticks = profile.BackoffStep.Ticks * steps;
        return ticks >= profile.BackoffMax.Ticks || ticks < 0 ? profile.BackoffMax : TimeSpan.FromTicks(ticks);
    }

    /// <summary>A poll had something to read: back to the normal rhythm.</summary>
    public void OnActivity() => _idlePolls = 0;
}
