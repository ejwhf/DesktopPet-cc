namespace DesktopPet.Core.Behavior;

/// <summary>Default scheduler states corresponding to the MVP action timelines.</summary>
public static class PlannedBehaviorCatalog
{
    public const string Startup = "startup";
    public const string Idle = "idle";
    public const string Sitting = "sitting";
    public const string Reading = "reading";
    public const string Sleeping = "sleeping";
    public const string Heart = "heart";
    public const string Waving = "waving";
    public const string Writing = "writing";
    public const string Dragging = "dragging";
    public const string Falling = "falling";
    public const string Menu = "menu";
    public const string Hidden = "hidden";

    public static IReadOnlyList<BehaviorStateDefinition> CreateDefault() =>
    [
        State(Startup, PetMode.Boot, 1_800, 0, false, 0, Idle),
        State(Idle, PetMode.Idle, 4_000, 0, true, 8, Sitting, Reading, Sleeping, Writing),
        State(Sitting, PetMode.Sitting, 4_400, 20_000, true, 3, Idle, Reading),
        State(Reading, PetMode.Reading, 6_150, 35_000, true, 2, Idle),
        State(Sleeping, PetMode.Sleeping, 9_600, 60_000, true, 1, Idle),
        State(Heart, PetMode.Heart, 2_000, 8_000, false, 0, Idle),
        State(Waving, PetMode.Waving, 2_260, 8_000, false, 0, Idle),
        State(Writing, PetMode.Writing, 8_400, 45_000, true, 2, Idle),
        State(Dragging, PetMode.Dragging, 0, 0, true, 0, Falling, Idle, priority: 300),
        State(Falling, PetMode.Falling, 1_150, 0, false, 1, Idle, priority: 250),
        State(Menu, PetMode.Menu, 0, 0, true, 0, Idle, priority: 400),
        State(Hidden, PetMode.Hidden, 0, 0, true, 0, Idle, priority: 500)
    ];

    public static WeightedBehaviorStateMachine CreateStateMachine(ulong seed, long startedAtMs = 0) =>
        new(CreateDefault(), Startup, seed, startedAtMs);

    private static BehaviorStateDefinition State(
        string id,
        PetMode mode,
        long minimumDurationMs,
        long cooldownMs,
        bool interruptible,
        double weight,
        string nextState,
        int priority = 0) =>
        State(id, mode, minimumDurationMs, cooldownMs, interruptible, weight, [nextState], priority);

    private static BehaviorStateDefinition State(
        string id,
        PetMode mode,
        long minimumDurationMs,
        long cooldownMs,
        bool interruptible,
        double weight,
        string nextStateA,
        string nextStateB,
        int priority = 0) =>
        State(id, mode, minimumDurationMs, cooldownMs, interruptible, weight,
            [nextStateA, nextStateB], priority);

    private static BehaviorStateDefinition State(
        string id,
        PetMode mode,
        long minimumDurationMs,
        long cooldownMs,
        bool interruptible,
        double weight,
        string nextStateA,
        string nextStateB,
        string nextStateC,
        string nextStateD,
        int priority = 0) =>
        State(id, mode, minimumDurationMs, cooldownMs, interruptible, weight,
            [nextStateA, nextStateB, nextStateC, nextStateD], priority);

    private static BehaviorStateDefinition State(
        string id,
        PetMode mode,
        long minimumDurationMs,
        long cooldownMs,
        bool interruptible,
        double weight,
        IReadOnlyList<string> nextStates,
        int priority) => new()
        {
            Id = id,
            Mode = mode,
            MinimumDurationMs = minimumDurationMs,
            CooldownMs = cooldownMs,
            Interruptible = interruptible,
            Weight = weight,
            Priority = priority,
            NextStateIds = nextStates
        };
}
