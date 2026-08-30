using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Core.Serialization;

/// <summary>Creates the JSON conventions shared by manifests and settings.</summary>
public static class CoreJson
{
    public static JsonSerializerOptions CreateOptions(bool writeIndented = true)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
