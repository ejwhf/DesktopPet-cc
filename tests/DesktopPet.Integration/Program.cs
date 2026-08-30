using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DesktopPet.Integration;

internal static class Program
{
    private const string StablePoseId = "idle.neutral";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    [STAThread]
    private static int Main(string[] args)
    {
        Process? process = null;
        try
        {
            var options = Options.Parse(args);
            Console.WriteLine("DesktopPet Windows native integration test");
            Console.WriteLine($"  executable : {options.ExecutablePath}");
            Console.WriteLine($"  manifest   : {options.ManifestPath}");
            Console.WriteLine($"  timeout    : {options.Timeout.TotalSeconds:F1}s");

            var manifest = AssetManifest.Load(options.ManifestPath);
            var asset = manifest.Assets.SingleOrDefault(
                candidate => string.Equals(candidate.Id, StablePoseId, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Manifest is missing required pose '{StablePoseId}'.");
            var assetRoot = Directory.GetParent(Path.GetDirectoryName(options.ManifestPath)!)?.FullName
                ?? throw new InvalidDataException("Manifest must be inside an asset manifest directory.");
            var maskPath = ResolveContained(assetRoot, asset.HitMask);
            var mask = PngMaskReader.Load(maskPath);
            var threshold = checked((byte)Math.Clamp(manifest.HitMaskAlphaThreshold, 1, byte.MaxValue));
            var opaque = mask.FindInteriorOpaquePoint(threshold);
            Console.WriteLine(
                $"  hit mask    : {mask.Width}x{mask.Height}, threshold {threshold}, " +
                $"opaque source ({opaque.X},{opaque.Y}) alpha {opaque.Value}");

            EnsureNoExistingInstance(options.ExecutablePath);
            var startInfo = new ProcessStartInfo(options.ExecutablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(options.ExecutablePath)!
            };
            startInfo.Environment["DESKTOPPET_DIAGNOSTIC_POSE"] = StablePoseId;
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");
            Console.WriteLine($"[start] PID {process.Id}");

            var window = WaitForMainWindow(process, options.Timeout);
            ValidateWindow(process, window);
            ValidateHitTesting(window, mask, opaque, options.Timeout);

            Console.WriteLine("PASS: native window and per-pixel WM_NCHITTEST behavior are correct.");
            return 0;
        }
        catch (OptionsRequestedException exception)
        {
            Console.WriteLine(exception.Message);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception.Message}");
            if (exception is Win32Exception win32)
            {
                Console.Error.WriteLine($"  Win32 error: {win32.NativeErrorCode}");
            }

            Console.Error.WriteLine(exception.StackTrace);
            return 1;
        }
        finally
        {
            StopProcess(process);
        }
    }

    private static nint WaitForMainWindow(Process process, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        nint lastWindow = nint.Zero;
        while (deadline.Elapsed < timeout)
        {
            process.Refresh();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"DesktopPet exited before creating a window (exit code {process.ExitCode}). " +
                    "Another instance may already own the single-instance mutex.");
            }

            NativeMethods.EnumWindows((window, ignoredParameter) =>
            {
                NativeMethods.GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId == process.Id && NativeMethods.IsWindowVisible(window))
                {
                    lastWindow = window;
                    return false;
                }

                return true;
            }, nint.Zero);

            if (lastWindow != nint.Zero &&
                NativeMethods.GetClientRect(lastWindow, out var client) &&
                client.Width > 0 && client.Height > 0)
            {
                Console.WriteLine($"[window] HWND 0x{lastWindow:X}, client {client.Width}x{client.Height}");
                return lastWindow;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"No visible main window appeared within {timeout.TotalSeconds:F1}s.");
    }

    private static void ValidateWindow(Process process, nint window)
    {
        Assert(NativeMethods.IsWindowVisible(window), "Main window must be visible.");
        _ = NativeMethods.GetWindowThreadProcessId(window, out var ownerProcessId);
        Assert(ownerProcessId == process.Id, "Window belongs to an unexpected process.");
        Assert(NativeMethods.GetWindowRect(window, out var bounds), "GetWindowRect failed.");
        Assert(bounds.Width > 0 && bounds.Height > 0, $"Window has invalid bounds {bounds}.");

        var style = NativeMethods.GetWindowLong(window, NativeMethods.GwlStyle);
        var extendedStyle = NativeMethods.GetWindowLong(window, NativeMethods.GwlExStyle);
        Console.WriteLine($"[style] WS=0x{style:X8}, WS_EX=0x{extendedStyle:X8}, bounds {bounds}");
        Assert((style & NativeMethods.WsVisible) != 0, "WS_VISIBLE is missing.");
        Assert((style & NativeMethods.WsCaption) == 0, "Borderless window unexpectedly has WS_CAPTION.");
        Assert((style & NativeMethods.WsThickFrame) == 0, "Non-resizable window unexpectedly has WS_THICKFRAME.");
        Assert((extendedStyle & NativeMethods.WsExLayered) != 0, "Transparent WPF window must have WS_EX_LAYERED.");
        Assert((extendedStyle & NativeMethods.WsExTopMost) != 0, "Topmost desktop pet must have WS_EX_TOPMOST.");
        Assert((extendedStyle & NativeMethods.WsExToolWindow) != 0, "Taskbar-hidden window must have WS_EX_TOOLWINDOW.");
        Assert((extendedStyle & NativeMethods.WsExTransparent) == 0,
            "Default character-pixel mode must not set whole-window WS_EX_TRANSPARENT.");
    }

    private static void ValidateHitTesting(
        nint window,
        PngMaskReader mask,
        (int X, int Y, byte Value) opaqueSource,
        TimeSpan timeout)
    {
        Assert(NativeMethods.GetClientRect(window, out var client), "GetClientRect failed.");
        var origin = new NativeMethods.Point(0, 0);
        Assert(NativeMethods.ClientToScreen(window, ref origin), "ClientToScreen failed.");

        var transparentClient = new NativeMethods.Point(1, 1);
        var opaqueClient = MapSourceToClient(
            client.Width,
            client.Height,
            mask.Width,
            mask.Height,
            opaqueSource.X,
            opaqueSource.Y);

        var deadline = Stopwatch.StartNew();
        long transparentResult = long.MinValue;
        long opaqueResult = NativeMethods.HtTransparent;
        while (deadline.Elapsed < timeout)
        {
            origin = new NativeMethods.Point(0, 0);
            Assert(NativeMethods.ClientToScreen(window, ref origin), "ClientToScreen failed.");
            transparentResult = NativeMethods.NcHitTest(
                window,
                new NativeMethods.Point(origin.X + transparentClient.X, origin.Y + transparentClient.Y));
            opaqueResult = NativeMethods.NcHitTest(
                window,
                new NativeMethods.Point(origin.X + opaqueClient.X, origin.Y + opaqueClient.Y));
            if (transparentResult == NativeMethods.HtTransparent && opaqueResult != NativeMethods.HtTransparent)
            {
                break;
            }

            Thread.Sleep(100);
        }

        Console.WriteLine(
            $"[hit-test] transparent client ({transparentClient.X},{transparentClient.Y}) => " +
            $"{HitName(transparentResult)} ({transparentResult})");
        Console.WriteLine(
            $"[hit-test] character client ({opaqueClient.X},{opaqueClient.Y}) " +
            $"from source ({opaqueSource.X},{opaqueSource.Y}) => {HitName(opaqueResult)} ({opaqueResult})");

        Assert(transparentResult == NativeMethods.HtTransparent,
            $"Transparent client point returned {HitName(transparentResult)}, expected HTTRANSPARENT.");
        Assert(opaqueResult != NativeMethods.HtTransparent,
            "Opaque character point unexpectedly returned HTTRANSPARENT.");
    }

    private static NativeMethods.Point MapSourceToClient(
        int clientWidth,
        int clientHeight,
        int imageWidth,
        int imageHeight,
        int sourceX,
        int sourceY)
    {
        // MainWindow PetImage has Margin=12 and Stretch=Uniform.
        const double margin = 12;
        var controlWidth = clientWidth - (2 * margin);
        var controlHeight = clientHeight - (2 * margin);
        Assert(controlWidth > 0 && controlHeight > 0, "Client is too small for the image margin.");
        var imageRatio = imageWidth / (double)imageHeight;
        var controlRatio = controlWidth / controlHeight;
        var renderedWidth = controlRatio > imageRatio ? controlHeight * imageRatio : controlWidth;
        var renderedHeight = controlRatio > imageRatio ? controlHeight : controlWidth / imageRatio;
        var renderLeft = margin + ((controlWidth - renderedWidth) / 2);
        var renderTop = margin + ((controlHeight - renderedHeight) / 2);
        return new NativeMethods.Point(
            checked((int)Math.Floor(renderLeft + ((sourceX + 0.5) / imageWidth * renderedWidth))),
            checked((int)Math.Floor(renderTop + ((sourceY + 0.5) / imageHeight * renderedHeight))));
    }

    private static void EnsureNoExistingInstance(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var currentProcessId = Environment.ProcessId;
        var existing = Process.GetProcessesByName(processName)
            .Where(process => process.Id != currentProcessId)
            .ToArray();
        try
        {
            if (existing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Found {existing.Length} existing '{processName}' process(es): " +
                    string.Join(", ", existing.Select(process => process.Id)) +
                    ". Exit the running desktop pet before integration testing.");
            }
        }
        finally
        {
            foreach (var process in existing)
            {
                process.Dispose();
            }
        }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.Refresh();
            if (!process.HasExited)
            {
                Console.WriteLine($"[cleanup] terminating PID {process.Id}");
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5_000))
                {
                    Console.Error.WriteLine($"[cleanup] PID {process.Id} did not exit within 5s.");
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"[cleanup] could not terminate PID {process.Id}: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string ResolveContained(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Manifest path escapes the asset root: '{relativePath}'.");
        }

        return resolved;
    }

    private static string HitName(long result) => result switch
    {
        NativeMethods.HtTransparent => "HTTRANSPARENT",
        NativeMethods.HtClient => "HTCLIENT",
        0 => "HTNOWHERE",
        _ => "HT_" + result.ToString(CultureInfo.InvariantCulture)
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Options(string ExecutablePath, string ManifestPath, TimeSpan Timeout)
    {
        internal static Options Parse(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
            {
                throw new OptionsRequestedException(
                    "Usage: DesktopPet.Integration --exe <DesktopPet.exe> --manifest <assets.json> " +
                    "[--timeout-seconds 15]\n" +
                    "Both --exe and --manifest are required; paths are resolved to absolute paths.");
            }

            string? executable = null;
            string? manifest = null;
            var timeout = DefaultTimeout;
            for (var index = 0; index < args.Length; index++)
            {
                var option = args[index];
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value after '{option}'.");
                }

                var value = args[++index];
                switch (option.ToLowerInvariant())
                {
                    case "--exe":
                        executable = Path.GetFullPath(value);
                        break;
                    case "--manifest":
                        manifest = Path.GetFullPath(value);
                        break;
                    case "--timeout-seconds":
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                            seconds is < 1 or > 120)
                        {
                            throw new ArgumentException("Timeout must be between 1 and 120 seconds.");
                        }

                        timeout = TimeSpan.FromSeconds(seconds);
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{option}'.");
                }
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(executable);
            ArgumentException.ThrowIfNullOrWhiteSpace(manifest);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("DesktopPet executable was not found.", executable);
            }

            if (!File.Exists(manifest))
            {
                throw new FileNotFoundException("Asset manifest was not found.", manifest);
            }

            return new Options(executable, manifest, timeout);
        }
    }

    private sealed class OptionsRequestedException(string message) : Exception(message);
}
