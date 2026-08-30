using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App.Interop;
using DesktopPet.App.Services;
using AppHitTestMode = DesktopPet.App.Models.HitTestMode;
using DesktopPet.Core.Timeline;
using WpfPoint = System.Windows.Point;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Views;

public partial class MainWindow : Window
{
    private const int RecoveryHotKeyId = 0x4450;
    private readonly DispatcherTimer _savePlacementTimer;
    private readonly DispatcherTimer _hintTimer;
    private readonly ScaleTransform _poseScale = new();
    private readonly RotateTransform _poseRotation = new();
    private readonly TranslateTransform _poseTranslation = new();
    private HwndSource? _hwndSource;
    private nint _windowHandle;
    private byte[]? _hitTestPixels;
    private int _hitTestPixelWidth;
    private int _hitTestPixelHeight;
    private int _hitTestStride;
    private bool _allowClose;
    private bool _isDragging;
    private bool _alwaysOnTop = true;
    private byte _alphaHitThreshold = 16;

    public MainWindow()
    {
        InitializeComponent();

        var poseTransforms = new TransformGroup();
        poseTransforms.Children.Add(_poseScale);
        poseTransforms.Children.Add(_poseRotation);
        poseTransforms.Children.Add(_poseTranslation);
        PetImage.RenderTransform = poseTransforms;

        _savePlacementTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _savePlacementTimer.Tick += (_, _) =>
        {
            _savePlacementTimer.Stop();
            SavePlacement();
        };

        _hintTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
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

    public AppHitTestMode HitTestMode { get; private set; } = AppHitTestMode.CharacterPixels;

    public event EventHandler<AppHitTestMode>? HitTestModeChanged;

    public event EventHandler? PlacementChanged;

    public event EventHandler? PetDragStarted;

    public event EventHandler? PetDragCompleted;

    public event EventHandler? PetClicked;

    /// <summary>Raised by the Debug action picker so the M2 controller can switch immediately.</summary>
    public event EventHandler<string>? DebugActionRequested;

    /// <summary>Displays one approved RGBA action frame.</summary>
    public void SetPetImage(BitmapSource? image)
    {
        PetImage.Source = image;
        PetImage.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        BuildAlphaHitMask(image);
    }

    /// <summary>Displays a cached animation frame without re-copying its alpha channel.</summary>
    public void SetPetFrame(BitmapSource image, byte[] alphaPixels)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(alphaPixels);
        if (alphaPixels.Length != image.PixelWidth * image.PixelHeight)
        {
            throw new ArgumentException("Alpha plane dimensions do not match the bitmap.", nameof(alphaPixels));
        }
        PetImage.Source = image;
        PetImage.Visibility = Visibility.Visible;
        _hitTestPixels = alphaPixels;
        _hitTestPixelWidth = image.PixelWidth;
        _hitTestPixelHeight = image.PixelHeight;
        _hitTestStride = image.PixelWidth;
    }

    public void SetAlphaHitThreshold(int threshold) =>
        _alphaHitThreshold = (byte)Math.Clamp(threshold, 1, byte.MaxValue);

    /// <summary>Applies one sampled full-frame pose while preserving a stable foot-area origin.</summary>
    public void ApplyPose(
        BitmapSource image,
        PoseTransform transform,
        double transformOriginX = 0.5,
        double transformOriginY = 0.9)
    {
        if (!ReferenceEquals(PetImage.Source, image))
        {
            SetPetImage(image);
        }

        ApplyPoseTransform(image, transform, transformOriginX, transformOriginY);
    }

    public void ApplyAnimationFrame(
        BitmapSource image,
        byte[] alphaPixels,
        PoseTransform transform,
        double transformOriginX,
        double transformOriginY)
    {
        if (!ReferenceEquals(PetImage.Source, image))
        {
            SetPetFrame(image, alphaPixels);
        }
        ApplyPoseTransform(image, transform, transformOriginX, transformOriginY);
    }

    private void ApplyPoseTransform(
        BitmapSource image,
        PoseTransform transform,
        double transformOriginX,
        double transformOriginY)
    {
        PetImage.RenderTransformOrigin = GetImageTransformOrigin(
            image,
            transformOriginX,
            transformOriginY);
        _poseScale.ScaleX = transform.Scale;
        _poseScale.ScaleY = transform.Scale;
        _poseRotation.Angle = transform.RotationDegrees;
        _poseTranslation.X = transform.OffsetX;
        _poseTranslation.Y = transform.OffsetY;
        PetImage.Opacity = transform.Opacity;
    }

    private WpfPoint GetImageTransformOrigin(BitmapSource image, double sourceX, double sourceY)
    {
        var controlWidth = PetImage.ActualWidth;
        var controlHeight = PetImage.ActualHeight;
        if (controlWidth <= 0 || controlHeight <= 0 || image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            return new WpfPoint(0.5, 0.9);
        }

        var imageRatio = image.PixelWidth / (double)image.PixelHeight;
        var controlRatio = controlWidth / controlHeight;
        var renderedWidth = controlRatio > imageRatio ? controlHeight * imageRatio : controlWidth;
        var renderedHeight = controlRatio > imageRatio ? controlHeight : controlWidth / imageRatio;
        var renderLeft = (controlWidth - renderedWidth) / 2;
        var renderTop = (controlHeight - renderedHeight) / 2;
        var originX = (renderLeft + (Math.Clamp(sourceX, 0, 1) * renderedWidth)) / controlWidth;
        var originY = (renderTop + (Math.Clamp(sourceY, 0, 1) * renderedHeight)) / controlHeight;
        return new WpfPoint(originX, originY);
    }

    public void SetHitTestMode(AppHitTestMode mode)
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

        SetHitTestMode(AppHitTestMode.CharacterPixels);
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

    public void ToggleActionPicker()
    {
#if DEBUG
        ActionPicker.Visibility = ActionPicker.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (ActionPicker.Visibility == Visibility.Visible && HitTestMode == AppHitTestMode.ClickThrough)
        {
            SetHitTestMode(AppHitTestMode.WholeWindow);
        }
#endif
    }

    public void AllowClose() => _allowClose = true;

    public void ApplyDisplaySettings(
        double scale,
        double opacity,
        bool alwaysOnTop,
        bool lockPosition)
    {
        Width = 300 * Math.Clamp(scale, 0.5, 2);
        Height = 360 * Math.Clamp(scale, 0.5, 2);
        Opacity = Math.Clamp(opacity, 0.2, 1);
        _alwaysOnTop = alwaysOnTop;
        Topmost = alwaysOnTop;
        IsPositionLocked = lockPosition;
        ClampToVisibleDesktop();
        SchedulePlacementSave();
    }

    public bool IsPositionLocked { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WindowProcedure);

        _ = NativeMethods.RegisterHotKey(
            _windowHandle,
            RecoveryHotKeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
            NativeMethods.VkP);
        ApplyNativeHitTestStyle();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClampToVisibleDesktop();
        ApplyNativeHitTestStyle();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isDragging)
        {
            return;
        }

        SchedulePlacementSave();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(() => ClampToVisibleDesktop()));
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (HitTestMode == AppHitTestMode.ClickThrough ||
            IsPositionLocked ||
            ActionPicker.IsMouseOver ||
            e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        _isDragging = true;
        var startedLeft = Left;
        var startedTop = Top;
        PetDragStarted?.Invoke(this, EventArgs.Empty);
        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // The pointer may have been released before WPF entered the native move loop.
        }
        finally
        {
            _isDragging = false;
            ClampToVisibleDesktop();
            SchedulePlacementSave();
            var moved = Math.Abs(Left - startedLeft) > 2 || Math.Abs(Top - startedTop) > 2;
            if (moved)
            {
                PetDragCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                PetClicked?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmNcHitTest when HitTestMode == AppHitTestMode.CharacterPixels:
            {
                var screenPoint = new WpfPoint(
                    NativeMethods.GetSignedLowWord(lParam),
                    NativeMethods.GetSignedHighWord(lParam));
                var localPoint = PointFromScreen(screenPoint);
                if (!IsCharacterPixel(localPoint))
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

    private bool IsCharacterPixel(WpfPoint windowPoint)
    {
        if (ActionPicker.Visibility == Visibility.Visible && IsPointWithin(ActionPicker, windowPoint))
        {
            return true;
        }

        if (PetImage.Visibility == Visibility.Visible && PetImage.Source is BitmapSource bitmap)
        {
            return IsOpaqueImagePixel(bitmap, windowPoint);
        }

        return false;
    }

    private bool IsPointWithin(FrameworkElement element, WpfPoint windowPoint)
    {
        var topLeft = element.TranslatePoint(new WpfPoint(0, 0), this);
        return windowPoint.X >= topLeft.X &&
               windowPoint.Y >= topLeft.Y &&
               windowPoint.X < topLeft.X + element.ActualWidth &&
               windowPoint.Y < topLeft.Y + element.ActualHeight;
    }

    private bool IsOpaqueImagePixel(BitmapSource bitmap, WpfPoint windowPoint)
    {
        var isMeasured = PetImage.ActualWidth > 0 && PetImage.ActualHeight > 0;
        var topLeft = isMeasured
            ? PetImage.TranslatePoint(new WpfPoint(0, 0), this)
            : new WpfPoint(PetImage.Margin.Left, PetImage.Margin.Top);
        var localX = windowPoint.X - topLeft.X;
        var localY = windowPoint.Y - topLeft.Y;
        var controlWidth = isMeasured
            ? PetImage.ActualWidth
            : Math.Max(0, ActualWidth - PetImage.Margin.Left - PetImage.Margin.Right);
        var controlHeight = isMeasured
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

        var pixelX = Math.Clamp((int)((localX - renderLeft) / renderedWidth * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
        var pixelY = Math.Clamp((int)((localY - renderTop) / renderedHeight * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);

        if (_hitTestPixels is null ||
            _hitTestPixelWidth != bitmap.PixelWidth ||
            _hitTestPixelHeight != bitmap.PixelHeight)
        {
            return true;
        }

        return _hitTestPixels[(pixelY * _hitTestStride) + pixelX] >= _alphaHitThreshold;
    }

    private void BuildAlphaHitMask(BitmapSource? bitmap)
    {
        _hitTestPixels = null;
        _hitTestPixelWidth = 0;
        _hitTestPixelHeight = 0;
        _hitTestStride = 0;
        if (bitmap is null || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            return;
        }

        var converted = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        _hitTestPixelWidth = converted.PixelWidth;
        _hitTestPixelHeight = converted.PixelHeight;
        var bgraStride = _hitTestPixelWidth * 4;
        var bgra = new byte[bgraStride * _hitTestPixelHeight];
        converted.CopyPixels(bgra, bgraStride, 0);
        _hitTestStride = _hitTestPixelWidth;
        _hitTestPixels = new byte[_hitTestStride * _hitTestPixelHeight];
        for (var index = 0; index < _hitTestPixels.Length; index++)
        {
            _hitTestPixels[index] = bgra[(index * 4) + 3];
        }
    }

    private void OnActionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActionComboBox.SelectedItem is ComboBoxItem { Tag: string actionId })
        {
            DebugActionRequested?.Invoke(this, actionId);
        }
    }

    private void ApplyNativeHitTestStyle()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        // Make taskbar and Alt-Tab exclusion explicit instead of relying only on WPF's
        // ShowInTaskbar owner-window implementation.
        style |= NativeMethods.WsExToolWindow;
        if (HitTestMode == AppHitTestMode.ClickThrough)
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

    public void RestorePlacement(double? left, double? top, string? monitorId = null)
    {
        if (!left.HasValue || !top.HasValue ||
            !double.IsFinite(left.Value) || !double.IsFinite(top.Value))
        {
            Left = SystemParameters.WorkArea.Right - ActualWidth - 24;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 24;
        }
        else
        {
            Left = left.Value;
            Top = top.Value;
        }

        ClampToVisibleDesktop(monitorId);
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
            Left = SystemParameters.WorkArea.Right - Math.Max(ActualWidth, Width) - 24;
            Top = SystemParameters.WorkArea.Bottom - Math.Max(ActualHeight, Height) - 24;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var currentRect = new Rect(Left, Top, Math.Max(ActualWidth, Width), Math.Max(ActualHeight, Height));
        var target = screens
            .FirstOrDefault(screen => string.Equals(screen.DeviceName, preferredMonitorId, StringComparison.OrdinalIgnoreCase))
            ?? screens
                .OrderByDescending(screen => IntersectionArea(currentRect, ToLogicalRect(screen.WorkingArea, scaleX, scaleY)))
                .ThenBy(screen => DistanceSquared(currentRect, ToLogicalRect(screen.WorkingArea, scaleX, scaleY)))
                .First();
        var workArea = ToLogicalRect(target.WorkingArea, scaleX, scaleY);
        var minLeft = workArea.Left - Math.Max(0, currentRect.Width - visibleGrabArea);
        var maxLeft = workArea.Right - visibleGrabArea;
        var minTop = workArea.Top;
        var maxTop = workArea.Bottom - visibleGrabArea;
        Left = Math.Clamp(Left, minLeft, maxLeft);
        Top = Math.Clamp(Top, minTop, maxTop);
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
        var x = left.Left + (left.Width / 2) - (right.Left + (right.Width / 2));
        var y = left.Top + (left.Height / 2) - (right.Top + (right.Height / 2));
        return (x * x) + (y * y);
    }

    private void SchedulePlacementSave()
    {
        if (!IsLoaded || !double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return;
        }

        _savePlacementTimer.Stop();
        _savePlacementTimer.Start();
    }

    private void SavePlacement()
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top))
        {
            return;
        }

        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        SavePlacement();
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
