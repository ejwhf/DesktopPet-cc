namespace DesktopPet.Core.Configuration;

public enum HitTestMode
{
    CharacterPixels,
    EntireWindow,
    ClickThrough
}

/// <summary>Persisted user preferences. Coordinates are WPF logical pixels.</summary>
public sealed record PetSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public double Scale { get; init; } = 1;

    public double Opacity { get; init; } = 1;

    public bool AlwaysOnTop { get; init; } = true;

    public bool LockPosition { get; init; }

    public HitTestMode HitTestMode { get; init; } = HitTestMode.CharacterPixels;

    public double ActionFrequencyMultiplier { get; init; } = 1;

    public int SleepAfterMinutes { get; init; } = 20;

    public bool ReduceMotion { get; init; }

    public bool SoundEnabled { get; init; } = true;

    public bool StartWithWindows { get; init; }

    public WindowPlacement Placement { get; init; } = new();
}

public sealed record WindowPlacement
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public string? MonitorId { get; init; }

    public bool MirrorHorizontally { get; init; }
}
