namespace DesktopPet.Core.Timeline;

/// <summary>Samples a validated timeline using caller-supplied monotonic milliseconds.</summary>
public sealed class TimelinePlayer
{
    private readonly long[] _cueStarts;
    private readonly long _loopStartMs;
    private readonly long _loopDurationMs;
    private long _lastObservedMs;

    public TimelinePlayer(ActionTimeline timeline, long startedAtMs = 0)
    {
        TimelineValidator.Validate(timeline);
        if (startedAtMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAtMs));
        }

        Timeline = timeline;
        StartedAtMs = startedAtMs;
        _lastObservedMs = startedAtMs;
        _cueStarts = new long[timeline.Cues.Count];

        var cursor = 0L;
        for (var index = 0; index < timeline.Cues.Count; index++)
        {
            _cueStarts[index] = cursor;
            cursor = checked(cursor + timeline.Cues[index].DurationMs);
        }

        _loopStartMs = _cueStarts[timeline.LoopStartIndex];
        _loopDurationMs = timeline.TotalDurationMs - _loopStartMs;
    }

    public ActionTimeline Timeline { get; }

    public long StartedAtMs { get; private set; }

    public void Restart(long nowMs)
    {
        ObserveTime(nowMs);
        StartedAtMs = nowMs;
    }

    public TimelineSample Sample(long nowMs)
    {
        ObserveTime(nowMs);
        var elapsed = nowMs - StartedAtMs;
        var total = Timeline.TotalDurationMs;
        var complete = !Timeline.Loop && elapsed >= total;
        long position;

        if (complete)
        {
            position = total;
        }
        else if (Timeline.Loop && elapsed >= _loopStartMs)
        {
            position = _loopStartMs + ((elapsed - _loopStartMs) % _loopDurationMs);
        }
        else
        {
            position = elapsed;
        }

        var cueIndex = FindCue(position, complete);
        var cue = Timeline.Cues[cueIndex];
        var cueElapsedLong = complete && cueIndex == Timeline.Cues.Count - 1
            ? cue.DurationMs
            : position - _cueStarts[cueIndex];
        var cueElapsed = checked((int)cueElapsedLong);
        var rawProgress = (double)cueElapsed / cue.DurationMs;
        var easedProgress = ApplyEasing(cue.Easing, rawProgress);

        return new TimelineSample(
            Timeline.Id,
            cue.PoseId,
            cueIndex,
            elapsed,
            cueElapsed,
            Math.Clamp(rawProgress, 0, 1),
            PoseTransform.Interpolate(cue.From, cue.To, easedProgress),
            cue.Interruptible,
            complete);
    }

    private int FindCue(long position, bool complete)
    {
        if (complete)
        {
            return Timeline.Cues.Count - 1;
        }

        // Cue counts are deliberately tiny; a linear scan is clearer and faster than allocations.
        for (var index = 0; index < Timeline.Cues.Count; index++)
        {
            if (position < _cueStarts[index] + Timeline.Cues[index].DurationMs)
            {
                return index;
            }
        }

        return Timeline.Cues.Count - 1;
    }

    private void ObserveTime(long nowMs)
    {
        if (nowMs < _lastObservedMs)
        {
            throw new ArgumentOutOfRangeException(nameof(nowMs), "Timeline time must be monotonic.");
        }

        _lastObservedMs = nowMs;
    }

    private static double ApplyEasing(EasingKind easing, double progress)
    {
        var clamped = Math.Clamp(progress, 0, 1);
        return easing switch
        {
            EasingKind.Step => clamped >= 1 ? 1 : 0,
            EasingKind.Linear => clamped,
            EasingKind.EaseInOut => clamped * clamped * (3 - (2 * clamped)),
            _ => throw new ArgumentOutOfRangeException(nameof(easing))
        };
    }
}
