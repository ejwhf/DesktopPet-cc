using System.Text.Json;
using DesktopPet1Refined.NaturalMotion.Models;
using DesktopPet1Refined.NaturalMotion.Serialization;
using DesktopPet1Refined.NaturalMotion.Validation;

namespace DesktopPet1Refined.NaturalMotion.Catalog;

public sealed class NaturalMotionCatalog
{
    private NaturalMotionCatalog(
        IReadOnlyDictionary<string, PoseProfile> poses,
        IReadOnlyDictionary<string, MotionDefinition> motions)
    {
        Poses = poses;
        Motions = motions;
    }

    public IReadOnlyDictionary<string, PoseProfile> Poses { get; }

    public IReadOnlyDictionary<string, MotionDefinition> Motions { get; }

    public static NaturalMotionCatalog Load(string naturalMotionDirectory)
    {
        var root = Path.GetFullPath(naturalMotionDirectory);
        var profilePath = Path.Combine(root, "pose-profiles.json");
        var profileDocument = JsonSerializer.Deserialize<PoseProfileDocument>(
                                  File.ReadAllText(profilePath),
                                  NaturalMotionJson.Options)
                              ?? throw new InvalidDataException("pose-profiles.json is empty.");
        if (profileDocument.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported pose profile schema {profileDocument.SchemaVersion}.");
        }

        var poses = new Dictionary<string, PoseProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pose in profileDocument.Poses)
        {
            NaturalMotionValidator.ThrowIfInvalid(pose);
            if (!poses.TryAdd(pose.PoseId, pose))
            {
                throw new InvalidDataException($"Pose '{pose.PoseId}' is duplicated.");
            }
        }

        var motions = new Dictionary<string, MotionDefinition>(StringComparer.OrdinalIgnoreCase);
        var motionsDirectory = Path.Combine(root, "motions");
        if (Directory.Exists(motionsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(motionsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var motion = JsonSerializer.Deserialize<MotionDefinition>(
                                 File.ReadAllText(path),
                                 NaturalMotionJson.Options)
                             ?? throw new InvalidDataException($"Motion file '{path}' is empty.");
                NaturalMotionValidator.ThrowIfInvalid(motion);
                if (!poses.ContainsKey(motion.BasePose))
                {
                    throw new InvalidDataException($"Motion '{motion.Id}' references unknown pose '{motion.BasePose}'.");
                }
                if (!motions.TryAdd(motion.Id, motion))
                {
                    throw new InvalidDataException($"Motion '{motion.Id}' is duplicated.");
                }
            }
        }

        return new NaturalMotionCatalog(poses, motions);
    }
}
