using DesktopPet.Core.Assets;

namespace DesktopPet.Core.Animation;

public sealed record AnimationManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public PixelSize CanvasSize { get; init; } = new(512, 512);

    public int DefaultFrameDurationMs { get; init; } = 83;

    public int FrameCount { get; init; }

    public IReadOnlyList<AnimationClip> Clips { get; init; } = Array.Empty<AnimationClip>();
}

public sealed record AnimationClip
{
    public required string Id { get; init; }

    public bool Loop { get; init; }

    public required string FallbackPoseId { get; init; }

    public IReadOnlyList<AnimationFrame> Frames { get; init; } = Array.Empty<AnimationFrame>();

    public long TotalDurationMs => Frames.Aggregate(0L, (total, frame) =>
        checked(total + frame.DurationMs));
}

public sealed record AnimationFrame
{
    public required string File { get; init; }

    public int DurationMs { get; init; } = 83;

    public PixelRect VisualBounds { get; init; }

    public NormalizedPoint Anchor { get; init; } = new(0.5, 0.90625);

    public bool Interruptible { get; init; } = true;

    public required string SourcePoseId { get; init; }
}
