namespace DesktopPet1Refined.App.Models;

public enum HitTestMode
{
    CharacterPixels,
    EntireWindow,
    ClickThrough
}

internal sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 3;

    public double Scale { get; init; } = 1;

    public double Opacity { get; init; } = 1;

    public bool AlwaysOnTop { get; init; } = true;

    public bool LockPosition { get; init; }

    public bool ReduceMotion { get; init; }

    public HitTestMode HitTestMode { get; init; } = HitTestMode.CharacterPixels;

    public double? Left { get; init; }

    public double? Top { get; init; }

    public string? MonitorId { get; init; }

    public AppSettings Normalize() => this with
    {
        SchemaVersion = 3,
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, 0.5, 2) : 1,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1) : 1,
        Left = Left is { } left && double.IsFinite(left) ? left : null,
        Top = Top is { } top && double.IsFinite(top) ? top : null,
        MonitorId = string.IsNullOrWhiteSpace(MonitorId) ? null : MonitorId
    };
}
