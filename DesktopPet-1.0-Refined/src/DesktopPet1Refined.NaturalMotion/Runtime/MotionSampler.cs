using DesktopPet1Refined.NaturalMotion.Models;

namespace DesktopPet1Refined.NaturalMotion.Runtime;

public readonly record struct MotionValueKey(string Target, MotionProperty Property);

public sealed record MotionSample(
    double TimeMs,
    int CompletedLoops,
    bool IsComplete,
    IReadOnlyDictionary<MotionValueKey, double> Values);

public static class MotionSampler
{
    public static MotionSample Sample(MotionDefinition motion, double elapsedMs)
    {
        var safeElapsed = Math.Max(0, double.IsFinite(elapsedMs) ? elapsedMs : 0);
        var duration = motion.DurationMs;
        var completedLoops = (int)Math.Min(int.MaxValue, Math.Floor(safeElapsed / duration));
        var complete = motion.LoopMode == MotionLoopMode.Once && safeElapsed >= duration;
        var time = motion.LoopMode switch
        {
            MotionLoopMode.Once => Math.Min(safeElapsed, duration),
            MotionLoopMode.Loop => safeElapsed % duration,
            MotionLoopMode.PingPong => PingPongTime(safeElapsed, duration),
            _ => Math.Min(safeElapsed, duration)
        };

        var values = new Dictionary<MotionValueKey, double>();
        foreach (var track in motion.Tracks)
        {
            values[new MotionValueKey(track.Target, track.Property)] = SampleTrack(track, time);
        }
        return new MotionSample(time, completedLoops, complete, values);
    }

    private static double PingPongTime(double elapsed, double duration)
    {
        var cycleTime = elapsed % (duration * 2);
        return cycleTime <= duration ? cycleTime : (duration * 2) - cycleTime;
    }

    private static double SampleTrack(MotionTrack track, double timeMs)
    {
        var frames = track.Keyframes;
        if (timeMs <= frames[0].TimeMs)
        {
            return frames[0].Value;
        }
        for (var index = 1; index < frames.Count; index++)
        {
            var end = frames[index];
            if (timeMs > end.TimeMs)
            {
                continue;
            }
            var start = frames[index - 1];
            if (end.Easing == MotionEasing.Hold)
            {
                return start.Value;
            }
            var progress = (timeMs - start.TimeMs) / (end.TimeMs - start.TimeMs);
            var eased = ApplyEasing(progress, end.Easing);
            return start.Value + ((end.Value - start.Value) * eased);
        }
        return frames[^1].Value;
    }

    private static double ApplyEasing(double progress, MotionEasing easing)
    {
        var value = Math.Clamp(progress, 0, 1);
        return easing switch
        {
            MotionEasing.EaseIn => value * value,
            MotionEasing.EaseOut => 1 - ((1 - value) * (1 - value)),
            MotionEasing.EaseInOut => value < 0.5
                ? 2 * value * value
                : 1 - (Math.Pow(-2 * value + 2, 2) / 2),
            _ => value
        };
    }
}
