using DesktopPet.Core.Behavior;

namespace DesktopPet.Core.Animation;

public sealed record BehaviorAnimationPlan(
    string StateId,
    string ClipId);

public static class PlannedAnimationCatalog
{
    public static IReadOnlyList<string> RequiredClipIds { get; } =
    [
        "boot.enter", "idle.loop", "sit.enter", "sit.loop", "sit.exit",
        "read.enter", "read.loop", "read.surprise", "read.exit",
        "sleep.enter", "sleep.loop", "sleep.exit", "heart.play", "wave.play",
        "write.enter", "write.loop", "write.exit", "drag.loop", "fall.play", "landing.play"
    ];

    public static IReadOnlyDictionary<string, BehaviorAnimationPlan> CreateDefault()
    {
        var plans = new[]
        {
            Plan(PlannedBehaviorCatalog.Startup, "idle.loop"),
            Plan(PlannedBehaviorCatalog.Idle, "idle.loop"),
            Plan(PlannedBehaviorCatalog.Sitting, "sit.loop"),
            Plan(PlannedBehaviorCatalog.Reading, "read.loop"),
            Plan(PlannedBehaviorCatalog.Sleeping, "sleep.loop"),
            Plan(PlannedBehaviorCatalog.Heart, "heart.play"),
            Plan(PlannedBehaviorCatalog.Waving, "wave.play"),
            Plan(PlannedBehaviorCatalog.Writing, "write.loop"),
            Plan(PlannedBehaviorCatalog.Dragging, "drag.loop"),
            Plan(PlannedBehaviorCatalog.Falling, "fall.play")
        };
        return plans.ToDictionary(plan => plan.StateId, StringComparer.Ordinal);
    }

    private static BehaviorAnimationPlan Plan(string stateId, string clipId) =>
        new(stateId, clipId);
}
