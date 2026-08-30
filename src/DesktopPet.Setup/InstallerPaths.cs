namespace DesktopPet.Setup;

internal static class InstallerPaths
{
    private const string ProgramsDirectoryName = "Programs";
    private const string InstallDirectoryName = "DesktopPet";

    internal static string LocalAppDataRoot => RequireFolder(Environment.SpecialFolder.LocalApplicationData);

    internal static string ProgramsRoot => Path.GetFullPath(
        Path.Combine(LocalAppDataRoot, ProgramsDirectoryName));

    internal static string InstallDirectory => Path.GetFullPath(
        Path.Combine(ProgramsRoot, InstallDirectoryName));

    internal static string SettingsDirectory => Path.GetFullPath(
        Path.Combine(LocalAppDataRoot, InstallDirectoryName));

    internal static string TempRoot => Path.GetFullPath(
        Path.Combine(LocalAppDataRoot, "Temp"));

    internal static string StartMenuShortcut
    {
        get
        {
            var programs = RequireFolder(Environment.SpecialFolder.Programs);
            return Path.GetFullPath(Path.Combine(programs, "DesktopPet.lnk"));
        }
    }

    internal static string CreateStagingDirectory() => Path.Combine(
        ProgramsRoot,
        $"DesktopPet.install-{Guid.NewGuid():N}");

    internal static string CreateBackupDirectory() => Path.Combine(
        ProgramsRoot,
        $"DesktopPet.backup-{Guid.NewGuid():N}");

    internal static string CreateTemporaryUninstallerPath() => Path.Combine(
        TempRoot,
        $"DesktopPet-Uninstall-{Guid.NewGuid():N}.exe");

    internal static string ValidateInstallDirectory(string candidate)
    {
        var resolved = Path.GetFullPath(candidate);
        if (!string.Equals(resolved, InstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝操作非预期安装目录：{resolved}");
        }

        EnsureDirectChildOfProgramsRoot(resolved, InstallDirectoryName);
        return resolved;
    }

    internal static string ValidateTransientDirectory(string candidate)
    {
        var resolved = Path.GetFullPath(candidate);
        var name = Path.GetFileName(resolved);
        var allowedName = name.StartsWith("DesktopPet.install-", StringComparison.Ordinal) ||
                          name.StartsWith("DesktopPet.backup-", StringComparison.Ordinal);
        if (!allowedName)
        {
            throw new InvalidOperationException($"拒绝操作非预期临时目录：{resolved}");
        }

        EnsureDirectChildOfProgramsRoot(resolved, name);
        return resolved;
    }

    internal static string ValidateTemporaryUninstaller(string candidate)
    {
        var resolved = Path.GetFullPath(candidate);
        var root = TempRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = root + Path.DirectorySeparatorChar;
        var fileName = Path.GetFileName(resolved);
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.StartsWith("DesktopPet-Uninstall-", StringComparison.Ordinal) ||
            !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝操作非预期临时卸载器：{resolved}");
        }

        return resolved;
    }

    internal static string ValidateStartMenuShortcut(string candidate)
    {
        var resolved = Path.GetFullPath(candidate);
        if (!string.Equals(resolved, StartMenuShortcut, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝操作非预期开始菜单快捷方式：{resolved}");
        }

        return resolved;
    }

    internal static bool IsWithinInstallDirectory(string candidate)
    {
        var resolved = Path.GetFullPath(candidate);
        var root = InstallDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(resolved, root, StringComparison.OrdinalIgnoreCase) ||
               resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDirectChildOfProgramsRoot(string candidate, string expectedName)
    {
        var parent = Directory.GetParent(candidate)?.FullName;
        if (!string.Equals(parent, ProgramsRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(candidate), expectedName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"路径不在预期 LocalAppData Programs 目录中：{candidate}");
        }
    }

    private static string RequireFolder(Environment.SpecialFolder specialFolder)
    {
        var path = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"无法解析 Windows 用户目录：{specialFolder}");
        }

        return Path.GetFullPath(path);
    }
}
