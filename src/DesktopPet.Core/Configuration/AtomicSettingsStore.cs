using System.Text;
using System.Text.Json;
using DesktopPet.Core.Serialization;

namespace DesktopPet.Core.Configuration;

public sealed record SettingsLoadResult(
    PetSettings Settings,
    bool WasMigrated,
    bool RecoveredFromBackup,
    bool FileExisted);

/// <summary>
/// Persists settings through a same-directory temporary file. Existing settings are retained as
/// a <c>.bak</c> file during replacement.
/// </summary>
public sealed class AtomicSettingsStore
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public async Task<SettingsLoadResult> LoadAsync(
        string path,
        bool recoverFromBackup = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizePath(path);
        if (!File.Exists(fullPath))
        {
            return new SettingsLoadResult(new PetSettings(), false, false, false);
        }

        try
        {
            var primary = await LoadFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return new SettingsLoadResult(primary.Settings, primary.WasMigrated, false, true);
        }
        catch (Exception primaryException) when (recoverFromBackup && IsRecoverable(primaryException))
        {
            var backupPath = fullPath + ".bak";
            if (!File.Exists(backupPath))
            {
                throw;
            }

            try
            {
                var backup = await LoadFileAsync(backupPath, cancellationToken).ConfigureAwait(false);
                return new SettingsLoadResult(backup.Settings, backup.WasMigrated, true, true);
            }
            catch (Exception backupException) when (IsRecoverable(backupException))
            {
                throw new AggregateException(
                    "Neither the primary settings file nor its backup could be loaded.",
                    primaryException,
                    backupException);
            }
        }
    }

    public async Task SaveAsync(
        string path,
        PetSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        PetSettingsValidator.Validate(settings).ThrowIfInvalid();
        var fullPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Settings path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = fullPath + ".bak";

        try
        {
            var json = JsonSerializer.Serialize(settings, CoreJson.CreateOptions());
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
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

    private static async Task<SettingsMigrationResult> LoadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return SettingsMigrator.Migrate(json);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static bool IsRecoverable(Exception exception) => exception is
        JsonException or
        SettingsValidationException or
        NotSupportedException or
        IOException or
        UnauthorizedAccessException;
}
