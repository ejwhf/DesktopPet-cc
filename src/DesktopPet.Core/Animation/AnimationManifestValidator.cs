using System.Text.Json;
using DesktopPet.Core.Assets;
using DesktopPet.Core.Serialization;

namespace DesktopPet.Core.Animation;

public sealed class AnimationManifestValidationException(IReadOnlyList<string> issues)
    : Exception(string.Join(Environment.NewLine, issues))
{
    public IReadOnlyList<string> Issues { get; } = issues;
}

public static class AnimationManifestValidator
{
    public static IReadOnlyList<string> Validate(AnimationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<string>();
        if (manifest.SchemaVersion != AnimationManifest.CurrentSchemaVersion)
        {
            issues.Add($"schemaVersion: expected {AnimationManifest.CurrentSchemaVersion}.");
        }
        if (!manifest.CanvasSize.IsPositive)
        {
            issues.Add("canvasSize: dimensions must be positive.");
        }
        if (manifest.DefaultFrameDurationMs <= 0)
        {
            issues.Add("defaultFrameDurationMs: must be positive.");
        }
        if (manifest.Clips is null || manifest.Clips.Count == 0)
        {
            issues.Add("clips: at least one clip is required.");
            return issues.AsReadOnly();
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var frameCount = 0;
        foreach (var clip in manifest.Clips)
        {
            if (clip is null)
            {
                issues.Add("clips: entries cannot be null.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(clip.Id) || !ids.Add(clip.Id))
            {
                issues.Add($"clips: invalid or duplicate id '{clip.Id}'.");
            }
            if (string.IsNullOrWhiteSpace(clip.FallbackPoseId))
            {
                issues.Add($"{clip.Id}.fallbackPoseId: is required.");
            }
            else if (!PoseIds.All.Contains(clip.FallbackPoseId, StringComparer.Ordinal))
            {
                issues.Add($"{clip.Id}.fallbackPoseId: unknown pose '{clip.FallbackPoseId}'.");
            }
            if (clip.Frames is null || clip.Frames.Count == 0)
            {
                issues.Add($"{clip.Id}.frames: at least one frame is required.");
                continue;
            }
            for (var index = 0; index < clip.Frames.Count; index++)
            {
                frameCount++;
                var frame = clip.Frames[index];
                var path = $"{clip.Id}.frames[{index}]";
                if (frame is null || string.IsNullOrWhiteSpace(frame.File))
                {
                    issues.Add($"{path}.file: is required.");
                    continue;
                }
                if (frame.DurationMs <= 0)
                {
                    issues.Add($"{path}.durationMs: must be positive.");
                }
                if (!frame.VisualBounds.IsPositive || frame.VisualBounds.X < 0 || frame.VisualBounds.Y < 0 ||
                    frame.VisualBounds.Right > manifest.CanvasSize.Width ||
                    frame.VisualBounds.Bottom > manifest.CanvasSize.Height)
                {
                    issues.Add($"{path}.visualBounds: must fit the canvas.");
                }
                if (!frame.Anchor.IsInUnitSquare)
                {
                    issues.Add($"{path}.anchor: must be within 0..1.");
                }
                if (string.IsNullOrWhiteSpace(frame.SourcePoseId))
                {
                    issues.Add($"{path}.sourcePoseId: is required.");
                }
                else if (!PoseIds.All.Contains(frame.SourcePoseId, StringComparer.Ordinal))
                {
                    issues.Add($"{path}.sourcePoseId: unknown pose '{frame.SourcePoseId}'.");
                }
            }
            try
            {
                _ = clip.TotalDurationMs;
            }
            catch (OverflowException)
            {
                issues.Add($"{clip.Id}: total duration overflow.");
            }
        }
        if (manifest.FrameCount != frameCount)
        {
            issues.Add($"frameCount: declared {manifest.FrameCount}, measured {frameCount}.");
        }
        foreach (var required in PlannedAnimationCatalog.RequiredClipIds.Where(id => !ids.Contains(id)))
        {
            issues.Add($"clips: required clip '{required}' is missing.");
        }
        return issues.AsReadOnly();
    }

    public static async Task<AnimationManifest> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<AnimationManifest>(
            stream, CoreJson.CreateOptions(), cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("Animation manifest is empty.");
        var issues = Validate(manifest);
        if (issues.Count > 0)
        {
            throw new AnimationManifestValidationException(issues);
        }
        return manifest;
    }
}
