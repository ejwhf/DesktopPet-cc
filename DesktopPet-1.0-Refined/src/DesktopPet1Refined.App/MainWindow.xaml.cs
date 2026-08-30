using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet1Refined.App.Interop;
using DesktopPet1Refined.App.Models;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;

namespace DesktopPet1Refined.App;

public partial class MainWindow : Window
{
    private const int RecoveryHotKeyId = 0x3152;
    private const byte AlphaHitThreshold = 16;
    private readonly DispatcherTimer _savePlacementTimer;
    private readonly DispatcherTimer _hintTimer;
    private HwndSource? _hwndSource;
    private nint _windowHandle;
    private byte[]? _alphaPixels;
    private int _pixelWidth;
    private int _pixelHeight;
    private bool _allowClose;
    private bool _isDragging;
    private bool _alwaysOnTop = true;

    public MainWindow()
    {
        InitializeComponent();

        _savePlacementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _savePlacementTimer.Tick += (_, _) =>
        {
            _savePlacementTimer.Stop();
            RaisePlacementChanged();
        };
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            RecoveryHint.Visibility = Visibility.Collapsed;
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    public HitTestMode HitTestMode { get; private set; } = HitTestMode.CharacterPixels;

    public bool IsPositionLocked { get; private set; }

    public event EventHandler? PlacementChanged;

    public event EventHandler<HitTestMode>? HitTestModeChanged;

    public void SetStaticFrame(BitmapSource image, byte[] alphaPixels)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(alphaPixels);
        if (image.PixelWidth != 512 || image.PixelHeight != 512 ||
            alphaPixels.Length != image.PixelWidth * image.PixelHeight)
        {
            throw new ArgumentException("Static frame dimensions or alpha data are invalid.");
        }

        _alphaPixels = alphaPixels;
        _pixelWidth = image.PixelWidth;
        _pixelHeight = image.PixelHeight;
        PetImage.Source = image;
        PetImage.Visibility = Visibility.Visible;
    }

    public void ApplyDisplaySettings(double scale, double opacity, bool alwaysOnTop, bool lockPosition)
    {
        Width = 300 * Math.Clamp(scale, 0.5, 2);
        Height = 360 * Math.Clamp(scale, 0.5, 2);
        Opacity = Math.Clamp(opacity, 0.2, 1);
        _alwaysOnTop = alwaysOnTop;
        Topmost = alwaysOnTop;
        IsPositionLocked = lockPosition;
        ClampToVisibleDesktop();
    }

    public void SetHitTestMode(HitTestMode mode)
    {
        if (HitTestMode == mode)
        {
            return;
        }

        HitTestMode = mode;
        ApplyNativeHitTestStyle();
        HitTestModeChanged?.Invoke(this, mode);
    }

    public void RecoverControl()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RecoverControl));
            return;
        }

        SetHitTestMode(HitTestMode.CharacterPixels);
        Show();
        ClampToVisibleDesktop();
        var configuredTopmost = _alwaysOnTop;
        Topmost = false;
        Topmost = true;
        Topmost = configuredTopmost;
        RecoveryHint.Visibility = Visibility.Visible;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    public void RestorePlacement(double? left, double? top, string? monitorId)
    {
        if (left is { } x && top is { } y && double.IsFinite(x) && double.IsFinite(y))
        {
            Left = x;
            Top = y;
        }
        else
        {
            SetDefaultPlacement();
        }

        ClampToVisibleDesktop(monitorId);
    }

    public void ResetPlacement()
    {
        SetDefaultPlacement();
        ClampToVisibleDesktop();
        RaisePlacementChanged();
    }

    public string? GetCurrentMonitorId() => _windowHandle == nint.Zero
        ? null
        : Forms.Screen.FromHandle(_windowHandle).DeviceName;

    public void AllowClose() => _allowClose = true;

    private void SetDefaultPlacement()
    {
        Left = SystemParameters.WorkArea.Right - Math.Max(Width, 300) - 24;
        Top = SystemParameters.WorkArea.Bottom - Math.Max(Height, 360) - 24;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WindowProcedure);
        _ = NativeMethods.RegisterHotKey(
            _windowHandle,
            RecoveryHotKeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
            NativeMethods.VkR);
        ApplyNativeHitTestStyle();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClampToVisibleDesktop();
        ApplyNativeHitTestStyle();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_isDragging && IsLoaded)
        {
            _savePlacementTimer.Stop();
            _savePlacementTimer.Start();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(() => ClampToVisibleDesktop()));
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HitTestMode == HitTestMode.ClickThrough || IsPositionLocked ||
            e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        _isDragging = true;
        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _isDragging = false;
            ClampToVisibleDesktop();
            RaisePlacementChanged();
        }
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmNcHitTest when HitTestMode == HitTestMode.CharacterPixels:
            {
                var screenPoint = new WpfPoint(
                    NativeMethods.GetSignedLowWord(lParam),
                    NativeMethods.GetSignedHighWord(lParam));
                if (!IsOpaqueCharacterPixel(PointFromScreen(screenPoint)))
                {
                    handled = true;
                    return new nint(NativeMethods.HtTransparent);
                }
                break;
            }
            case NativeMethods.WmHotKey when wParam.ToInt32() == RecoveryHotKeyId:
                handled = true;
                RecoverControl();
                break;
            case NativeMethods.WmDisplayChange:
                Dispatcher.BeginInvoke(new Action(() => ClampToVisibleDesktop()));
                break;
        }

        return nint.Zero;
    }

    private bool IsOpaqueCharacterPixel(WpfPoint windowPoint)
    {
        if (PetImage.Visibility != Visibility.Visible || PetImage.Source is not BitmapSource bitmap ||
            _alphaPixels is null || _pixelWidth != bitmap.PixelWidth || _pixelHeight != bitmap.PixelHeight)
        {
            return false;
        }

        var measured = PetImage.ActualWidth > 0 && PetImage.ActualHeight > 0;
        var topLeft = measured
            ? PetImage.TranslatePoint(new WpfPoint(0, 0), this)
            : new WpfPoint(PetImage.Margin.Left, PetImage.Margin.Top);
        var localX = windowPoint.X - topLeft.X;
        var localY = windowPoint.Y - topLeft.Y;
        var controlWidth = measured
            ? PetImage.ActualWidth
            : Math.Max(0, ActualWidth - PetImage.Margin.Left - PetImage.Margin.Right);
        var controlHeight = measured
            ? PetImage.ActualHeight
            : Math.Max(0, ActualHeight - PetImage.Margin.Top - PetImage.Margin.Bottom);
        if (controlWidth <= 0 || controlHeight <= 0 ||
            localX < 0 || localY < 0 || localX >= controlWidth || localY >= controlHeight)
        {
            return false;
        }

        var imageRatio = bitmap.PixelWidth / (double)bitmap.PixelHeight;
        var controlRatio = controlWidth / controlHeight;
        var renderedWidth = controlRatio > imageRatio ? controlHeight * imageRatio : controlWidth;
        var renderedHeight = controlRatio > imageRatio ? controlHeight : controlWidth / imageRatio;
        var renderLeft = (controlWidth - renderedWidth) / 2;
        var renderTop = (controlHeight - renderedHeight) / 2;
        if (localX < renderLeft || localY < renderTop ||
            localX >= renderLeft + renderedWidth || localY >= renderTop + renderedHeight)
        {
            return false;
        }

        var pixelX = Math.Clamp(
            (int)((localX - renderLeft) / renderedWidth * bitmap.PixelWidth),
            0,
            bitmap.PixelWidth - 1);
        var pixelY = Math.Clamp(
            (int)((localY - renderTop) / renderedHeight * bitmap.PixelHeight),
            0,
            bitmap.PixelHeight - 1);
        return _alphaPixels[(pixelY * _pixelWidth) + pixelX] >= AlphaHitThreshold;
    }

    private void ApplyNativeHitTestStyle()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExToolWindow;
        if (HitTestMode == HitTestMode.ClickThrough)
        {
            style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate;
        }
        else
        {
            style &= ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);
        }

        _ = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, new nint(style));
        _ = NativeMethods.SetWindowPos(
            _windowHandle,
            nint.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
    }

    private void ClampToVisibleDesktop(string? preferredMonitorId = null)
    {
        const double visibleGrabArea = 48;
        var screens = Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return;
        }

        if (!double.IsFinite(Left) || !double.IsFinite(Top))
        {
            SetDefaultPlacement();
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var current = new Rect(Left, Top, Math.Max(ActualWidth, Width), Math.Max(ActualHeight, Height));
        var target = screens.FirstOrDefault(screen =>
                         string.Equals(screen.DeviceName, preferredMonitorId, StringComparison.OrdinalIgnoreCase))
                     ?? screens
                         .OrderByDescending(screen => IntersectionArea(current, ToLogicalRect(screen.WorkingArea, scaleX, scaleY)))
                         .ThenBy(screen => DistanceSquared(current, ToLogicalRect(screen.WorkingArea, scaleX, scaleY)))
                         .First();
        var workArea = ToLogicalRect(target.WorkingArea, scaleX, scaleY);
        Left = Math.Clamp(Left, workArea.Left - Math.Max(0, current.Width - visibleGrabArea), workArea.Right - visibleGrabArea);
        Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - visibleGrabArea);
    }

    private static Rect ToLogicalRect(System.Drawing.Rectangle rectangle, double scaleX, double scaleY) =>
        new(rectangle.Left / scaleX, rectangle.Top / scaleY, rectangle.Width / scaleX, rectangle.Height / scaleY);

    private static double IntersectionArea(Rect left, Rect right)
    {
        var intersection = Rect.Intersect(left, right);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }

    private static double DistanceSquared(Rect left, Rect right)
    {
        var x = left.Left + (left.Width / 2) - right.Left - (right.Width / 2);
        var y = left.Top + (left.Height / 2) - right.Top - (right.Height / 2);
        return (x * x) + (y * y);
    }

    private void RaisePlacementChanged()
    {
        if (double.IsFinite(Left) && double.IsFinite(Top))
        {
            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        RaisePlacementChanged();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _savePlacementTimer.Stop();
        _hintTimer.Stop();
        if (_windowHandle != nint.Zero)
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, RecoveryHotKeyId);
        }
        _hwndSource?.RemoveHook(WindowProcedure);
    }
}
