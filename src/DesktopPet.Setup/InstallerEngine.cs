namespace DesktopPet.Setup;

internal static class InstallerEngine
{
    internal static bool IsExistingInstallation
    {
        get
        {
            var installDirectory = InstallerPaths.ValidateInstallDirectory(InstallerPaths.InstallDirectory);
            return Directory.Exists(installDirectory) &&
                   Directory.EnumerateFileSystemEntries(installDirectory).Any() ||
                   ShellIntegration.HasUninstallRegistration();
        }
    }

    internal static void InstallOrUpdate()
    {
        var installDirectory = InstallerPaths.ValidateInstallDirectory(InstallerPaths.InstallDirectory);
        var stagingDirectory = InstallerPaths.ValidateTransientDirectory(
            InstallerPaths.CreateStagingDirectory());
        var backupDirectory = InstallerPaths.ValidateTransientDirectory(
            InstallerPaths.CreateBackupDirectory());
        var previousMoved = false;
        var newInstalled = false;

        Directory.CreateDirectory(InstallerPaths.ProgramsRoot);

        try
        {
            SafeZipExtractor.ExtractEmbeddedPayload(stagingDirectory);
            CopyUninstallerInto(stagingDirectory);

            if (Directory.Exists(installDirectory))
            {
                Directory.Move(installDirectory, backupDirectory);
                previousMoved = true;
            }

            Directory.Move(stagingDirectory, installDirectory);
            newInstalled = true;

            ShellIntegration.RegisterInstallation(installDirectory, Program.ProductVersion);

            if (previousMoved && Directory.Exists(backupDirectory))
            {
                DeleteValidatedDirectory(backupDirectory, transient: true);
                previousMoved = false;
            }
        }
        catch
        {
            try
            {
                ShellIntegration.RemoveShortcutIfPresent();

                if (newInstalled && Directory.Exists(installDirectory))
                {
                    DeleteValidatedDirectory(installDirectory, transient: false);
                    newInstalled = false;
                }

                if (previousMoved && Directory.Exists(backupDirectory) && !Directory.Exists(installDirectory))
                {
                    Directory.Move(backupDirectory, installDirectory);
                    previousMoved = false;
                }
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException("安装失败，且恢复上一版本时也发生错误。", rollbackException);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                DeleteValidatedDirectory(stagingDirectory, transient: true);
            }
        }
    }

    internal static void DeleteInstalledApplication()
    {
        var installDirectory = InstallerPaths.ValidateInstallDirectory(InstallerPaths.InstallDirectory);
        if (Directory.Exists(installDirectory))
        {
            DeleteValidatedDirectory(installDirectory, transient: false);
        }
    }

    internal static void DeleteValidatedDirectory(string directory, bool transient)
    {
        var validated = transient
            ? InstallerPaths.ValidateTransientDirectory(directory)
            : InstallerPaths.ValidateInstallDirectory(directory);
        Directory.Delete(validated, recursive: true);
    }

    private static void CopyUninstallerInto(string stagingDirectory)
    {
        var staging = InstallerPaths.ValidateTransientDirectory(stagingDirectory);
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            throw new InvalidOperationException("无法定位当前安装器，不能创建卸载器。");
        }

        File.Copy(source, Path.Combine(staging, "Uninstall.exe"), overwrite: false);
    }
}
