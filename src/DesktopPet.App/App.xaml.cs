using System.IO;
using System.Windows;
using DesktopPet.App.Services;
using DesktopPet.App.Views;
using DesktopPet.Core.Configuration;

namespace DesktopPet.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _trayIcon;
    private MainWindow? _petWindow;
    private PetRuntimeController? _petRuntime;
    private PetSettingsCoordinator? _settingsCoordinator;
    private PetSettings _settings = new();
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimaryInstance)
        {
            _singleInstance.SignalPrimaryInstance();
            Shutdown();
            return;
        }

        _petWindow = new MainWindow();
        MainWindow = _petWindow;

        _singleInstance.ActivationRequested += (_, _) =>
            Dispatcher.BeginInvoke(new Action(_petWindow.RecoverControl));

        _petWindow.Show();
        InitializeApplicationAsync(_petWindow);
    }

    private async void InitializeApplicationAsync(MainWindow window)
    {
        _settingsCoordinator = new PetSettingsCoordinator(Dispatcher);
        try
        {
            _settings = await _settingsCoordinator.LoadAsync();
            SynchronizeStartupSetting();
        }
        catch (Exception exception)
        {
            _settings = new PetSettings();
            System.Diagnostics.Debug.WriteLine($"Desktop-pet settings could not be loaded: {exception}");
            WriteStartupDiagnostic("settings", exception);
        }

        window.RestorePlacement(
            _settings.Placement.Left,
            _settings.Placement.Top,
            _settings.Placement.MonitorId);
        window.PlacementChanged += OnPlacementChanged;
        ApplySettings(_settings);

        try
        {
            _trayIcon = new TrayIconService(
                window,
                _settingsCoordinator,
                requestExit: RequestExit,
                toggleActionPicker: window.ToggleActionPicker,
                applySettings: ApplySettings);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Desktop-pet tray icon could not be initialized: {exception}");
            WriteStartupDiagnostic("tray", exception);
        }

        try
        {
            var runtime = await PetRuntimeController.CreateAsync(window);
            if (_isShuttingDown)
            {
                runtime.Dispose();
                return;
            }

            _petRuntime = runtime;
            runtime.ApplyBehaviorSettings(
                _settings.ActionFrequencyMultiplier,
                _settings.ReduceMotion);
            runtime.Start();
            var diagnosticPose = Environment.GetEnvironmentVariable("DESKTOPPET_DIAGNOSTIC_POSE");
            if (!string.IsNullOrWhiteSpace(diagnosticPose))
            {
                runtime.SelectDebugPose(diagnosticPose);
            }
#if DEBUG
            if (window.ActionComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string selectedPose })
            {
                runtime.SelectDebugPose(selectedPose);
            }
#endif
        }
        catch (Exception exception)
        {
            // Keep the tray alive and the window transparent when deployment assets are invalid.
            System.Diagnostics.Debug.WriteLine($"Desktop-pet assets could not be initialized: {exception}");
            WriteStartupDiagnostic("assets", exception);
        }
    }

    private static void WriteStartupDiagnostic(string stage, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-errors.log"),
                $"[{DateTimeOffset.Now:O}] {stage}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception diagnosticException) when (diagnosticException is
            IOException or
            UnauthorizedAccessException)
        {
            // Diagnostics must never block the pet from starting.
        }
    }

    private void SynchronizeStartupSetting()
    {
        if (_settingsCoordinator is null)
        {
            return;
        }

        _ = WindowsStartupService.RemoveStaleEntry();
        var actual = WindowsStartupService.IsEnabled();
        if (actual != _settings.StartWithWindows)
        {
            _settingsCoordinator.Update(settings => settings with { StartWithWindows = actual });
            _settings = _settingsCoordinator.Current;
        }
    }

    private void ApplySettings(PetSettings settings)
    {
        _settings = settings;
        _petWindow?.ApplyDisplaySettings(
            settings.Scale,
            settings.Opacity,
            settings.AlwaysOnTop,
            settings.LockPosition);
        _petWindow?.SetHitTestMode(TrayIconService.FromCoreMode(settings.HitTestMode));
        _petRuntime?.ApplyBehaviorSettings(
            settings.ActionFrequencyMultiplier,
            settings.ReduceMotion);
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (_settingsCoordinator is null || _petWindow is null)
        {
            return;
        }

        _settingsCoordinator.Update(settings => settings with
        {
            Placement = settings.Placement with
            {
                Left = _petWindow.Left,
                Top = _petWindow.Top,
                MonitorId = GetCurrentMonitorId(_petWindow)
            }
        });
        _settings = _settingsCoordinator.Current;
    }

    private static string? GetCurrentMonitorId(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        return handle == nint.Zero
            ? null
            : System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _isShuttingDown = true;
        _petWindow?.AllowClose();
        if (_petWindow is not null)
        {
            _petWindow.PlacementChanged -= OnPlacementChanged;
        }
        _settingsCoordinator?.FlushAsync().GetAwaiter().GetResult();
        _petRuntime?.Dispose();
        _petRuntime = null;
        base.OnSessionEnding(e);
    }

    private void RequestExit()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _petWindow?.AllowClose();
        if (_petWindow is not null)
        {
            _petWindow.PlacementChanged -= OnPlacementChanged;
        }
        _settingsCoordinator?.FlushAsync().GetAwaiter().GetResult();
        _trayIcon?.Dispose();
        _petRuntime?.Dispose();
        _settingsCoordinator?.Dispose();
        _trayIcon = null;
        _petRuntime = null;
        _settingsCoordinator = null;
        _petWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _petRuntime?.Dispose();
        _settingsCoordinator?.Dispose();
        _singleInstance?.Dispose();
        _trayIcon = null;
        _petRuntime = null;
        _settingsCoordinator = null;
        _singleInstance = null;
        base.OnExit(e);
    }
}
