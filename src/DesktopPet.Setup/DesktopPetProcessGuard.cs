using System.Diagnostics;

namespace DesktopPet.Setup;

internal static class DesktopPetProcessGuard
{
    internal static bool EnsureStopped(IWin32Window? owner)
    {
        while (FindRunningProcesses().Count > 0)
        {
            var result = MessageBox.Show(
                owner,
                "检测到桌宠正在运行。请从托盘菜单退出桌宠，然后点击“重试”。\n\n" +
                "安装器不会静默结束或强制终止桌宠进程。",
                Program.DisplayName,
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Retry)
            {
                return false;
            }
        }

        return true;
    }

    private static List<Process> FindRunningProcesses()
    {
        var matches = new List<Process>();
        foreach (var process in Process.GetProcessesByName("DesktopPet"))
        {
            try
            {
                var executable = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executable) ||
                    InstallerPaths.IsWithinInstallDirectory(executable))
                {
                    matches.Add(process);
                    continue;
                }
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
            {
                // A same-named process that cannot be inspected is treated as potentially relevant.
                matches.Add(process);
                continue;
            }

            process.Dispose();
        }

        foreach (var process in matches)
        {
            process.Dispose();
        }

        return matches;
    }
}
