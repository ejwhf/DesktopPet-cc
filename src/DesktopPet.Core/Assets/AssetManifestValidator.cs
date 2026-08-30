using System.Text.Json;
using DesktopPet.Core.Serialization;

namespace DesktopPet.Core.Assets;

public sealed record ManifestValidationIssue(string Path, string Message);

public sealed record ManifestValidationResult(IReadOnlyList<ManifestValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new AssetManifestValidationException(Issues);
        }
    }
}

public sealed class AssetManifestValidationException : Exception
{
    public AssetManifestValidationException(IReadOnlyList<ManifestValidationIssue> issues)
        : base(string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Path}: {issue.Message}")))
    {
        Issues = issues;
    }

    public IReadOnlyList<ManifestValidationIssue> Issues { get; }
}

public static class AssetManifestValidator
{
    public static ManifestValidationResult Validate(AssetManifest manifest, bool requireAllPlannedPoses = false)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<ManifestValidationIssue>();

        if (manifest.SchemaVersion != AssetManifest.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", $"Expected {AssetManifest.CurrentSchemaVersion}."));
        }

        if (manifest.CharacterId is not null && string.IsNullOrWhiteSpace(manifest.CharacterId))
        {
            issues.Add(new("characterId", "Character id cannot be blank when present."));
        }

        if (manifest.SourceSize is { IsPositive: false })
        {
            issues.Add(new("sourceSize", "Width and height must be positive when present."));
        }

        if (manifest.HitMaskAlphaThreshold is < 0 or > 255)
        {
            issues.Add(new("hitMaskAlphaThreshold", "Threshold must be between 0 and 255."));
        }

        if (manifest.SourceSheet is not null && string.IsNullOrWhiteSpace(manifest.SourceSheet))
        {
            issues.Add(new("sourceSheet", "Source-sheet path cannot be blank when present."));
        }

        if (manifest.Assets is null)
        {
            issues.Add(new("assets", "Asset collection cannot be null."));
            return new(issues.AsReadOnly());
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Assets.Count; index++)
        {
            var asset = manifest.Assets[index];
            var path = $"assets[{index}]";
            if (asset is null)
            {
                issues.Add(new(path, "Asset entry cannot be null."));
                continue;
            }

            ValidateAsset(asset, path, ids, issues);
        }

        ValidateNormalization(manifest.Normalization, ids, issues);

        for (var index = 0; index < manifest.Assets.Count; index++)
        {
            var asset = manifest.Assets[index];
            if (asset?.NextStates is null)
            {
                continue;
            }

            foreach (var nextState in asset.NextStates)
            {
                if (!ids.Contains(nextState))
                {
                    issues.Add(new($"assets[{index}].nextStates", $"Unknown pose id '{nextState}'."));
                }
            }
        }

        if (requireAllPlannedPoses)
        {
            foreach (var poseId in PoseIds.All.Where(poseId => !ids.Contains(poseId)))
            {
                issues.Add(new("assets", $"Required pose '{poseId}' is missing."));
            }
        }

        return new(issues.AsReadOnly());
    }

    public static async Task<AssetManifest> LoadAsync(
        string path,
        bool requireAllPlannedPoses = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var manifest = await JsonSerializer.DeserializeAsync<AssetManifest>(
            stream,
            CoreJson.CreateOptions(),
            cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Asset manifest is empty.");

        Validate(manifest, requireAllPlannedPoses).ThrowIfInvalid();
        return manifest;
    }

    private static void ValidateAsset(
        PetAssetDefinition asset,
        string path,
        ISet<string> ids,
        ICollection<ManifestValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(asset.Id))
        {
            issues.Add(new($"{path}.id", "Pose id is required."));
        }
        else if (!ids.Add(asset.Id))
        {
            issues.Add(new($"{path}.id", $"Duplicate pose id '{asset.Id}'."));
        }

        if (string.IsNullOrWhiteSpace(asset.File))
        {
            issues.Add(new($"{path}.file", "Image file is required."));
        }

        if (!asset.SourceSize.IsPositive)
        {
            issues.Add(new($"{path}.sourceSize", "Width and height must be positive."));
        }

        if (!asset.VisualBounds.IsPositive)
        {
            issues.Add(new($"{path}.visualBounds", "Width and height must be positive."));
        }
        else if (asset.VisualBounds.X < 0 || asset.VisualBounds.Y < 0 ||
                 (long)asset.VisualBounds.X + asset.VisualBounds.Width > asset.SourceSize.Width ||
                 (long)asset.VisualBounds.Y + asset.VisualBounds.Height > asset.SourceSize.Height)
        {
            issues.Add(new($"{path}.visualBounds", "Bounds must be contained by sourceSize."));
        }

        ValidateAnchor(asset.Pivot, $"{path}.pivot", issues);
        ValidateOptionalAnchor(asset.GroundAnchor, $"{path}.groundAnchor", issues);
        ValidateOptionalAnchor(asset.SeatAnchor, $"{path}.seatAnchor", issues);
        ValidateOptionalAnchor(asset.FloorAnchor, $"{path}.floorAnchor", issues);
        ValidateOptionalAnchor(asset.EdgeAnchor, $"{path}.edgeAnchor", issues);
        ValidateOptionalAnchor(asset.NeckAnchor, $"{path}.neckAnchor", issues);

        if (asset.DurationMs <= 0)
        {
            issues.Add(new($"{path}.durationMs", "Duration must be greater than zero."));
        }

        if (!double.IsFinite(asset.Weight) || asset.Weight < 0)
        {
            issues.Add(new($"{path}.weight", "Weight must be finite and non-negative."));
        }

        if (asset.CooldownMs < 0)
        {
            issues.Add(new($"{path}.cooldownMs", "Cooldown cannot be negative."));
        }

        if (asset.NextStates is null)
        {
            issues.Add(new($"{path}.nextStates", "State collection cannot be null."));
        }
        else if (asset.NextStates.Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(new($"{path}.nextStates", "State ids cannot be blank."));
        }
    }

    private static void ValidateOptionalAnchor(
        NormalizedPoint? anchor,
        string path,
        ICollection<ManifestValidationIssue> issues)
    {
        if (anchor.HasValue)
        {
            ValidateAnchor(anchor.Value, path, issues);
        }
    }

    private static void ValidateNormalization(
        AssetNormalization? normalization,
        ISet<string> ids,
        ICollection<ManifestValidationIssue> issues)
    {
        if (normalization is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(normalization.MasterAssetId))
        {
            issues.Add(new("normalization.masterAssetId", "Master asset id is required."));
        }
        else if (!ids.Contains(normalization.MasterAssetId))
        {
            issues.Add(new(
                "normalization.masterAssetId",
                $"Unknown pose id '{normalization.MasterAssetId}'."));
        }

        ValidatePositiveOptional(
            normalization.MasterFaceWidthPx,
            "normalization.masterFaceWidthPx",
            issues);
        ValidatePositiveOptional(
            normalization.TargetFaceWidthPx,
            "normalization.targetFaceWidthPx",
            issues);
        ValidatePositiveOptional(
            normalization.TargetStandingHeightPx,
            "normalization.targetStandingHeightPx",
            issues);

        if (normalization.SafetyMarginPx is < 0)
        {
            issues.Add(new("normalization.safetyMarginPx", "Safety margin cannot be negative."));
        }
    }

    private static void ValidatePositiveOptional(
        int? value,
        string path,
        ICollection<ManifestValidationIssue> issues)
    {
        if (value is <= 0)
        {
            issues.Add(new(path, "Value must be positive when present."));
        }
    }

    private static void ValidateAnchor(
        NormalizedPoint anchor,
        string path,
        ICollection<ManifestValidationIssue> issues)
    {
        if (!anchor.IsInUnitSquare)
        {
            issues.Add(new(path, "Anchor coordinates must be finite values in the range 0..1."));
        }
    }
}
