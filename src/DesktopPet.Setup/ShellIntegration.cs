using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopPet.Setup;

internal static class ShellIntegration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopPet";
    private const string RegistryValueName = "DesktopPet";

    internal static bool HasUninstallRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: false);
        return key is not null;
    }

    internal static void RegisterInstallation(string installDirectory, string version)
    {
        installDirectory = InstallerPaths.ValidateInstallDirectory(installDirectory);
        var appPath = Path.Combine(installDirectory, "DesktopPet.exe");
        var uninstallerPath = Path.Combine(installDirectory, "Uninstall.exe");

        CreateStartMenuShortcut(appPath, installDirectory);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法创建当前用户卸载注册信息。");
        key.SetValue("DisplayName", Program.DisplayName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        key.SetValue("Publisher", "DesktopPet", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        key.SetValue("DisplayIcon", $"\"{appPath}\"", RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
        key.SetValue("EstimatedSize", EstimateInstalledKilobytes(installDirectory), RegistryValueKind.DWord);
    }

    internal static void RemoveRegistration()
    {
        using (var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
        {
            runKey?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        var shortcut = InstallerPaths.ValidateStartMenuShortcut(InstallerPaths.StartMenuShortcut);
        if (File.Exists(shortcut))
        {
            File.Delete(shortcut);
        }
    }

    internal static void RemoveShortcutIfPresent()
    {
        var shortcut = InstallerPaths.ValidateStartMenuShortcut(InstallerPaths.StartMenuShortcut);
        if (File.Exists(shortcut))
        {
            File.Delete(shortcut);
        }
    }

    private static void CreateStartMenuShortcut(string appPath, string workingDirectory)
    {
        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException("找不到已安装的桌宠程序。", appPath);
        }

        var shortcutPath = InstallerPaths.ValidateStartMenuShortcut(InstallerPaths.StartMenuShortcut);
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host 不可用，无法创建开始菜单快捷方式。");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建 Windows Shell 快捷方式服务。");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath })
                ?? throw new InvalidOperationException("无法创建快捷方式对象。");

            SetComProperty(shortcut, "TargetPath", appPath);
            SetComProperty(shortcut, "WorkingDirectory", workingDirectory);
            SetComProperty(shortcut, "Description", Program.DisplayName);
            SetComProperty(shortcut, "IconLocation", $"{appPath},0");
            shortcut.GetType().InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void SetComProperty(object instance, string propertyName, string value)
    {
        instance.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target: instance,
            args: new object[] { value });
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private static int EstimateInstalledKilobytes(string installDirectory)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories))
        {
            bytes = checked(bytes + new FileInfo(file).Length);
        }

        return (int)Math.Clamp((bytes + 1023) / 1024, 1, int.MaxValue);
    }
}
