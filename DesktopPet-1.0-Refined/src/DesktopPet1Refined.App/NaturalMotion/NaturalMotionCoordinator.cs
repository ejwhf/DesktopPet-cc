using System.IO;
using DesktopPet1Refined.NaturalMotion.Catalog;

namespace DesktopPet1Refined.App.NaturalMotion;

internal sealed class NaturalMotionCoordinator : IDisposable
{
    private readonly MainWindow _petWindow;
    private readonly NaturalMotionCatalog _catalog;
    private readonly AssetImageLoader _assets;
    private readonly LiveMotionPlayer _livePlayer;
    private readonly BlinkController _blink;
    private MotionPreviewWindow? _previewWindow;
    private BlinkDebugWindow? _blinkDebugWindow;
    private bool _reduceMotion;
    private bool _dragging;
    private bool _actionPlaying;

    public NaturalMotionCoordinator(MainWindow petWindow)
    {
        _petWindow = petWindow;
        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        _assets = new AssetImageLoader(assetsRoot);
        _catalog = NaturalMotionCatalog.Load(Path.Combine(assetsRoot, "natural-motion"));
        _livePlayer = new LiveMotionPlayer(petWindow, _catalog, _assets);
        _blink = new BlinkController(petWindow, _assets);
        _petWindow.DragStarted += OnDragStarted;
        _petWindow.DragCompleted += OnDragCompleted;
        _petWindow.IsVisibleChanged += OnVisibilityChanged;
        _livePlayer.PlaybackActiveChanged += OnPlaybackActiveChanged;
    }

    public IReadOnlyList<string> MotionIds => _catalog.Motions.Keys
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void ShowStaticBasePose()
    {
        var pose = _catalog.Poses["06_idle"];
        var frame = _assets.LoadPose(pose.File, (int)pose.CanvasSize.Width, (int)pose.CanvasSize.Height);
        _petWindow.SetStaticFrame(frame.Image, frame.AlphaPixels);
    }

    public void PlayDebugMotion(string motionId) => _livePlayer.Play(motionId);

    public void TriggerBlink() => _blink.Trigger();

    public void SetReduceMotion(bool reduceMotion)
    {
        _reduceMotion = reduceMotion;
        UpdateBlinkAvailability();
    }

    public void OpenPreview()
    {
        if (_previewWindow is { IsLoaded: true })
        {
            _previewWindow.Activate();
            return;
        }
        _previewWindow = new MotionPreviewWindow(_catalog, _assets);
        _previewWindow.Closed += (_, _) => _previewWindow = null;
        _previewWindow.Show();
    }

    public void OpenBlinkDebug()
    {
        if (_blinkDebugWindow is { IsLoaded: true })
        {
            _blinkDebugWindow.Activate();
            return;
        }
        _blinkDebugWindow = new BlinkDebugWindow(_catalog, _assets);
        _blinkDebugWindow.Closed += (_, _) => _blinkDebugWindow = null;
        _blinkDebugWindow.Show();
    }

    private void OnDragStarted(object? sender, EventArgs e)
    {
        _dragging = true;
        UpdateBlinkAvailability();
        _livePlayer.InterruptForUserInteraction();
    }

    private void OnDragCompleted(object? sender, EventArgs e)
    {
        _dragging = false;
        UpdateBlinkAvailability();
    }

    private void OnVisibilityChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e) =>
        UpdateBlinkAvailability();

    private void OnPlaybackActiveChanged(object? sender, bool playing)
    {
        _actionPlaying = playing;
        UpdateBlinkAvailability();
    }

    private void UpdateBlinkAvailability() =>
        _blink.SetIdleAvailable(_petWindow.IsVisible && !_reduceMotion && !_dragging && !_actionPlaying);

    public void Dispose()
    {
        _petWindow.DragStarted -= OnDragStarted;
        _petWindow.DragCompleted -= OnDragCompleted;
        _petWindow.IsVisibleChanged -= OnVisibilityChanged;
        _livePlayer.PlaybackActiveChanged -= OnPlaybackActiveChanged;
        _previewWindow?.Close();
        _previewWindow = null;
        _blinkDebugWindow?.Close();
        _blinkDebugWindow = null;
        _blink.Dispose();
        _livePlayer.Dispose();
    }
}
