namespace DesktopPet.Core.Assets;

/// <summary>Versioned collection of all renderable pet poses.</summary>
public sealed record AssetManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Optional character namespace for future multi-character manifests.</summary>
    public string? CharacterId { get; init; }

    /// <summary>Default canvas size inherited by entries that omit their own sourceSize.</summary>
    public PixelSize? SourceSize { get; init; }

    public int? HitMaskAlphaThreshold { get; init; }

    public string? SourceSheet { get; init; }

    public AssetNormalization? Normalization { get; init; }

    public IReadOnlyList<PetAssetDefinition> Assets { get; init; } = Array.Empty<PetAssetDefinition>();
}

/// <summary>Records the production pipeline's scale-normalization parameters.</summary>
public sealed record AssetNormalization
{
    public string? MasterAssetId { get; init; }

    public int? MasterFaceWidthPx { get; init; }

    public int? TargetFaceWidthPx { get; init; }

    /// <summary>Common visible pixel height applied to complete standing poses.</summary>
    public int? TargetStandingHeightPx { get; init; }

    public int? SafetyMarginPx { get; init; }
}

/// <summary>A pose image and the normalized anchors required by the desktop shell.</summary>
public sealed record PetAssetDefinition
{
    public required string Id { get; init; }

    /// <summary>Path to the RGBA sprite, relative to the asset root.</summary>
    public required string File { get; init; }

    public PixelSize SourceSize { get; init; }

    public PixelRect VisualBounds { get; init; }

    public NormalizedPoint Pivot { get; init; } = new(0.5, 0.5);

    public NormalizedPoint? GroundAnchor { get; init; }

    public NormalizedPoint? SeatAnchor { get; init; }

    public NormalizedPoint? FloorAnchor { get; init; }

    public NormalizedPoint? EdgeAnchor { get; init; }

    public NormalizedPoint? NeckAnchor { get; init; }

    public string? HitMask { get; init; }

    public int DurationMs { get; init; } = 1_200;

    public bool Interruptible { get; init; } = true;

    public double Weight { get; init; } = 1;

    public int CooldownMs { get; init; }

    public IReadOnlyList<string> NextStates { get; init; } = Array.Empty<string>();
}

/// <summary>Stable identifiers for the eighteen approved key poses.</summary>
public static class PoseIds
{
    public const string PeekScreenLeft = "peek.screen_left";
    public const string FallingReach = "falling.reach";
    public const string SitEdgeIdle = "sit.edge_idle";
    public const string SitEdgeAlternate = "sit.edge_alt";
    public const string IdleBlinkSmile = "idle.blink_smile";
    public const string IdleNeutral = "idle.neutral";
    public const string IdleAlternate = "idle.alt";
    public const string ReadSmall = "read.small";
    public const string ReadHold = "read.hold";
    public const string SleepSeated = "sleep.seated";
    public const string Heart = "heart";
    public const string ReadSurprised = "read.surprised";
    public const string SleepSeatedAlternate = "sleep.seated_alt";
    public const string HeartRaise = "heart.raise";
    public const string WaveSingleA = "wave.single_a";
    public const string CheerBothHands = "cheer.both_hands";
    public const string WaveSingleB = "wave.single_b";
    public const string WriteProne = "write.prone";

    public static IReadOnlyList<string> All { get; } =
    [
        PeekScreenLeft,
        FallingReach,
        SitEdgeIdle,
        SitEdgeAlternate,
        IdleBlinkSmile,
        IdleNeutral,
        IdleAlternate,
        ReadSmall,
        ReadHold,
        SleepSeated,
        Heart,
        ReadSurprised,
        SleepSeatedAlternate,
        HeartRaise,
        WaveSingleA,
        CheerBothHands,
        WaveSingleB,
        WriteProne
    ];
}
