namespace DesktopPet.Core.Animation;

public readonly record struct AnimationFrameSample(
    string ClipId,
    int FrameIndex,
    AnimationFrame Frame,
    long ClipElapsedMs,
    bool IsComplete);

/// <summary>Monotonic frame sampler that skips late frames rather than replaying them.</summary>
public sealed class AnimationClipPlayer
{
    private readonly long[] _frameStarts;
    private long _lastObservedMs;

    public AnimationClipPlayer(AnimationClip clip, long startedAtMs = 0)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (startedAtMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startedAtMs));
        }
        Clip = clip;
        StartedAtMs = startedAtMs;
        _lastObservedMs = startedAtMs;
        _frameStarts = new long[clip.Frames.Count];
        var cursor = 0L;
        for (var index = 0; index < clip.Frames.Count; index++)
        {
            _frameStarts[index] = cursor;
            cursor = checked(cursor + clip.Frames[index].DurationMs);
        }
    }

    public AnimationClip Clip { get; }

    public long StartedAtMs { get; }

    public AnimationFrameSample Sample(long nowMs)
    {
        if (nowMs < _lastObservedMs)
        {
            throw new ArgumentOutOfRangeException(nameof(nowMs), "Animation time must be monotonic.");
        }
        _lastObservedMs = nowMs;
        var elapsed = nowMs - StartedAtMs;
        var complete = !Clip.Loop && elapsed >= Clip.TotalDurationMs;
        var position = complete
            ? Clip.TotalDurationMs - 1
            : Clip.Loop
                ? elapsed % Clip.TotalDurationMs
                : elapsed;
        var frameIndex = FindFrame(position);
        return new AnimationFrameSample(Clip.Id, frameIndex, Clip.Frames[frameIndex], elapsed, complete);
    }

    private int FindFrame(long position)
    {
        for (var index = 0; index < Clip.Frames.Count; index++)
        {
            if (position < _frameStarts[index] + Clip.Frames[index].DurationMs)
            {
                return index;
            }
        }
        return Clip.Frames.Count - 1;
    }
}
