using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet1Refined.App.NaturalMotion;
using DesktopPet1Refined.NaturalMotion.Catalog;
using DesktopPet1Refined.NaturalMotion.Runtime;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace DesktopPet1Refined.App;

public partial class BlinkDebugWindow : Window
{
    private readonly BlinkTimeline _timeline = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IReadOnlyDictionary<BlinkFrame, BitmapSource?> _frames;
    private bool _rendering;

    internal BlinkDebugWindow(NaturalMotionCatalog catalog, AssetImageLoader assets)
    {
        InitializeComponent();
        var pose = catalog.Poses["06_idle"];
        BaseImage.Source = assets.LoadPose(pose.File, 512, 512).Image;
        _frames = new Dictionary<BlinkFrame, BitmapSource?>
        {
            [BlinkFrame.Open] = null,
            [BlinkFrame.Forty] = assets.LoadBitmap("natural-motion/rigs/idle/blink_40.png"),
            [BlinkFrame.SeventyFive] = assets.LoadBitmap("natural-motion/rigs/idle/blink_75.png"),
            [BlinkFrame.Closed] = assets.LoadBitmap("natural-motion/rigs/idle/blink_closed.png")
        };
        Closed += OnClosed;
        ShowFrame(BlinkFrame.Open);
    }

    private void OnTrigger(object sender, RoutedEventArgs e)
    {
        if (_timeline.IsPaused)
        {
            _timeline.Resume(_clock.Elapsed);
        }
        else if (_timeline.Phase == BlinkPhase.Idle)
        {
            _timeline.Start(_clock.Elapsed);
        }
        AttachRendering();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        _timeline.Pause(_clock.Elapsed);
        StopRendering();
        StatusText.Text = $"Paused · {_timeline.Phase}";
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        _timeline.Start(_clock.Elapsed);
        AttachRendering();
    }

    private void OnShowOpen(object sender, RoutedEventArgs e) => ShowFixed(BlinkFrame.Open);
    private void OnShow40(object sender, RoutedEventArgs e) => ShowFixed(BlinkFrame.Forty);
    private void OnShow75(object sender, RoutedEventArgs e) => ShowFixed(BlinkFrame.SeventyFive);
    private void OnShowClosed(object sender, RoutedEventArgs e) => ShowFixed(BlinkFrame.Closed);

    private void ShowFixed(BlinkFrame frame)
    {
        _timeline.Stop();
        StopRendering();
        ShowFrame(frame);
        StatusText.Text = $"Fixed frame · {frame}";
    }

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedSelector.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
        {
            _timeline.Speed = speed;
        }
    }

    private void OnBackgroundChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewBackground is null || BackgroundSelector.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        PreviewBackground.Background = item.Tag?.ToString() switch
        {
            "light" => WpfBrushes.White,
            "dark" => new SolidColorBrush(WpfColor.FromRgb(24, 26, 31)),
            _ => new SolidColorBrush(WpfColor.FromRgb(232, 232, 232))
        };
    }

    private void OnRoiChanged(object sender, RoutedEventArgs e) =>
        RoiCanvas.Visibility = RoiToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnRendering(object? sender, EventArgs e)
    {
        var sample = _timeline.Tick(_clock.Elapsed);
        ShowFrame(sample.Frame);
        StatusText.Text = $"{sample.Phase} · {sample.Progress:P0} · {sample.Frame} · {_timeline.Speed:0.##}×";
        if (sample.IsComplete)
        {
            StopRendering();
        }
    }

    private void ShowFrame(BlinkFrame frame) => BlinkImage.Source = _frames[frame];

    private void AttachRendering()
    {
        if (_rendering)
        {
            return;
        }
        CompositionTarget.Rendering += OnRendering;
        _rendering = true;
    }

    private void StopRendering()
    {
        if (!_rendering)
        {
            return;
        }
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timeline.Stop();
        StopRendering();
    }
}
