namespace DesktopPet1Refined.NaturalMotion.Models;

public sealed record PoseProfileDocument
{
    public int SchemaVersion { get; init; } = 1;

    public IReadOnlyList<PoseProfile> Poses { get; init; } = [];
}

public sealed record PoseProfile
{
    public required string PoseId { get; init; }

    public required string File { get; init; }

    public MotionSize CanvasSize { get; init; } = new(512, 512);

    public required MotionRect VisualBounds { get; init; }

    public MotionPoint? HeadReference { get; init; }

    public MotionPoint? BodyReference { get; init; }

    public MotionPoint? FootAnchor { get; init; }

    public MotionPoint? SeatAnchor { get; init; }

    public MotionPoint? DeskAnchor { get; init; }

    public double DisplayOffsetX { get; init; }

    public double DisplayOffsetY { get; init; }

    public PoseHitTestProfile HitTest { get; init; } = new();
}

public sealed record PoseHitTestProfile
{
    public string Source { get; init; } = "alpha";

    public byte AlphaThreshold { get; init; } = 16;
}
