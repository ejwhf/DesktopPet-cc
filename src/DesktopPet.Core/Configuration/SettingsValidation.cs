namespace DesktopPet.Core.Configuration;

public sealed record SettingsValidationIssue(string Path, string Message);

public sealed record SettingsValidationResult(IReadOnlyList<SettingsValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SettingsValidationException(Issues);
        }
    }
}

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(IReadOnlyList<SettingsValidationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Path}: {issue.Message}")))
    {
        Issues = issues;
    }

    public IReadOnlyList<SettingsValidationIssue> Issues { get; }
}

public static class PetSettingsValidator
{
    public static SettingsValidationResult Validate(PetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<SettingsValidationIssue>();

        if (settings.SchemaVersion != PetSettings.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", $"Expected {PetSettings.CurrentSchemaVersion}."));
        }

        InRange(settings.Scale, 0.5, 2, "scale", issues);
        InRange(settings.Opacity, 0.2, 1, "opacity", issues);
        InRange(settings.ActionFrequencyMultiplier, 0.25, 4, "actionFrequencyMultiplier", issues);

        if (settings.SleepAfterMinutes is < 1 or > 24 * 60)
        {
            issues.Add(new("sleepAfterMinutes", "Value must be between 1 and 1440 minutes."));
        }

        if (!Enum.IsDefined(settings.HitTestMode))
        {
            issues.Add(new("hitTestMode", "Value is not a supported hit-test mode."));
        }

        if (settings.Placement is null)
        {
            issues.Add(new("placement", "Placement cannot be null."));
        }
        else
        {
            ValidateNullableCoordinate(settings.Placement.Left, "placement.left", issues);
            ValidateNullableCoordinate(settings.Placement.Top, "placement.top", issues);
            if (settings.Placement.MonitorId is { Length: > 512 })
            {
                issues.Add(new("placement.monitorId", "Monitor id cannot exceed 512 characters."));
            }
        }

        return new(issues.AsReadOnly());
    }

    private static void InRange(
        double value,
        double minimum,
        double maximum,
        string path,
        ICollection<SettingsValidationIssue> issues)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            issues.Add(new(path, $"Value must be finite and between {minimum} and {maximum}."));
        }
    }

    private static void ValidateNullableCoordinate(
        double? value,
        string path,
        ICollection<SettingsValidationIssue> issues)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
        {
            issues.Add(new(path, "Coordinate must be finite when present."));
        }
    }
}
