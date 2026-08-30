using DesktopPet.Core.Behavior;

namespace DesktopPet.Core.Timeline;

public enum EasingKind
{
    Step,
    Linear,
    EaseInOut
}

/// <summary>Lightweight transform applied to a complete pose bitmap.</summary>
public readonly record struct PoseTransform(
    double OffsetX = 0,
    double OffsetY = 0,
    double Scale = 1,
    double RotationDegrees = 0,
    double Opacity = 1)
{
    // A parameterless value-type construction zeroes every field; it does not apply
    // the optional primary-constructor values. Keep the visual identity explicit.
    public static PoseTransform Identity => new(
        OffsetX: 0,
        OffsetY: 0,
        Scale: 1,
        RotationDegrees: 0,
        Opacity: 1);

    public bool IsValid =>
        double.IsFinite(OffsetX) &&
        double.IsFinite(OffsetY) &&
        double.IsFinite(Scale) &&
        Scale > 0 &&
        double.IsFinite(RotationDegrees) &&
        double.IsFinite(Opacity) &&
        Opacity is >= 0 and <= 1;

    public static PoseTransform Interpolate(PoseTransform start, PoseTransform end, double progress) => new(
        Lerp(start.OffsetX, end.OffsetX, progress),
        Lerp(start.OffsetY, end.OffsetY, progress),
        Lerp(start.Scale, end.Scale, progress),
        Lerp(start.RotationDegrees, end.RotationDegrees, progress),
        Lerp(start.Opacity, end.Opacity, progress));

    private static double Lerp(double start, double end, double progress) =>
        start + ((end - start) * Math.Clamp(progress, 0, 1));
}

/// <summary>A timed display of one of the approved static poses.</summary>
public sealed record PoseCue
{
    public required string PoseId { get; init; }

    public int DurationMs { get; init; } = 1_000;

    public PoseTransform From { get; init; } = PoseTransform.Identity;

    public PoseTransform To { get; init; } = PoseTransform.Identity;

    public EasingKind Easing { get; init; } = EasingKind.EaseInOut;

    public bool Interruptible { get; init; } = true;
}

/// <summary>
/// A sequence may have a non-looping prelude followed by a loop beginning at LoopStartIndex.
/// </summary>
public sealed record ActionTimeline
{
    public required string Id { get; init; }

    public PetMode Mode { get; init; }

    public IReadOnlyList<PoseCue> Cues { get; init; } = Array.Empty<PoseCue>();

    public bool Loop { get; init; }

    public int LoopStartIndex { get; init; }

    public long TotalDurationMs => Cues.Aggregate(0L, (total, cue) => checked(total + cue.DurationMs));
}

public readonly record struct TimelineSample(
    string TimelineId,
    string PoseId,
    int CueIndex,
    long TimelineElapsedMs,
    int CueElapsedMs,
    double CueProgress,
    PoseTransform Transform,
    bool Interruptible,
    bool IsComplete);

public sealed class TimelineDefinitionException : ArgumentException
{
    public TimelineDefinitionException(string message)
        : base(message)
    {
    }
}

public static class TimelineValidator
{
    public static void Validate(ActionTimeline timeline, ISet<string>? knownPoseIds = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (string.IsNullOrWhiteSpace(timeline.Id))
        {
            throw new TimelineDefinitionException("Timeline id is required.");
        }

        if (timeline.Cues is null)
        {
            throw new TimelineDefinitionException($"Timeline '{timeline.Id}' cue collection cannot be null.");
        }

        if (timeline.Cues.Count == 0)
        {
            throw new TimelineDefinitionException($"Timeline '{timeline.Id}' has no cues.");
        }

        if (timeline.LoopStartIndex < 0 || timeline.LoopStartIndex >= timeline.Cues.Count)
        {
            throw new TimelineDefinitionException(
                $"Timeline '{timeline.Id}' has an invalid loop-start index.");
        }

        for (var index = 0; index < timeline.Cues.Count; index++)
        {
            var cue = timeline.Cues[index];
            if (cue is null)
            {
                throw new TimelineDefinitionException($"Timeline '{timeline.Id}' cue {index} cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(cue.PoseId))
            {
                throw new TimelineDefinitionException($"Timeline '{timeline.Id}' cue {index} has no pose id.");
            }

            if (knownPoseIds is not null && !knownPoseIds.Contains(cue.PoseId))
            {
                throw new TimelineDefinitionException(
                    $"Timeline '{timeline.Id}' cue {index} references unknown pose '{cue.PoseId}'.");
            }

            if (cue.DurationMs <= 0)
            {
                throw new TimelineDefinitionException(
                    $"Timeline '{timeline.Id}' cue {index} must have positive duration.");
            }

            if (!cue.From.IsValid || !cue.To.IsValid)
            {
                throw new TimelineDefinitionException(
                    $"Timeline '{timeline.Id}' cue {index} has an invalid transform.");
            }
        }

        try
        {
            _ = timeline.TotalDurationMs;
        }
        catch (OverflowException exception)
        {
            throw new TimelineDefinitionException(
                $"Timeline '{timeline.Id}' has a duration that exceeds Int64 capacity: {exception.Message}");
        }
    }
}
