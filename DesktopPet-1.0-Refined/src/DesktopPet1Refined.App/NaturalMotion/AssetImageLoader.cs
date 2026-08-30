using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPet1Refined.App.NaturalMotion;

internal sealed record DecodedAsset(BitmapSource Image, byte[] AlphaPixels);

internal sealed class AssetImageLoader
{
    private readonly string _assetsRoot;
    private readonly Dictionary<string, DecodedAsset> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AssetImageLoader(string assetsRoot)
    {
        _assetsRoot = Path.GetFullPath(assetsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public DecodedAsset LoadPose(string relativePath, int expectedWidth, int expectedHeight)
    {
        var asset = Load(relativePath);
        if (asset.Image.PixelWidth != expectedWidth || asset.Image.PixelHeight != expectedHeight)
        {
            throw new InvalidDataException(
                $"Pose '{relativePath}' must be {expectedWidth}x{expectedHeight}; found {asset.Image.PixelWidth}x{asset.Image.PixelHeight}.");
        }
        var opaqueCount = asset.AlphaPixels.Count(value => value >= 16);
        if (opaqueCount == 0 || opaqueCount == asset.AlphaPixels.Length)
        {
            throw new InvalidDataException($"Pose '{relativePath}' has an invalid transparency plane.");
        }
        return asset;
    }

    public BitmapSource LoadBitmap(string relativePath) => Load(relativePath).Image;

    private DecodedAsset Load(string relativePath)
    {
        var path = Resolve(relativePath);
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"NaturalMotion asset '{relativePath}' is missing.", path);
        }

        BitmapFrame decoded;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1)
            {
                throw new InvalidDataException($"NaturalMotion asset '{relativePath}' must contain one PNG frame.");
            }
            decoded = decoder.Frames[0];
        }

        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = new byte[converted.PixelWidth * converted.PixelHeight];
        for (var index = 0; index < alpha.Length; index++)
        {
            alpha[index] = pixels[(index * 4) + 3];
        }

        var asset = new DecodedAsset(converted, alpha);
        _cache[path] = asset;
        return asset;
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Asset paths must be non-empty and relative.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(_assetsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_assetsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Asset path '{relativePath}' escapes the assets directory.");
        }
        return fullPath;
    }
}
