namespace DesktopPet1Refined.NaturalMotion.Models;

public enum MotionLoopMode
{
    Once,
    Loop,
    PingPong
}

public enum MotionTargetKind
{
    Layer,
    PartSprite,
    Window
}

public enum MotionProperty
{
    TranslateX,
    TranslateY,
    Rotate,
    Opacity,
    SpriteFrame
}

public enum MotionEasing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Hold
}

public enum MotionInterruptPolicy
{
    Immediate,
    FinishCurrentLoop,
    Locked
}

public enum MotionExitPolicy
{
    RestoreBasePose,
    HoldFinalFrame,
    HideThenRestore
}

public sealed record MotionDefinition
{
    public int SchemaVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required string BasePose { get; init; }

    public required double DurationMs { get; init; }

    public MotionLoopMode LoopMode { get; init; } = MotionLoopMode.Once;

    public IReadOnlyList<MotionTargetDefinition> Targets { get; init; } = [];

    public IReadOnlyList<MotionTrack> Tracks { get; init; } = [];

    public MotionInterruptPolicy InterruptPolicy { get; init; } = MotionInterruptPolicy.Immediate;

    public MotionExitPolicy ExitPolicy { get; init; } = MotionExitPolicy.RestoreBasePose;
}

public sealed record MotionTargetDefinition
{
    public required string Id { get; init; }

    public required MotionTargetKind Kind { get; init; }

    public string? Source { get; init; }

    public IReadOnlyList<string> SpriteFrames { get; init; } = [];

    public MotionRect? Bounds { get; init; }

    public MotionPoint? Anchor { get; init; }

    public MotionPoint? Pivot { get; init; }

    public int ZIndex { get; init; }
}

public sealed record MotionTrack
{
    public required string Target { get; init; }

    public required MotionProperty Property { get; init; }

    public IReadOnlyList<MotionKeyframe> Keyframes { get; init; } = [];
}

public sealed record MotionKeyframe
{
    public required double TimeMs { get; init; }

    public required double Value { get; init; }

    public MotionEasing Easing { get; init; } = MotionEasing.Linear;
}
