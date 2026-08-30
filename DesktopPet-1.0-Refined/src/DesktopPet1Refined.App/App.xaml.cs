using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet1Refined.App.Models;
using DesktopPet1Refined.App.Services;

namespace DesktopPet1Refined.App;

public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _trayIcon;
    private MainWindow? _petWindow;
    private IdleAnimationPlayer? _idleAnimation;
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

        _trayIcon = new TrayIconService(
            _petWindow,
            () => _settings,
            UpdateSettings,
            RequestExit);

        // The image control is still collapsed here. The transparent window is shown first so a
        // decode failure can never expose a placeholder, designer image, or stale bitmap.
        _petWindow.Show();
        await System.Windows.Threading.Dispatcher.Yield(
            System.Windows.Threading.DispatcherPriority.Background);

        try
        {
            var neutral = LoadApprovedFrame("idle.neutral.png");
            _petWindow.SetStaticFrame(neutral.Image, neutral.AlphaPixels);
            try
            {
                var alternate = LoadApprovedFrame("idle.alt.png");
                var closedSmile = LoadApprovedFrame("idle.blink_smile.png");
                _idleAnimation = new IdleAnimationPlayer(
                    _petWindow,
                    neutral,
                    alternate,
                    closedSmile);
                _idleAnimation.SetEnabled(_settings.IdleAnimationEnabled);
            }
            catch (Exception animationException) when (animationException is
                IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The already validated neutral pose stays visible if an optional animation frame
                // is absent or invalid. No placeholder image is ever selected.
                WriteStartupError(animationException);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            WriteStartupError(exception);
        }
    }

    private static ApprovedFrame LoadApprovedFrame(string fileName)
    {
        if (fileName is not ("idle.neutral.png" or "idle.alt.png" or "idle.blink_smile.png"))
        {
            throw new InvalidDataException($"Frame '{fileName}' is not in the idle animation whitelist.");
        }

        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "sprites",
            fileName));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The approved frame '{fileName}' is missing.", path);
        }

        BitmapFrame decoded;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1)
            {
                throw new InvalidDataException($"The approved frame '{fileName}' must contain exactly one PNG frame.");
            }

            decoded = decoder.Frames[0];
        }

        if (decoded.PixelWidth != 512 || decoded.PixelHeight != 512)
        {
            throw new InvalidDataException(
                $"The approved frame '{fileName}' must be 512x512; found {decoded.PixelWidth}x{decoded.PixelHeight}.");
        }

        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = new byte[converted.PixelWidth * converted.PixelHeight];
        var opaqueCount = 0;
        for (var index = 0; index < alpha.Length; index++)
        {
            var value = pixels[(index * 4) + 3];
            alpha[index] = value;
            if (value >= 16)
            {
                opaqueCount++;
            }
        }

        if (opaqueCount == 0 || opaqueCount == alpha.Length)
        {
            throw new InvalidDataException($"The approved frame '{fileName}' has an invalid transparency plane.");
        }

        return new ApprovedFrame(converted, alpha);
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
        _idleAnimation?.SetEnabled(settings.IdleAnimationEnabled);
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
        _idleAnimation?.Dispose();
        _idleAnimation = null;
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
        _idleAnimation?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            _singleInstance.Dispose();
        }

        _settingsSaveGate.Dispose();
        base.OnExit(e);
    }

    private sealed record ApprovedFrame(BitmapSource Image, byte[] AlphaPixels);

    private sealed class IdleAnimationPlayer : IDisposable
    {
        private readonly MainWindow _window;
        private readonly AnimationStep[] _steps;
        private readonly ApprovedFrame _neutral;
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private int _stepIndex;
        private bool _enabled;

        public IdleAnimationPlayer(
            MainWindow window,
            ApprovedFrame neutral,
            ApprovedFrame alternate,
            ApprovedFrame closedSmile)
        {
            _window = window;
            _neutral = neutral;
            // This is the original 1.0 idle order and timing, but without any whole-character
            // scaling. Every step swaps one complete, approved 512x512 source frame.
            _steps =
            [
                new AnimationStep(neutral, 1_600),
                new AnimationStep(alternate, 1_200),
                new AnimationStep(closedSmile, 220),
                new AnimationStep(neutral, 1_400)
            ];
            _timer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Render,
                window.Dispatcher);
            _timer.Tick += OnTick;
        }

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            _timer.Stop();
            if (!enabled)
            {
                _window.SetStaticFrame(_neutral.Image, _neutral.AlphaPixels);
                return;
            }

            _stepIndex = 0;
            ShowCurrentStep();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _timer.Stop();
            if (!_enabled)
            {
                return;
            }

            _stepIndex = (_stepIndex + 1) % _steps.Length;
            ShowCurrentStep();
        }

        private void ShowCurrentStep()
        {
            var step = _steps[_stepIndex];
            _window.SetStaticFrame(step.Frame.Image, step.Frame.AlphaPixels);
            _timer.Interval = TimeSpan.FromMilliseconds(step.DurationMs);
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }

        private sealed record AnimationStep(ApprovedFrame Frame, int DurationMs);
    }
}
