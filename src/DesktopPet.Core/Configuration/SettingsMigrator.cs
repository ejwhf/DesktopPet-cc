using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPet.Core.Serialization;

namespace DesktopPet.Core.Configuration;

public sealed record SettingsMigrationResult(PetSettings Settings, int SourceSchemaVersion)
{
    public bool WasMigrated => SourceSchemaVersion != PetSettings.CurrentSchemaVersion;
}

/// <summary>Applies ordered, explicit migrations to legacy settings JSON.</summary>
public static class SettingsMigrator
{
    public static SettingsMigrationResult Migrate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var node = JsonNode.Parse(
            json,
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject ?? throw new JsonException("Settings root must be a JSON object.");

        var sourceVersion = ReadSchemaVersion(node);
        if (sourceVersion < 0)
        {
            throw new JsonException("schemaVersion cannot be negative.");
        }

        if (sourceVersion > PetSettings.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Settings schema {sourceVersion} is newer than supported schema {PetSettings.CurrentSchemaVersion}.");
        }

        var version = sourceVersion;
        if (version == 0)
        {
            MigrateLegacyToV1(node);
            version = 1;
        }

        if (version == 1)
        {
            MigrateV1ToV2(node);
            version = 2;
        }

        node["schemaVersion"] = version;
        var settings = node.Deserialize<PetSettings>(CoreJson.CreateOptions())
            ?? throw new JsonException("Settings JSON produced no value.");
        PetSettingsValidator.Validate(settings).ThrowIfInvalid();
        return new SettingsMigrationResult(settings, sourceVersion);
    }

    private static int ReadSchemaVersion(JsonObject node)
    {
        if (!node.TryGetPropertyValue("schemaVersion", out var value) || value is null)
        {
            return 0;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var version))
        {
            throw new JsonException("schemaVersion must be an integer.");
        }

        return version;
    }

    private static void MigrateLegacyToV1(JsonObject node)
    {
        // Early prototypes stored an integer percentage rather than a scale factor.
        if (!node.ContainsKey("scale") &&
            TryTake(node, "sizePercent", out var sizeNode) &&
            sizeNode is JsonValue sizeValue &&
            sizeValue.TryGetValue<double>(out var sizePercent))
        {
            node["scale"] = sizePercent / 100d;
        }
        else
        {
            node.Remove("sizePercent");
        }

        MoveIfMissing(node, "topmost", "alwaysOnTop");
        MoveIfMissing(node, "sound", "soundEnabled");
        MoveIfMissing(node, "autostart", "startWithWindows");
        node["schemaVersion"] = 1;
    }

    private static void MigrateV1ToV2(JsonObject node)
    {
        MoveIfMissing(node, "actionFrequency", "actionFrequencyMultiplier");
        if (!node.ContainsKey("hitTestMode") &&
            TryTake(node, "clickThrough", out var clickThroughNode) &&
            clickThroughNode is JsonValue clickThroughValue &&
            clickThroughValue.TryGetValue<bool>(out var clickThrough))
        {
            node["hitTestMode"] = clickThrough ? "clickThrough" : "characterPixels";
        }
        else
        {
            node.Remove("clickThrough");
        }

        node["schemaVersion"] = 2;
    }

    private static void MoveIfMissing(JsonObject node, string oldName, string newName)
    {
        if (node.ContainsKey(newName))
        {
            node.Remove(oldName);
            return;
        }

        if (TryTake(node, oldName, out var value))
        {
            node[newName] = value;
        }
    }

    private static bool TryTake(JsonObject node, string name, out JsonNode? value)
    {
        if (!node.TryGetPropertyValue(name, out value))
        {
            return false;
        }

        node.Remove(name);
        return true;
    }
}
