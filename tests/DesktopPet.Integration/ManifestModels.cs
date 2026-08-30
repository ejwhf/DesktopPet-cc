using System.Text.Json;
using System.IO;

namespace DesktopPet.Integration;

internal sealed record AssetManifest
{
    public int[]? SourceSize { get; init; }

    public int HitMaskAlphaThreshold { get; init; } = 16;

    public IReadOnlyList<AssetEntry> Assets { get; init; } = Array.Empty<AssetEntry>();

    public static AssetManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<AssetManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidDataException("Manifest JSON is empty.");

        if (manifest.Assets.Count == 0)
        {
            throw new InvalidDataException("Manifest contains no assets.");
        }

        return manifest;
    }
}

internal sealed record AssetEntry
{
    public required string Id { get; init; }

    public required string File { get; init; }

    public required string HitMask { get; init; }

    public int[]? SourceSize { get; init; }
}
