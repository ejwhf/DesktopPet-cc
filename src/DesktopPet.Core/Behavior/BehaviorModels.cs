namespace DesktopPet.Core.Behavior;

/// <summary>Top-level modes used by the desktop shell and behavior scheduler.</summary>
public enum PetMode
{
    Boot,
    Peek,
    Idle,
    Sitting,
    Reading,
    Sleeping,
    Heart,
    Waving,
    Writing,
    Dragging,
    Falling,
    Menu,
    Hidden
}

/// <summary>Why a behavior transition was requested.</summary>
public enum TransitionCause
{
    Scheduled = 0,
    PlannedActivity = 100,
    DirectInteraction = 200,
    Drag = 300,
    Menu = 400,
    System = 500
}

public sealed record BehaviorStateDefinition
{
    public required string Id { get; init; }

    public PetMode Mode { get; init; }

    public int Priority { get; init; }

    public long MinimumDurationMs { get; init; }

    public long CooldownMs { get; init; }

    public bool Interruptible { get; init; } = true;

    public double Weight { get; init; } = 1;

    public IReadOnlyList<string> NextStateIds { get; init; } = Array.Empty<string>();
}

public readonly record struct TransitionRequest(
    string TargetStateId,
    TransitionCause Cause,
    int Priority = 0,
    bool RestartIfCurrent = false)
{
    public int EffectivePriority
    {
        get
        {
            var combined = (long)(int)Cause + Priority;
            return (int)Math.Clamp(combined, int.MinValue, int.MaxValue);
        }
    }
}

public sealed record BehaviorTransition(
    string? PreviousStateId,
    string CurrentStateId,
    long OccurredAtMs,
    TransitionCause Cause,
    int EffectivePriority);

public readonly record struct TransitionDecision(
    bool Accepted,
    BehaviorTransition? Transition,
    string? RejectionReason)
{
    public static TransitionDecision Reject(string reason) => new(false, null, reason);

    public static TransitionDecision Accept(BehaviorTransition transition) => new(true, transition, null);
}

public sealed class BehaviorDefinitionException : ArgumentException
{
    public BehaviorDefinitionException(string message)
        : base(message)
    {
    }
}
