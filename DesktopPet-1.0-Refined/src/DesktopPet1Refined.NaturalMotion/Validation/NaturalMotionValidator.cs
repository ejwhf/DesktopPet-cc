using DesktopPet1Refined.NaturalMotion.Models;

namespace DesktopPet1Refined.NaturalMotion.Validation;

public static class NaturalMotionValidator
{
    public static IReadOnlyList<string> Validate(PoseProfile profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.PoseId))
        {
            errors.Add("poseId is required.");
        }
        ValidateRelativeAssetPath(profile.File, "file", errors);
        if (profile.CanvasSize.Width <= 0 || profile.CanvasSize.Height <= 0)
        {
            errors.Add("canvasSize must be positive.");
        }
        if (profile.VisualBounds.Width <= 0 || profile.VisualBounds.Height <= 0)
        {
            errors.Add("visualBounds must be positive.");
        }
        if (!string.Equals(profile.HitTest.Source, "alpha", StringComparison.OrdinalIgnoreCase) ||
            profile.HitTest.AlphaThreshold != 16)
        {
            errors.Add("The static baseline requires alpha hit testing with threshold 16.");
        }
        return errors;
    }

    public static IReadOnlyList<string> Validate(MotionDefinition motion)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(motion.Id))
        {
            errors.Add("id is required.");
        }
        if (string.IsNullOrWhiteSpace(motion.BasePose))
        {
            errors.Add("basePose is required.");
        }
        if (!double.IsFinite(motion.DurationMs) || motion.DurationMs <= 0)
        {
            errors.Add("durationMs must be finite and greater than zero.");
        }

        var targets = new Dictionary<string, MotionTargetDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in motion.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Id) || !targets.TryAdd(target.Id, target))
            {
                errors.Add($"Target id '{target.Id}' is empty or duplicated.");
                continue;
            }

            switch (target.Kind)
            {
                case MotionTargetKind.Layer:
                    ValidateRelativeAssetPath(target.Source, $"target '{target.Id}' source", errors);
                    break;
                case MotionTargetKind.PartSprite:
                    if (target.SpriteFrames.Count == 0)
                    {
                        errors.Add($"Part sprite target '{target.Id}' requires at least one frame.");
                    }
                    foreach (var frame in target.SpriteFrames)
                    {
                        ValidateRelativeAssetPath(frame, $"target '{target.Id}' sprite frame", errors);
                    }
                    break;
                case MotionTargetKind.Window:
                    if (target.Source is not null || target.SpriteFrames.Count > 0)
                    {
                        errors.Add($"Window target '{target.Id}' cannot reference image assets.");
                    }
                    break;
            }
        }

        var trackKeys = new HashSet<(string, MotionProperty)>();
        foreach (var track in motion.Tracks)
        {
            if (!targets.TryGetValue(track.Target, out var target))
            {
                errors.Add($"Track target '{track.Target}' does not exist.");
                continue;
            }
            if (!trackKeys.Add((track.Target.ToUpperInvariant(), track.Property)))
            {
                errors.Add($"Track '{track.Target}.{track.Property}' is duplicated.");
            }
            if (!IsPropertyAllowed(target.Kind, track.Property))
            {
                errors.Add($"Property '{track.Property}' is not valid for {target.Kind} target '{target.Id}'.");
            }
            if (track.Keyframes.Count == 0)
            {
                errors.Add($"Track '{track.Target}.{track.Property}' has no keyframes.");
                continue;
            }

            var previous = double.NegativeInfinity;
            foreach (var keyframe in track.Keyframes)
            {
                if (!double.IsFinite(keyframe.TimeMs) || keyframe.TimeMs < 0 || keyframe.TimeMs > motion.DurationMs)
                {
                    errors.Add($"Keyframe time in '{track.Target}.{track.Property}' is outside the motion duration.");
                }
                if (!double.IsFinite(keyframe.Value))
                {
                    errors.Add($"Keyframe value in '{track.Target}.{track.Property}' must be finite.");
                }
                if (keyframe.TimeMs <= previous)
                {
                    errors.Add($"Keyframes in '{track.Target}.{track.Property}' must be strictly increasing.");
                }
                if (track.Property == MotionProperty.Opacity && (keyframe.Value < 0 || keyframe.Value > 1))
                {
                    errors.Add($"Opacity in '{track.Target}' must be between 0 and 1.");
                }
                if (track.Property == MotionProperty.SpriteFrame &&
                    (keyframe.Value < 0 || keyframe.Value >= target.SpriteFrames.Count || keyframe.Value % 1 != 0))
                {
                    errors.Add($"Sprite frame in '{track.Target}' must be a valid integer frame index.");
                }
                previous = keyframe.TimeMs;
            }
        }

        return errors;
    }

    public static void ThrowIfInvalid(PoseProfile profile)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Pose profile '{profile.PoseId}' is invalid: {string.Join(" ", errors)}");
        }
    }

    public static void ThrowIfInvalid(MotionDefinition motion)
    {
        var errors = Validate(motion);
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Motion '{motion.Id}' is invalid: {string.Join(" ", errors)}");
        }
    }

    private static bool IsPropertyAllowed(MotionTargetKind kind, MotionProperty property) => kind switch
    {
        MotionTargetKind.Layer => property is MotionProperty.TranslateX or MotionProperty.TranslateY or MotionProperty.Rotate or MotionProperty.Opacity,
        MotionTargetKind.PartSprite => property is MotionProperty.TranslateX or MotionProperty.TranslateY or MotionProperty.Rotate or MotionProperty.Opacity or MotionProperty.SpriteFrame,
        MotionTargetKind.Window => property is MotionProperty.TranslateX or MotionProperty.TranslateY,
        _ => false
    };

    private static void ValidateRelativeAssetPath(string? path, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{label} is required.");
            return;
        }
        if (Path.IsPathRooted(path) || path.Split('/', '\\').Any(segment => segment == ".."))
        {
            errors.Add($"{label} must be a safe relative asset path.");
        }
    }
}
