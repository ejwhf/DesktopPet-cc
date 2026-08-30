using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace DesktopPet.Integration;

/// <summary>Minimal PNG reader for the pipeline's 8-bit, non-interlaced grayscale hit masks.</summary>
internal sealed class PngMaskReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private PngMaskReader(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    internal int Width { get; }

    internal int Height { get; }

    internal byte[] Pixels { get; }

    internal static PngMaskReader Load(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(Signature))
        {
            throw new InvalidDataException($"'{path}' is not a PNG file.");
        }

        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        using var compressed = new MemoryStream();

        var headerBytes = new byte[8];
        var crcBytes = new byte[4];
        while (true)
        {
            stream.ReadExactly(headerBytes);
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(headerBytes.AsSpan(0, 4)));
            var type = System.Text.Encoding.ASCII.GetString(headerBytes, 4, 4);
            var data = new byte[length];
            stream.ReadExactly(data);
            stream.ReadExactly(crcBytes);

            switch (type)
            {
                case "IHDR":
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4)));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)));
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    return Decode(path, width, height, bitDepth, colorType, interlace, compressed);
            }
        }
    }

    internal (int X, int Y, byte Value) FindInteriorOpaquePoint(byte threshold)
    {
        // Prefer a point near the image centre with a fully opaque 9x9 neighbourhood. The old
        // first-match scan selected the top edge of the hair, which was too sensitive to DPI
        // rounding when source pixels were mapped back to the WPF client area.
        var centreX = (Width - 1) / 2d;
        var centreY = (Height - 1) / 2d;
        var bestIndex = -1;
        var bestDistance = double.PositiveInfinity;
        const int radius = 4;
        for (var y = radius; y < Height - radius; y++)
        {
            for (var x = radius; x < Width - radius; x++)
            {
                var index = (y * Width) + x;
                if (Pixels[index] < threshold)
                {
                    continue;
                }

                var fullyOpaque = true;
                for (var offsetY = -radius; offsetY <= radius && fullyOpaque; offsetY++)
                {
                    for (var offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (Pixels[index + (offsetY * Width) + offsetX] < threshold)
                        {
                            fullyOpaque = false;
                            break;
                        }
                    }
                }

                if (!fullyOpaque)
                {
                    continue;
                }

                var distance = Math.Pow(x - centreX, 2) + Math.Pow(y - centreY, 2);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }
        }

        if (bestIndex < 0)
        {
            throw new InvalidDataException(
                "Hit mask contains no sufficiently interior opaque pixels at the configured threshold.");
        }

        return (bestIndex % Width, bestIndex / Width, Pixels[bestIndex]);
    }

    private static PngMaskReader Decode(
        string path,
        int width,
        int height,
        int bitDepth,
        int colorType,
        int interlace,
        MemoryStream compressed)
    {
        if (width <= 0 || height <= 0 || bitDepth != 8 || colorType != 0 || interlace != 0)
        {
            throw new NotSupportedException(
                $"Hit mask '{path}' must be a non-interlaced 8-bit grayscale PNG; " +
                $"found {width}x{height}, depth {bitDepth}, color type {colorType}, interlace {interlace}.");
        }

        compressed.Position = 0;
        var stride = width;
        var filtered = new byte[checked((stride + 1) * height)];
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.ReadExactly(filtered);
            if (zlib.ReadByte() != -1)
            {
                throw new InvalidDataException($"Hit mask '{path}' contains unexpected decompressed data.");
            }
        }

        var pixels = new byte[checked(stride * height)];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (stride + 1)];
            var sourceOffset = (y * (stride + 1)) + 1;
            var destinationOffset = y * stride;
            for (var x = 0; x < stride; x++)
            {
                var raw = filtered[sourceOffset + x];
                var left = x > 0 ? pixels[destinationOffset + x - 1] : (byte)0;
                var up = y > 0 ? pixels[destinationOffset - stride + x] : (byte)0;
                var upperLeft = x > 0 && y > 0 ? pixels[destinationOffset - stride + x - 1] : (byte)0;
                pixels[destinationOffset + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + up)),
                    3 => unchecked((byte)(raw + ((left + up) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, up, upperLeft))),
                    _ => throw new InvalidDataException($"Unsupported PNG filter {filter} in '{path}'.")
                };
            }
        }

        return new PngMaskReader(width, height, pixels);
    }

    private static byte Paeth(byte left, byte up, byte upperLeft)
    {
        var predictor = left + up - upperLeft;
        var distanceLeft = Math.Abs(predictor - left);
        var distanceUp = Math.Abs(predictor - up);
        var distanceUpperLeft = Math.Abs(predictor - upperLeft);
        return distanceLeft <= distanceUp && distanceLeft <= distanceUpperLeft
            ? left
            : distanceUp <= distanceUpperLeft ? up : upperLeft;
    }
}
