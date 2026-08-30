using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet1Refined.App.Models;

namespace DesktopPet1Refined.App.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopPet1Refined",
        "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(SettingsPath).ConfigureAwait(false);
            return (JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings()).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp");
        var backupPath = SettingsPath + ".bak";

        try
        {
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
