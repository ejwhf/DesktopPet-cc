using DesktopPet.Core.Assets;
using DesktopPet.Core.Behavior;

namespace DesktopPet.Core.Timeline;

/// <summary>Canonical MVP arrangements for all eighteen approved poses.</summary>
public static class PlannedActionCatalog
{
    public const string Startup = PlannedBehaviorCatalog.Startup;
    public const string Idle = PlannedBehaviorCatalog.Idle;
    public const string Sitting = PlannedBehaviorCatalog.Sitting;
    public const string Reading = PlannedBehaviorCatalog.Reading;
    public const string Sleeping = PlannedBehaviorCatalog.Sleeping;
    public const string Heart = PlannedBehaviorCatalog.Heart;
    public const string Waving = PlannedBehaviorCatalog.Waving;
    public const string Writing = PlannedBehaviorCatalog.Writing;
    public const string Dragging = PlannedBehaviorCatalog.Dragging;
    public const string Falling = PlannedBehaviorCatalog.Falling;

    public static IReadOnlyDictionary<string, ActionTimeline> CreateDefault()
    {
        var timelines = new[]
        {
            Timeline(Startup, PetMode.Boot, false,
                Cue(PoseIds.PeekScreenLeft, 900, interruptible: false),
                Cue(PoseIds.IdleNeutral, 900)),
            Timeline(Idle, PetMode.Idle, true,
                Cue(PoseIds.IdleNeutral, 1_600, scaleFrom: 0.995, scaleTo: 1.005),
                Cue(PoseIds.IdleAlternate, 1_200, scaleFrom: 1.005, scaleTo: 0.995),
                Cue(PoseIds.IdleBlinkSmile, 220, easing: EasingKind.Step),
                Cue(PoseIds.IdleNeutral, 1_400, scaleFrom: 0.995, scaleTo: 1.005)),
            Timeline(Sitting, PetMode.Sitting, true,
                Cue(PoseIds.SitEdgeIdle, 2_200, offsetFrom: 0, offsetTo: -1),
                Cue(PoseIds.SitEdgeAlternate, 2_200, offsetFrom: -1, offsetTo: 0)),
            Timeline(Reading, PetMode.Reading, true,
                Cue(PoseIds.ReadSmall, 900),
                Cue(PoseIds.ReadHold, 2_800, scaleFrom: 0.997, scaleTo: 1.003),
                Cue(PoseIds.ReadSurprised, 650, interruptible: false),
                Cue(PoseIds.ReadHold, 1_800, scaleFrom: 1.003, scaleTo: 0.997)),
            Timeline(Sleeping, PetMode.Sleeping, true,
                Cue(PoseIds.SleepSeated, 2_400, scaleFrom: 0.995, scaleTo: 1.005),
                Cue(PoseIds.SleepSeatedAlternate, 2_400, scaleFrom: 1.005, scaleTo: 0.995)),
            Timeline(Heart, PetMode.Heart, false,
                Cue(PoseIds.Heart, 650, interruptible: false, scaleFrom: 0.995, scaleTo: 1.005),
                Cue(PoseIds.HeartRaise, 850, interruptible: false, scaleFrom: 1.005, scaleTo: 1),
                Cue(PoseIds.IdleNeutral, 500)),
            Timeline(Waving, PetMode.Waving, false,
                Cue(PoseIds.WaveSingleB, 500, interruptible: false),
                Cue(PoseIds.WaveSingleA, 420, interruptible: false, rotationFrom: -1, rotationTo: 1),
                Cue(PoseIds.CheerBothHands, 420, interruptible: false, rotationFrom: 1, rotationTo: -1),
                Cue(PoseIds.WaveSingleA, 420, interruptible: false, rotationFrom: -1, rotationTo: 0),
                Cue(PoseIds.IdleNeutral, 500)),
            Timeline(Writing, PetMode.Writing, true,
                Cue(PoseIds.WriteProne, 2_100, offsetFrom: 0, offsetTo: 1),
                Cue(PoseIds.WriteProne, 2_100, offsetFrom: 1, offsetTo: 0)),
            Timeline(Dragging, PetMode.Dragging, true,
                Cue(PoseIds.FallingReach, 500, scaleFrom: 1, scaleTo: 1.005)),
            Timeline(Falling, PetMode.Falling, false,
                Cue(PoseIds.FallingReach, 700, interruptible: false, offsetFrom: -3, offsetTo: 4,
                    rotationFrom: -3, rotationTo: 3),
                Cue(PoseIds.IdleNeutral, 450, interruptible: false, scaleFrom: 1.005, scaleTo: 1))
        };

        var knownPoses = PoseIds.All.ToHashSet(StringComparer.Ordinal);
        foreach (var timeline in timelines)
        {
            TimelineValidator.Validate(timeline, knownPoses);
        }

        return timelines.ToDictionary(timeline => timeline.Id, StringComparer.Ordinal);
    }

    private static ActionTimeline Timeline(
        string id,
        PetMode mode,
        bool loop,
        params PoseCue[] cues) => new()
        {
            Id = id,
            Mode = mode,
            Loop = loop,
            LoopStartIndex = 0,
            Cues = cues
        };

    private static PoseCue Cue(
        string poseId,
        int durationMs,
        bool interruptible = true,
        double scaleFrom = 1,
        double scaleTo = 1,
        double offsetFrom = 0,
        double offsetTo = 0,
        double rotationFrom = 0,
        double rotationTo = 0,
        EasingKind easing = EasingKind.EaseInOut) => new()
        {
            PoseId = poseId,
            DurationMs = durationMs,
            Interruptible = interruptible,
            Easing = easing,
            From = new PoseTransform(0, offsetFrom, scaleFrom, rotationFrom),
            To = new PoseTransform(0, offsetTo, scaleTo, rotationTo)
        };
}
