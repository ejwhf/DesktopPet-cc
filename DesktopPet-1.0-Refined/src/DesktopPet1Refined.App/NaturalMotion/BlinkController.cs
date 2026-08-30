using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet1Refined.NaturalMotion.Runtime;

namespace DesktopPet1Refined.App.NaturalMotion;

internal sealed class BlinkController : IDisposable
{
    private const int MinimumIntervalMs = 3000;
    private const int MaximumIntervalMs = 7000;
    private readonly MainWindow _window;
    private readonly IReadOnlyDictionary<BlinkFrame, BitmapSource?> _frames;
    private readonly BlinkTimeline _timeline = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Threading.Timer _waitTimer;
    private bool _idleAvailable = true;
    private bool _rendering;
    private bool _disposed;

    public BlinkController(MainWindow window, AssetImageLoader assets)
    {
        _window = window;
        _frames = new Dictionary<BlinkFrame, BitmapSource?>
        {
            [BlinkFrame.Open] = null,
            [BlinkFrame.Forty] = LoadOverlay(assets, "natural-motion/rigs/idle/blink_40.png"),
            [BlinkFrame.SeventyFive] = LoadOverlay(assets, "natural-motion/rigs/idle/blink_75.png"),
            [BlinkFrame.Closed] = LoadOverlay(assets, "natural-motion/rigs/idle/blink_closed.png")
        };
        _waitTimer = new System.Threading.Timer(OnWaitElapsed, null, Timeout.Infinite, Timeout.Infinite);
        ScheduleNext();
    }

    internal int CompletedBlinkCount { get; private set; }

    public void SetIdleAvailable(bool available)
    {
        if (_disposed || _idleAvailable == available)
        {
            return;
        }
        _idleAvailable = available;
        if (available)
        {
            ScheduleNext();
        }
        else
        {
            InterruptAndClear();
        }
    }

    public void Trigger()
    {
        if (_disposed || !_idleAvailable || _timeline.Phase != BlinkPhase.Idle)
        {
            return;
        }
        _waitTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _timeline.Start(_clock.Elapsed);
        AttachRendering();
    }

    private static BitmapSource LoadOverlay(AssetImageLoader assets, string path)
    {
        var image = assets.LoadBitmap(path);
        if (image.PixelWidth != 512 || image.PixelHeight != 512)
        {
            throw new InvalidDataException($"Blink overlay '{path}' must be 512x512.");
        }
        return image;
    }

    private void OnWaitElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }
        _window.Dispatcher.BeginInvoke(new Action(Trigger));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_idleAvailable)
        {
            InterruptAndClear();
            return;
        }
        var sample = _timeline.Tick(_clock.Elapsed);
        _window.SetBlinkOverlay(_frames[sample.Frame]);
        if (!sample.IsComplete)
        {
            return;
        }
        CompletedBlinkCount++;
        StopRendering();
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        if (_disposed || !_idleAvailable)
        {
            return;
        }
        _window.SetBlinkOverlay(null);
        var delay = Random.Shared.Next(MinimumIntervalMs, MaximumIntervalMs + 1);
        _waitTimer.Change(delay, Timeout.Infinite);
    }

    private void InterruptAndClear()
    {
        _waitTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _timeline.Stop();
        StopRendering();
        _window.SetBlinkOverlay(null);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _waitTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _waitTimer.Dispose();
        _timeline.Stop();
        StopRendering();
        _window.SetBlinkOverlay(null);
    }
}
