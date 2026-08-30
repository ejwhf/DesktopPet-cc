using System.IO;
using System.Windows;
using DesktopPet1Refined.App.Models;
using DesktopPet1Refined.App.NaturalMotion;
using DesktopPet1Refined.App.Services;

namespace DesktopPet1Refined.App;

public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _trayIcon;
    private MainWindow? _petWindow;
    private NaturalMotionCoordinator? _naturalMotion;
    private AppSettings _settings = new();
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

        _singleInstance.ActivationRequested += OnActivationRequested;
        InitializeApplication();
    }

    private async void InitializeApplication()
    {
        try
        {
            await InitializeApplicationAsync();
        }
        catch (Exception exception)
        {
            WriteStartupError(exception);
            _isShuttingDown = true;
            _naturalMotion?.Dispose();
            _naturalMotion = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            if (_petWindow is not null)
            {
                _petWindow.PlacementChanged -= OnPlacementChanged;
                _petWindow.HitTestModeChanged -= OnHitTestModeChanged;
                _petWindow.AllowClose();
                _petWindow.Close();
            }

            System.Windows.MessageBox.Show(
                $"桌宠启动失败。错误已记录到：{Environment.NewLine}" +
                $"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet1Refined", "startup-errors.log")}{Environment.NewLine}{Environment.NewLine}" +
                exception.Message,
                "DesktopPet 1.0 Refined",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private async Task InitializeApplicationAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        if (_isShuttingDown)
        {
            return;
        }

        _petWindow = new MainWindow();
        MainWindow = _petWindow;
        _petWindow.PlacementChanged += OnPlacementChanged;
        _petWindow.HitTestModeChanged += OnHitTestModeChanged;
        ApplySettings(_settings);
        _petWindow.RestorePlacement(_settings.Left, _settings.Top, _settings.MonitorId);

        // The image remains collapsed while the transparent shell is first shown. The only startup
        // pose is then resolved through pose-profiles.json and validated before it becomes visible.
        _petWindow.Show();
        await System.Windows.Threading.Dispatcher.Yield(
            System.Windows.Threading.DispatcherPriority.Background);

        _naturalMotion = new NaturalMotionCoordinator(_petWindow);
        _naturalMotion.ShowStaticBasePose();
        _naturalMotion.SetReduceMotion(_settings.ReduceMotion);

        _trayIcon = new TrayIconService(
            _petWindow,
            () => _settings,
            UpdateSettings,
            _naturalMotion.OpenPreview,
            _naturalMotion.OpenBlinkDebug,
            _naturalMotion.TriggerBlink,
            _naturalMotion.MotionIds,
            _naturalMotion.PlayDebugMotion,
            RequestExit);
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings.Normalize();
        ApplySettings(_settings);
        _ = SaveSettingsAsync();
    }

    private void ApplySettings(AppSettings settings)
    {
        _petWindow?.ApplyDisplaySettings(
            settings.Scale,
            settings.Opacity,
            settings.AlwaysOnTop,
            settings.LockPosition);
        _petWindow?.SetHitTestMode(settings.HitTestMode);
        _naturalMotion?.SetReduceMotion(settings.ReduceMotion);
        _trayIcon?.RefreshChecks(settings);
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (_petWindow is null || _isShuttingDown)
        {
            return;
        }

        _settings = _settings with
        {
            Left = _petWindow.Left,
            Top = _petWindow.Top,
            MonitorId = _petWindow.GetCurrentMonitorId()
        };
        _ = SaveSettingsAsync();
    }

    private void OnHitTestModeChanged(object? sender, HitTestMode mode)
    {
        if (_isShuttingDown || _settings.HitTestMode == mode)
        {
            return;
        }

        _settings = _settings with { HitTestMode = mode };
        _trayIcon?.RefreshChecks(_settings);
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        var snapshot = _settings;
        await _settingsSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _settingsStore.SaveAsync(snapshot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteStartupError(exception);
        }
        finally
        {
            _settingsSaveGate.Release();
        }
    }

    private void OnActivationRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => _petWindow?.RecoverControl()));

    private async void RequestExit()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        if (_petWindow is not null)
        {
            _petWindow.PlacementChanged -= OnPlacementChanged;
            _petWindow.HitTestModeChanged -= OnHitTestModeChanged;
            _petWindow.AllowClose();
        }

        await SaveSettingsAsync();
        _naturalMotion?.Dispose();
        _naturalMotion = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _petWindow?.Close();
        Shutdown();
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet1Refined");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "startup-errors.log"),
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception diagnosticException) when (diagnosticException is IOException or UnauthorizedAccessException)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _naturalMotion?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            _singleInstance.Dispose();
        }

        _settingsSaveGate.Dispose();
        base.OnExit(e);
    }
}
