namespace OverTranslate.Services.Performance;

/// <summary>What a realtime loop should do about the window it watches being inactive.</summary>
/// <param name="Skip">Do not grab or read this poll.</param>
/// <param name="ReleaseModel">Inactive for long enough that the recognition model should be let go (once per spell).</param>
/// <param name="Resumed">Active again after a spell in which the model was let go (once per spell).</param>
internal readonly record struct SourceStep(bool Skip, bool ReleaseModel, bool Resumed);

/// <summary>
/// Decides, from whether the watched window is active, when a session should stop reading and when
/// it should give the recognition model back.
/// </summary>
/// <remarks>
/// Shared by a session's region loops, so it takes a lock; the work under it is a handful of
/// comparisons. The clock is a <see cref="TimeProvider"/> so a test can move it.
/// </remarks>
internal sealed class SourceInactivityTracker(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private long? _inactiveSince;
    private bool _released;

    /// <param name="active">Whether the watched window is on screen (and in front, if that was asked for).</param>
    /// <param name="releaseAfter">How long inactivity lasts before <see cref="SourceStep.ReleaseModel"/> is raised.</param>
    public SourceStep Evaluate(bool active, TimeSpan releaseAfter)
    {
        lock (_gate)
        {
            if (active)
            {
                var resumed = _inactiveSince is not null && _released;
                _inactiveSince = null;
                _released = false;
                return new SourceStep(false, false, resumed);
            }

            var now = _clock.GetTimestamp();
            _inactiveSince ??= now;

            var release = false;
            if (!_released && _clock.GetElapsedTime(_inactiveSince.Value, now) >= releaseAfter)
            {
                _released = true;
                release = true;
            }

            return new SourceStep(true, release, false);
        }
    }
}
