using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Core.Assets;

[JsonConverter(typeof(PixelSizeJsonConverter))]
public readonly record struct PixelSize(int Width, int Height)
{
    public bool IsPositive => Width > 0 && Height > 0;
}

[JsonConverter(typeof(PixelRectJsonConverter))]
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool IsPositive => Width > 0 && Height > 0;
}

[JsonConverter(typeof(NormalizedPointJsonConverter))]
public readonly record struct NormalizedPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public bool IsInUnitSquare => IsFinite && X is >= 0 and <= 1 && Y is >= 0 and <= 1;
}

internal abstract class FixedArrayConverter<T> : JsonConverter<T>
{
    protected static void ExpectStartArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected a JSON array.");
        }
    }

    protected static double ReadDouble(ref Utf8JsonReader reader, string label)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value))
        {
            throw new JsonException($"Expected numeric {label}.");
        }

        return value;
    }

    protected static int ReadInt32(ref Utf8JsonReader reader, string label)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
        {
            throw new JsonException($"Expected integer {label}.");
        }

        return value;
    }

    protected static void ExpectEndArray(ref Utf8JsonReader reader)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Array contains an unexpected number of values.");
        }
    }
}

internal sealed class PixelSizeJsonConverter : FixedArrayConverter<PixelSize>
{
    public override PixelSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ExpectStartArray(ref reader);
        var width = ReadInt32(ref reader, "width");
        var height = ReadInt32(ref reader, "height");
        ExpectEndArray(ref reader);
        return new PixelSize(width, height);
    }

    public override void Write(Utf8JsonWriter writer, PixelSize value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}

internal sealed class PixelRectJsonConverter : FixedArrayConverter<PixelRect>
{
    public override PixelRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ExpectStartArray(ref reader);
        var x = ReadInt32(ref reader, "x");
        var y = ReadInt32(ref reader, "y");
        var width = ReadInt32(ref reader, "width");
        var height = ReadInt32(ref reader, "height");
        ExpectEndArray(ref reader);
        return new PixelRect(x, y, width, height);
    }

    public override void Write(Utf8JsonWriter writer, PixelRect value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Width);
        writer.WriteNumberValue(value.Height);
        writer.WriteEndArray();
    }
}

internal sealed class NormalizedPointJsonConverter : FixedArrayConverter<NormalizedPoint>
{
    public override NormalizedPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ExpectStartArray(ref reader);
        var x = ReadDouble(ref reader, "x");
        var y = ReadDouble(ref reader, "y");
        ExpectEndArray(ref reader);
        return new NormalizedPoint(x, y);
    }

    public override void Write(Utf8JsonWriter writer, NormalizedPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}
