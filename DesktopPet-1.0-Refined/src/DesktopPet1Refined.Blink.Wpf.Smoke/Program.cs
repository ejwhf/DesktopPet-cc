using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet1Refined.App;
using DesktopPet1Refined.App.NaturalMotion;
using DesktopPet1Refined.NaturalMotion.Runtime;

namespace DesktopPet1Refined.Blink.Wpf.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("First argument must be the absolute assets directory.");
        }

        var assetsRoot = Path.GetFullPath(args[0]);
        var triggerCount = ReadIntArgument(args, "--trigger-count", 100);
        var soakMinutes = ReadIntArgument(args, "--soak-minutes", 0);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow
        {
            ShowInTaskbar = false,
            Topmost = false,
            Width = 120,
            Height = 144,
            Left = 24,
            Top = 24,
            Title = "DesktopPet Blink WPF Smoke"
        };
        var assets = new AssetImageLoader(assetsRoot);
        var neutral = assets.LoadPose("sprites/idle.neutral.png", 512, 512);
        window.SetStaticFrame(neutral.Image, neutral.AlphaPixels);
        window.Show();
        Pump(TimeSpan.FromMilliseconds(100));

        var overlayElement = (Image?)window.FindName("BlinkOverlayImage")
            ?? throw new InvalidOperationException("BlinkOverlayImage was not found in MainWindow.");
        Require(overlayElement.Width == 512 && overlayElement.Height == 512,
            "runtime overlay is not a full-canvas 512x512 image");
        Require(overlayElement.Stretch == Stretch.Fill, "runtime overlay does not share the base Fill mapping");
        Require(overlayElement.Margin == new Thickness(0), "runtime overlay has a positional margin");
        Require(overlayElement.RenderTransform.Value.IsIdentity, "runtime overlay has a non-identity transform");

        var expectedFrames = new Dictionary<object, BlinkFrame>
        {
            [assets.LoadBitmap("natural-motion/rigs/idle/blink_40.png")] = BlinkFrame.Forty,
            [assets.LoadBitmap("natural-motion/rigs/idle/blink_75.png")] = BlinkFrame.SeventyFive,
            [assets.LoadBitmap("natural-motion/rigs/idle/blink_closed.png")] = BlinkFrame.Closed
        };
        var seenFrames = new HashSet<BlinkFrame>();
        var descriptor = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
        EventHandler sourceChanged = (_, _) =>
        {
            if (overlayElement.Source is not null && expectedFrames.TryGetValue(overlayElement.Source, out var frame))
            {
                seenFrames.Add(frame);
            }
        };
        descriptor.AddValueChanged(overlayElement, sourceChanged);

        var controller = new BlinkController(window, assets);
        controller.SetIdleAvailable(false);
        var gateChecks = new[] { "drag", "action", "hidden", "reduce-motion" };
        foreach (var gate in gateChecks)
        {
            var before = controller.CompletedBlinkCount;
            controller.Trigger();
            Pump(TimeSpan.FromMilliseconds(350));
            Require(controller.CompletedBlinkCount == before, $"{gate} gate allowed a blink");
            Require(overlayElement.Source is null, $"{gate} gate left an overlay visible");
        }

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var initialMemory = process.PrivateMemorySize64;
        var initialThreads = process.Threads.Count;
        controller.SetIdleAvailable(true);
        for (var index = 0; index < triggerCount; index++)
        {
            var expected = controller.CompletedBlinkCount + 1;
            controller.Trigger();
            PumpUntil(() => controller.CompletedBlinkCount >= expected, TimeSpan.FromSeconds(1));
            Require(controller.CompletedBlinkCount == expected, $"trigger {index + 1} did not complete exactly once");
            Require(overlayElement.Source is null && overlayElement.Visibility == Visibility.Collapsed,
                $"trigger {index + 1} did not restore OPEN");
        }
        Require(seenFrames.SetEquals(new[] { BlinkFrame.Forty, BlinkFrame.SeventyFive, BlinkFrame.Closed }),
            "the WPF render loop did not display all three blink overlays");

        var soakStarted = DateTimeOffset.Now;
        if (soakMinutes > 0)
        {
            var target = TimeSpan.FromMinutes(soakMinutes);
            var nextReport = TimeSpan.FromMinutes(1);
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < target)
            {
                Pump(TimeSpan.FromSeconds(1));
                if (stopwatch.Elapsed >= nextReport)
                {
                    process.Refresh();
                    Console.WriteLine(
                        $"SOAK {stopwatch.Elapsed.TotalMinutes:0}/{soakMinutes} min; blinks={controller.CompletedBlinkCount}; " +
                        $"privateMB={process.PrivateMemorySize64 / 1024d / 1024d:0.0}; threads={process.Threads.Count}");
                    nextReport += TimeSpan.FromMinutes(1);
                }
            }
        }

        controller.SetIdleAvailable(false);
        controller.Dispose();
        Pump(TimeSpan.FromMilliseconds(350));
        descriptor.RemoveValueChanged(overlayElement, sourceChanged);
        window.AllowClose();
        window.Close();
        application.Shutdown();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        var result = new
        {
            triggerCount,
            completedBlinks = controller.CompletedBlinkCount,
            gateChecks,
            framesSeen = seenFrames.OrderBy(frame => frame).Select(frame => frame.ToString()).ToArray(),
            overlayInvariant = "512x512; margin=0; identity transform; Stretch.Fill",
            soakMinutes,
            soakStarted,
            privateMemoryBeforeBytes = initialMemory,
            privateMemoryAfterBytes = process.PrivateMemorySize64,
            threadCountBefore = initialThreads,
            threadCountAfter = process.Threads.Count
        };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Require(process.PrivateMemorySize64 - initialMemory < 64 * 1024 * 1024,
            "private memory grew by 64 MiB or more");
        Require(process.Threads.Count <= initialThreads + 4, "thread count indicates a timer/thread leak");
        return 0;
    }

    private static int ReadIntArgument(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value)
            ? value
            : fallback;
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < timeout)
        {
            Pump(TimeSpan.FromMilliseconds(20));
        }
        Require(condition(), $"condition was not reached within {timeout}");
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
