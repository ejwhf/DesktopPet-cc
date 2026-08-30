using System.Reflection;

namespace DesktopPet.Setup;

internal static class Program
{
    internal const string ProductName = "DesktopPet";
    internal const string DisplayName = "桌宠 DesktopPet";

    internal static string ProductVersion
    {
        get
        {
            var informational = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? "0.3.0"
                : informational.Split('+', 2)[0];
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            if (args.Length > 0 &&
                string.Equals(args[0], "--uninstall-worker", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 3 || !int.TryParse(args[1], out var parentProcessId))
                {
                    throw new ArgumentException("卸载工作进程参数无效。");
                }

                Uninstaller.RunWorker(parentProcessId, args[2]);
                return;
            }

            if (args.Any(static value =>
                    string.Equals(value, "--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstaller.RunInteractive();
                return;
            }

            Application.Run(new InstallerForm());
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"操作失败：\n\n{exception.Message}",
                DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
