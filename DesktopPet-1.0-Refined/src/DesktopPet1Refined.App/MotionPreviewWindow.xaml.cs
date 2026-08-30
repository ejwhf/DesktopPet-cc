using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopPet1Refined.App.NaturalMotion;
using DesktopPet1Refined.NaturalMotion.Catalog;
using DesktopPet1Refined.NaturalMotion.Models;
using DesktopPet1Refined.NaturalMotion.Runtime;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace DesktopPet1Refined.App;

public partial class MotionPreviewWindow : Window
{
    private readonly NaturalMotionCatalog _catalog;
    private readonly AssetImageLoader _assets;
    private readonly MotionSceneRenderer _renderer;
    private readonly MotionTimelinePlayer _timeline = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TranslateTransform _windowMotionPreview = new();
    private MotionDefinition? _selectedMotion;
    private bool _rendering;

    internal MotionPreviewWindow(NaturalMotionCatalog catalog, AssetImageLoader assets)
    {
        InitializeComponent();
        _catalog = catalog;
        _assets = assets;
        _renderer = new MotionSceneRenderer(PreviewLayerCanvas, PreviewDebugCanvas, assets);
        PreviewMotionRoot.RenderTransform = _windowMotionPreview;
        Closed += OnClosed;

        foreach (var motionId in catalog.Motions.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            MotionList.Items.Add(motionId);
        }

        if (MotionList.Items.Count == 0)
        {
            SetControlsEnabled(false);
            ShowBasePose("06_idle");
            StatusText.Text = "正式 Motion 目录为空。这是当前阶段的预期状态：动画底座已建立，但尚未制作任何具体动作。";
        }
        else
        {
            MotionList.SelectedIndex = 0;
        }
    }

    private void OnMotionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StopRendering();
        _timeline.Stop();
        _windowMotionPreview.X = 0;
        _windowMotionPreview.Y = 0;
        if (MotionList.SelectedItem is not string id || !_catalog.Motions.TryGetValue(id, out var motion))
        {
            _selectedMotion = null;
            SetControlsEnabled(false);
            return;
        }
        _selectedMotion = motion;
        ShowBasePose(motion.BasePose);
        _renderer.Prepare(motion);
        ApplyOverlaySettings();
        SetControlsEnabled(true);
        StatusText.Text = $"{motion.Id} · {motion.DurationMs:0} ms · {motion.LoopMode} · 尚未接入自动行为";
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_selectedMotion is null)
        {
            return;
        }
        if (_timeline.State == MotionPlaybackState.Paused)
        {
            _timeline.Resume(_clock.Elapsed);
        }
        else if (_timeline.State == MotionPlaybackState.Stopped)
        {
            _timeline.Start(_selectedMotion, _clock.Elapsed);
        }
        AttachRendering();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        _timeline.Pause(_clock.Elapsed);
        StopRendering();
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        if (_selectedMotion is null)
        {
            return;
        }
        _renderer.Prepare(_selectedMotion);
        ApplyOverlaySettings();
        _timeline.Start(_selectedMotion, _clock.Elapsed);
        AttachRendering();
    }

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedSelector.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), System.Globalization.CultureInfo.InvariantCulture, out var speed))
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
            "dark" => new SolidColorBrush(WpfColor.FromRgb(30, 30, 34)),
            _ => new SolidColorBrush(WpfColor.FromRgb(232, 232, 232))
        };
    }

    private void OnOverlayChanged(object sender, RoutedEventArgs e) => ApplyOverlaySettings();

    private void ApplyOverlaySettings()
    {
        if (_renderer is null)
        {
            return;
        }
        _renderer.ShowPivots = PivotToggle.IsChecked == true;
        _renderer.ShowAnchors = AnchorToggle.IsChecked == true;
        _renderer.ShowBounds = BoundsToggle.IsChecked == true;
        _renderer.RefreshDebugOverlay();
    }

    private void ShowBasePose(string poseId)
    {
        var pose = _catalog.Poses[poseId];
        PreviewPoseImage.Source = _assets.LoadPose(
            pose.File,
            (int)pose.CanvasSize.Width,
            (int)pose.CanvasSize.Height).Image;
    }

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

    private void OnRendering(object? sender, EventArgs e)
    {
        var sample = _timeline.Tick(_clock.Elapsed);
        var offset = _renderer.Apply(sample);
        _windowMotionPreview.X = offset.X;
        _windowMotionPreview.Y = offset.Y;
        StatusText.Text = $"{_selectedMotion!.Id} · {sample.TimeMs:0} / {_selectedMotion.DurationMs:0} ms · {_timeline.State}";
        if (_timeline.State == MotionPlaybackState.Stopped)
        {
            StopRendering();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        PauseButton.IsEnabled = enabled;
        RestartButton.IsEnabled = enabled;
        SpeedSelector.IsEnabled = enabled;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopRendering();
        _timeline.Stop();
    }
}
