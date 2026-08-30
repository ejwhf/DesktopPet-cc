using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App.Views;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Assets;
using DesktopPet.Core.Behavior;
using DesktopPet.Core.Timeline;

namespace DesktopPet.App.Services;

/// <summary>Runs v0.3 direct action clips with a v0.1 pose fallback.</summary>
public sealed class PetRuntimeController : IDisposable
{
    public const string AutomaticDebugSelection = "auto";
    private const int MaximumCachedFrames = 24;

    private sealed record CachedFrame(BitmapImage Image, byte[] AlphaPixels);

    private readonly MainWindow _window;
    private readonly IReadOnlyDictionary<string, PetAssetDefinition> _assets;
    private readonly IReadOnlyDictionary<string, string> _imagePaths;
    private readonly IReadOnlyDictionary<string, ActionTimeline> _timelines;
    private readonly IReadOnlyDictionary<string, AnimationClip> _animationClips;
    private readonly IReadOnlyDictionary<string, string> _animationFramePaths;
    private readonly IReadOnlyDictionary<string, BehaviorAnimationPlan> _animationPlans;
    private readonly Dictionary<string, CachedFrame> _frameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _frameLru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _frameLruNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _frameTimer;
    private WeightedBehaviorStateMachine _stateMachine;
    private TimelinePlayer _timelinePlayer;
    private AnimationClipPlayer? _clipPlayer;
    private string? _debugPoseId;
    private string? _debugClipId;
    private long _nextScheduledAdvanceMs;
    private bool _disposed;
    private bool _reduceMotion;
    private double _actionFrequencyMultiplier = 1;

    private bool HasFrameAnimations => _animationClips.Count > 0;

    private PetRuntimeController(
        MainWindow window,
        AssetManifest poseManifest,
        IReadOnlyDictionary<string, string> posePaths,
        AnimationManifest? animationManifest,
        IReadOnlyDictionary<string, string>? animationFramePaths,
        ulong seed)
    {
        _window = window;
        _imagePaths = posePaths;
        _assets = poseManifest.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        _timelines = PlannedActionCatalog.CreateDefault();
        _animationClips = animationManifest?.Clips.ToDictionary(clip => clip.Id, StringComparer.Ordinal)
            ?? new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        _animationFramePaths = animationFramePaths
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _animationPlans = PlannedAnimationCatalog.CreateDefault();
        _stateMachine = PlannedBehaviorCatalog.CreateStateMachine(seed);
        _timelinePlayer = new TimelinePlayer(_timelines[PlannedActionCatalog.Startup]);
        _nextScheduledAdvanceMs = long.MaxValue;

        _window.SetAlphaHitThreshold(poseManifest.HitMaskAlphaThreshold ?? 16);
        _window.DebugActionRequested += OnDebugActionRequested;
        _window.PetDragStarted += OnPetDragStarted;
        _window.PetDragCompleted += OnPetDragCompleted;
        _window.PetClicked += OnPetClicked;

        _frameTimer = new DispatcherTimer(DispatcherPriority.Render, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1000d / 30d)
        };
        _frameTimer.Tick += OnFrame;
    }

    public static async Task<PetRuntimeController> CreateAsync(
        MainWindow window,
        string? assetRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        assetRoot = Path.GetFullPath(assetRoot ?? Path.Combine(AppContext.BaseDirectory, "assets"));
        var poseManifest = await AssetManifestValidator.LoadAsync(
            Path.Combine(assetRoot, "manifest", "assets.json"),
            requireAllPlannedPoses: true,
            cancellationToken).ConfigureAwait(true);

        var posePaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in poseManifest.Assets)
        {
            var path = ResolveContainedAssetPath(assetRoot, asset.File);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Desktop-pet pose image was not found.", path);
            }
            posePaths.Add(asset.Id, path);
        }

        AnimationManifest? animationManifest = null;
        Dictionary<string, string>? framePaths = null;
        var animationManifestPath = Path.Combine(assetRoot, "manifest", "animations.json");
        if (File.Exists(animationManifestPath))
        {
            try
            {
                animationManifest = await AnimationManifestValidator.LoadAsync(
                    animationManifestPath, cancellationToken).ConfigureAwait(true);
                framePaths = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var clip in animationManifest.Clips)
                {
                    for (var index = 0; index < clip.Frames.Count; index++)
                    {
                        var key = FrameKey(clip.Id, index);
                        var path = ResolveContainedAssetPath(assetRoot, clip.Frames[index].File);
                        if (!File.Exists(path))
                        {
                            throw new FileNotFoundException("Animation frame was not found.", path);
                        }
                        framePaths.Add(key, path);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or AnimationManifestValidationException)
            {
                Trace.WriteLine($"Frame animation disabled; using pose fallback: {exception}");
                animationManifest = null;
                framePaths = null;
            }
        }

        return new PetRuntimeController(
            window, poseManifest, posePaths, animationManifest, framePaths,
            seed: unchecked((ulong)Environment.TickCount64));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var now = _clock.ElapsedMilliseconds;
        if (HasFrameAnimations)
        {
            StartCurrentStateAnimation(now);
        }
        else
        {
            RenderSample(_timelinePlayer.Sample(now));
        }
        _frameTimer.Start();
    }

    public void ApplyBehaviorSettings(double actionFrequencyMultiplier, bool reduceMotion)
    {
        _actionFrequencyMultiplier = Math.Clamp(actionFrequencyMultiplier, 0.25, 4);
        var changed = _reduceMotion != reduceMotion;
        _reduceMotion = reduceMotion;
        _frameTimer.Interval = TimeSpan.FromMilliseconds(reduceMotion ? 1000d / 12d : 1000d / 30d);
        if (changed && HasFrameAnimations)
        {
            StartCurrentStateAnimation(_clock.ElapsedMilliseconds);
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_debugPoseId is not null)
        {
            RenderPose(_debugPoseId, PoseTransform.Identity);
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        if (_debugClipId is not null && _clipPlayer is not null)
        {
            RenderAnimationSample(_clipPlayer.Sample(now));
            return;
        }
        if (HasFrameAnimations)
        {
            if (_reduceMotion)
            {
                RenderReducedMotionState(now);
            }
            else
            {
                OnAnimationFrame(now);
            }
            return;
        }
        OnLegacyFrame(now);
    }

    private void OnAnimationFrame(long now)
    {
        if (_clipPlayer is null)
        {
            StartCurrentStateAnimation(now);
            return;
        }

        var sample = _clipPlayer.Sample(now);
        RenderAnimationSample(sample);
        if (sample.IsComplete)
        {
            if (now >= _nextScheduledAdvanceMs)
            {
                AdvanceAnimation(now);
            }
        }
        else if (_clipPlayer.Clip.Loop && now >= _nextScheduledAdvanceMs)
        {
            AdvanceAnimation(now);
        }
    }

    private void OnLegacyFrame(long now)
    {
        var sample = _timelinePlayer.Sample(now);
        RenderSample(sample);
        if (sample.IsComplete || (_timelinePlayer.Timeline.Loop && now >= _nextScheduledAdvanceMs))
        {
            AdvanceLegacy(now);
        }
    }

    private void RenderReducedMotionState(long now)
    {
        RenderPose(GetCurrentFallbackPose(), PoseTransform.Identity);
        if (now >= _nextScheduledAdvanceMs)
        {
            AdvanceAnimation(now);
        }
    }

    private void AdvanceAnimation(long now)
    {
        var decision = _stateMachine.Advance(now);
        if (decision.Accepted)
        {
            StartCurrentStateAnimation(now);
        }
        else
        {
            _nextScheduledAdvanceMs = now + 1_000;
        }
    }

    private void AdvanceLegacy(long now)
    {
        var decision = _stateMachine.Advance(now);
        if (decision.Accepted)
        {
            StartCurrentStateTimeline(now);
        }
        else
        {
            _nextScheduledAdvanceMs = now + 1_000;
        }
    }

    private void StartCurrentStateAnimation(long now)
    {
        if (!_animationPlans.TryGetValue(_stateMachine.CurrentStateId, out var plan))
        {
            plan = _animationPlans[PlannedBehaviorCatalog.Idle];
        }
        _nextScheduledAdvanceMs = now + GetAnimationDwellDurationMs(_stateMachine.Current);
        if (_reduceMotion)
        {
            _clipPlayer = null;
            RenderPose(GetCurrentFallbackPose(), PoseTransform.Identity);
        }
        else if (!_animationClips.TryGetValue(plan.ClipId, out var clip))
        {
            _clipPlayer = null;
        }
        else
        {
            _clipPlayer = new AnimationClipPlayer(clip, now);
            RenderAnimationSample(_clipPlayer.Sample(now));
        }
    }

    private void StartCurrentStateTimeline(long now)
    {
        if (!_timelines.TryGetValue(_stateMachine.CurrentStateId, out var timeline))
        {
            timeline = _timelines[PlannedActionCatalog.Idle];
        }
        _timelinePlayer = new TimelinePlayer(timeline, now);
        _nextScheduledAdvanceMs = timeline.Loop
            ? now + GetLegacyDwellDurationMs(_stateMachine.Current, timeline)
            : now + _stateMachine.Current.MinimumDurationMs;
        RenderSample(_timelinePlayer.Sample(now));
    }

    private void RequestBehavior(string stateId, TransitionCause cause, bool restartIfCurrent = true)
    {
        _debugPoseId = null;
        _debugClipId = null;
        var now = _clock.ElapsedMilliseconds;
        var decision = _stateMachine.RequestTransition(
            new TransitionRequest(stateId, cause, RestartIfCurrent: restartIfCurrent), now);
        if (!decision.Accepted)
        {
            return;
        }
        if (HasFrameAnimations)
        {
            StartCurrentStateAnimation(now);
        }
        else
        {
            StartCurrentStateTimeline(now);
        }
    }

    private void OnDebugActionRequested(object? sender, string id)
    {
        if (string.Equals(id, AutomaticDebugSelection, StringComparison.Ordinal))
        {
            _debugPoseId = null;
            _debugClipId = null;
            if (HasFrameAnimations)
            {
                StartCurrentStateAnimation(_clock.ElapsedMilliseconds);
            }
            else
            {
                StartCurrentStateTimeline(_clock.ElapsedMilliseconds);
            }
            return;
        }
        if (HasFrameAnimations && _animationClips.TryGetValue(id, out var clip))
        {
            _debugPoseId = null;
            _debugClipId = id;
            _clipPlayer = new AnimationClipPlayer(clip, _clock.ElapsedMilliseconds);
            return;
        }
        if (_imagePaths.ContainsKey(id))
        {
            _debugClipId = null;
            _debugPoseId = id;
            RenderPose(id, PoseTransform.Identity);
        }
    }

    public void SelectDebugPose(string poseId) => OnDebugActionRequested(this, poseId);

    private void OnPetDragStarted(object? sender, EventArgs e) =>
        RequestBehavior(PlannedActionCatalog.Dragging, TransitionCause.Drag);

    private void OnPetDragCompleted(object? sender, EventArgs e) =>
        RequestBehavior(PlannedActionCatalog.Falling, TransitionCause.Drag);

    private void OnPetClicked(object? sender, EventArgs e)
    {
        var target = (_clock.ElapsedMilliseconds / 1_000) % 2 == 0
            ? PlannedActionCatalog.Heart
            : PlannedActionCatalog.Waving;
        RequestBehavior(target, TransitionCause.DirectInteraction);
    }

    private void RenderSample(TimelineSample sample) => RenderPose(sample.PoseId, sample.Transform);

    private void RenderAnimationSample(AnimationFrameSample sample)
    {
        var key = FrameKey(sample.ClipId, sample.FrameIndex);
        if (!_animationFramePaths.TryGetValue(key, out var path))
        {
            RenderPose(sample.Frame.SourcePoseId, PoseTransform.Identity);
            return;
        }
        var cached = GetOrLoadFrame(path);
        _window.ApplyAnimationFrame(
            cached.Image, cached.AlphaPixels, GetLoopTransform(sample),
            sample.Frame.Anchor.X, sample.Frame.Anchor.Y);
    }

    private static PoseTransform GetLoopTransform(AnimationFrameSample sample)
    {
        if (!sample.ClipId.EndsWith(".loop", StringComparison.Ordinal) || sample.ClipId == "drag.loop")
        {
            return PoseTransform.Identity;
        }
        var amplitude = sample.ClipId == "read.loop" ? 0.003 : 0.005;
        var progress = sample.ClipElapsedMs % 2_000 / 2_000d;
        return new PoseTransform(Scale: 1 + (Math.Sin(progress * Math.PI * 2) * amplitude));
    }

    private void RenderPose(string poseId, PoseTransform transform)
    {
        if (!_imagePaths.TryGetValue(poseId, out var path) || !_assets.TryGetValue(poseId, out var asset))
        {
            return;
        }
        var cached = GetOrLoadFrame(path);
        var anchor = asset.GroundAnchor ?? asset.FloorAnchor ?? asset.SeatAnchor ?? asset.Pivot;
        _window.ApplyAnimationFrame(
            cached.Image, cached.AlphaPixels, _reduceMotion ? PoseTransform.Identity : transform,
            anchor.X, anchor.Y);
    }

    private CachedFrame GetOrLoadFrame(string path)
    {
        if (_frameCache.TryGetValue(path, out var cached))
        {
            TouchFrame(path);
            return cached;
        }
        var bitmap = LoadBitmap(path);
        cached = new CachedFrame(bitmap, CopyAlphaPlane(bitmap));
        _frameCache.Add(path, cached);
        _frameLruNodes.Add(path, _frameLru.AddLast(path));
        while (_frameCache.Count > MaximumCachedFrames && _frameLru.First is { } oldest)
        {
            _frameLru.RemoveFirst();
            _frameLruNodes.Remove(oldest.Value);
            _frameCache.Remove(oldest.Value);
        }
        return cached;
    }

    private void TouchFrame(string path)
    {
        if (!_frameLruNodes.TryGetValue(path, out var node) || node == _frameLru.Last)
        {
            return;
        }
        _frameLru.Remove(node);
        _frameLru.AddLast(node);
    }

    private string GetCurrentFallbackPose()
    {
        if (_animationPlans.TryGetValue(_stateMachine.CurrentStateId, out var plan))
        {
            if (_animationClips.TryGetValue(plan.ClipId, out var clip))
            {
                return clip.FallbackPoseId;
            }
        }
        return PoseIds.IdleNeutral;
    }

    private long GetAnimationDwellDurationMs(BehaviorStateDefinition state)
    {
        var additional = state.Mode switch
        {
            PetMode.Idle => 5_000,
            PetMode.Sitting => 4_000,
            PetMode.Reading => 5_000,
            PetMode.Sleeping => 7_000,
            PetMode.Writing => 5_000,
            _ => 0
        };
        return Math.Max(state.MinimumDurationMs, 1) + (long)(additional / _actionFrequencyMultiplier);
    }

    private long GetLegacyDwellDurationMs(BehaviorStateDefinition state, ActionTimeline timeline)
    {
        var baseDuration = Math.Max(state.MinimumDurationMs, timeline.TotalDurationMs);
        return baseDuration + GetAnimationDwellDurationMs(state) - Math.Max(state.MinimumDurationMs, 1);
    }

    private static string FrameKey(string clipId, int frameIndex) => $"{clipId}:{frameIndex}";

    private static string ResolveContainedAssetPath(string assetRoot, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(assetRoot));
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Asset path escapes the asset root: '{relativePath}'.");
        }
        return resolved;
    }

    private static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] CopyAlphaPlane(BitmapSource bitmap)
    {
        var converted = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var bgra = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(bgra, stride, 0);
        var alpha = new byte[converted.PixelWidth * converted.PixelHeight];
        for (var index = 0; index < alpha.Length; index++)
        {
            alpha[index] = bgra[(index * 4) + 3];
        }
        return alpha;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _frameTimer.Stop();
        _frameTimer.Tick -= OnFrame;
        _window.DebugActionRequested -= OnDebugActionRequested;
        _window.PetDragStarted -= OnPetDragStarted;
        _window.PetDragCompleted -= OnPetDragCompleted;
        _window.PetClicked -= OnPetClicked;
        _frameCache.Clear();
        _frameLru.Clear();
        _frameLruNodes.Clear();
    }
}
