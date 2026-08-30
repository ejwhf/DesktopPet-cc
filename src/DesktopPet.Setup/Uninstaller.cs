using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopPet.Setup;

internal static class Uninstaller
{
    private const int MoveFileDelayUntilReboot = 0x4;

    internal static void RunInteractive()
    {
        var installDirectory = InstallerPaths.ValidateInstallDirectory(InstallerPaths.InstallDirectory);
        var confirmation = MessageBox.Show(
            $"将卸载 {Program.DisplayName} 并移除：\n" +
            $"• {installDirectory}\n" +
            "• 开始菜单快捷方式、开机启动项和卸载注册信息\n\n" +
            $"个人设置将保留在：\n{InstallerPaths.SettingsDirectory}\n\n是否继续？",
            $"卸载 {Program.DisplayName}",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        if (!DesktopPetProcessGuard.EnsureStopped(owner: null))
        {
            return;
        }

        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
        {
            throw new InvalidOperationException("无法定位卸载器自身。");
        }

        Directory.CreateDirectory(InstallerPaths.TempRoot);
        var workerPath = InstallerPaths.ValidateTemporaryUninstaller(
            InstallerPaths.CreateTemporaryUninstallerPath());
        File.Copy(currentExecutable, workerPath, overwrite: false);

        var readyEventName = $"Local\\DesktopPetUninstall-{Guid.NewGuid():N}";
        using var readyEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            readyEventName);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = workerPath,
            Arguments = $"--uninstall-worker {Environment.ProcessId} {readyEventName}",
            UseShellExecute = true,
            WorkingDirectory = InstallerPaths.TempRoot
        });
        if (process is null)
        {
            File.Delete(workerPath);
            throw new InvalidOperationException("无法启动卸载工作进程。");
        }

        using (process)
        {
            if (!readyEvent.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new InvalidOperationException("临时卸载工作进程未能及时启动。");
            }
        }
    }

    internal static void RunWorker(int parentProcessId, string readyEventName)
    {
        if (!readyEventName.StartsWith("Local\\DesktopPetUninstall-", StringComparison.Ordinal) ||
            readyEventName.Length != "Local\\DesktopPetUninstall-".Length + 32)
        {
            throw new InvalidOperationException("卸载工作进程握手参数无效。");
        }

        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable))
        {
            throw new InvalidOperationException("无法定位临时卸载工作进程。");
        }

        var validatedWorker = InstallerPaths.ValidateTemporaryUninstaller(currentExecutable);
        ValidateInteractiveParent(parentProcessId);
        using (var readyEvent = EventWaitHandle.OpenExisting(readyEventName))
        {
            readyEvent.Set();
        }
        WaitForParentExit(parentProcessId);

        try
        {
            ShellIntegration.RemoveRegistration();
            InstallerEngine.DeleteInstalledApplication();

            MessageBox.Show(
                $"{Program.DisplayName} 已卸载。\n\n" +
                $"个人设置已保留在：\n{InstallerPaths.SettingsDirectory}",
                Program.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        finally
        {
            _ = MoveFileEx(validatedWorker, null, MoveFileDelayUntilReboot);
        }
    }

    private static void ValidateInteractiveParent(int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            var executable = parent.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable) ||
                !InstallerPaths.IsWithinInstallDirectory(executable) ||
                !string.Equals(Path.GetFileName(executable), "Uninstall.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("卸载工作进程不是由已安装的交互式卸载器启动。");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("卸载器父进程不存在，已拒绝继续。 ");
        }
    }

    private static void WaitForParentExit(int parentProcessId)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            if (!parent.WaitForExit(milliseconds: 15000))
            {
                throw new InvalidOperationException("卸载器仍在使用中，请稍后重试。");
            }
        }
        catch (ArgumentException)
        {
            // Parent already exited.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(
        string existingFileName,
        string? newFileName,
        int flags);
}
