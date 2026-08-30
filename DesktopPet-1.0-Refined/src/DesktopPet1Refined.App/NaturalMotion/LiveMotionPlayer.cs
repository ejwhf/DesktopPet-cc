using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using DesktopPet1Refined.NaturalMotion.Catalog;
using DesktopPet1Refined.NaturalMotion.Models;
using DesktopPet1Refined.NaturalMotion.Runtime;

namespace DesktopPet1Refined.App.NaturalMotion;

internal sealed class LiveMotionPlayer : IDisposable
{
    private readonly MainWindow _window;
    private readonly NaturalMotionCatalog _catalog;
    private readonly AssetImageLoader _assets;
    private readonly MotionSceneRenderer _renderer;
    private readonly MotionTimelinePlayer _timeline = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _rendering;
    private bool _playbackActive;

    public LiveMotionPlayer(MainWindow window, NaturalMotionCatalog catalog, AssetImageLoader assets)
    {
        _window = window;
        _catalog = catalog;
        _assets = assets;
        _renderer = new MotionSceneRenderer(window.MotionLayerCanvasElement, window.MotionDebugCanvasElement, assets);
    }

    public bool IsPlaying => _timeline.State == MotionPlaybackState.Playing;

    public event EventHandler<bool>? PlaybackActiveChanged;

    public void Play(string motionId)
    {
        if (!_catalog.Motions.TryGetValue(motionId, out var motion))
        {
            throw new KeyNotFoundException($"Motion '{motionId}' is not registered.");
        }
        if (!_catalog.Poses.TryGetValue(motion.BasePose, out var pose))
        {
            throw new InvalidDataException($"Motion '{motionId}' has no valid base pose.");
        }

        InterruptForUserInteraction();
        var decoded = _assets.LoadPose(pose.File, (int)pose.CanvasSize.Width, (int)pose.CanvasSize.Height);
        _window.SetStaticFrame(decoded.Image, decoded.AlphaPixels);
        _renderer.Prepare(motion);
        _window.BeginWindowMotion();
        _timeline.Start(motion, _clock.Elapsed);
        SetPlaybackActive(true);
        AttachRendering();
    }

    public void InterruptForUserInteraction()
    {
        if (_timeline.State != MotionPlaybackState.Stopped)
        {
            _timeline.Stop();
        }
        StopRendering(restoreWindow: true);
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

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_timeline.State != MotionPlaybackState.Playing)
        {
            StopRendering(restoreWindow: true);
            return;
        }
        var sample = _timeline.Tick(_clock.Elapsed);
        var windowOffset = _renderer.Apply(sample);
        _window.ApplyWindowMotionOffset(windowOffset.X, windowOffset.Y);
        if (_timeline.State == MotionPlaybackState.Stopped)
        {
            var restore = _timeline.Motion?.ExitPolicy != MotionExitPolicy.HoldFinalFrame;
            StopRendering(restore);
        }
    }

    private void StopRendering(bool restoreWindow)
    {
        if (_rendering)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
        }
        _window.EndWindowMotion(restoreWindow);
        if (restoreWindow)
        {
            _renderer.Reset();
        }
        SetPlaybackActive(false);
    }

    private void SetPlaybackActive(bool active)
    {
        if (_playbackActive == active)
        {
            return;
        }
        _playbackActive = active;
        PlaybackActiveChanged?.Invoke(this, active);
    }

    public void Dispose()
    {
        _timeline.Stop();
        StopRendering(restoreWindow: true);
    }
}
