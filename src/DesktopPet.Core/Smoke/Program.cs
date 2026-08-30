using DesktopPet.Core.Assets;
using DesktopPet.Core.Behavior;
using DesktopPet.Core.Timeline;

if (!PoseTransform.Identity.IsValid ||
    PoseTransform.Identity.Scale != 1 ||
    PoseTransform.Identity.Opacity != 1)
{
    throw new InvalidOperationException("PoseTransform.Identity must preserve scale and opacity.");
}

var directPlans = DesktopPet.Core.Animation.PlannedAnimationCatalog.CreateDefault();
if (directPlans.Count != 10 ||
    directPlans.Values.Any(plan =>
        plan.ClipId.EndsWith(".enter", StringComparison.Ordinal) ||
        plan.ClipId.EndsWith(".exit", StringComparison.Ordinal) ||
        plan.ClipId == "boot.enter" ||
        plan.ClipId == "landing.play") ||
    directPlans[PlannedBehaviorCatalog.Startup].ClipId != "idle.loop")
{
    throw new InvalidOperationException("v0.3 behavior plans must switch directly without transition clips.");
}

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: DesktopPet.Core.Smoke <assets.json> [animations.json]");
    return 2;
}

if (args.Length == 2)
{
    var animationManifest = await DesktopPet.Core.Animation.AnimationManifestValidator.LoadAsync(args[1]);
    if (animationManifest.FrameCount != 160 || animationManifest.Clips.Count != 20)
    {
        throw new InvalidOperationException("Expected 20 animation clips containing exactly 160 frames.");
    }
    foreach (var clip in animationManifest.Clips)
    {
        var player = new DesktopPet.Core.Animation.AnimationClipPlayer(clip);
        var first = player.Sample(0);
        var late = player.Sample(clip.TotalDurationMs + 10_000);
        if (first.FrameIndex != 0 || (!clip.Loop && !late.IsComplete))
        {
            throw new InvalidOperationException($"Clip sampling failed for '{clip.Id}'.");
        }
    }
}

var manifest = await AssetManifestValidator.LoadAsync(args[0], requireAllPlannedPoses: true);
if (manifest.Assets.Count != PoseIds.All.Count)
{
    throw new InvalidOperationException(
        $"Expected {PoseIds.All.Count} assets, found {manifest.Assets.Count}.");
}

var manifestIds = manifest.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
if (!manifestIds.SetEquals(PoseIds.All))
{
    throw new InvalidOperationException("Manifest pose ids differ from the production Core contract.");
}

var timelines = PlannedActionCatalog.CreateDefault();
var timelinePoseIds = timelines.Values
    .SelectMany(timeline => timeline.Cues)
    .Select(cue => cue.PoseId)
    .ToHashSet(StringComparer.Ordinal);
if (!timelinePoseIds.IsSubsetOf(manifestIds))
{
    throw new InvalidOperationException("A planned timeline references a pose missing from the manifest.");
}

const double maximumAnimationScaleDeviation = 0.005;
var excessiveScaleCue = timelines.Values
    .SelectMany(timeline => timeline.Cues.Select(cue => (timeline.Id, Cue: cue)))
    .FirstOrDefault(item =>
        Math.Abs(item.Cue.From.Scale - 1) > maximumAnimationScaleDeviation + 1e-9 ||
        Math.Abs(item.Cue.To.Scale - 1) > maximumAnimationScaleDeviation + 1e-9);
if (excessiveScaleCue.Cue is not null)
{
    throw new InvalidOperationException(
        $"Timeline '{excessiveScaleCue.Id}' pose '{excessiveScaleCue.Cue.PoseId}' " +
        "exceeds the 0.5% animation scale limit.");
}

if (manifest.Normalization?.MasterAssetId is not PoseIds.IdleNeutral)
{
    throw new InvalidOperationException("The production normalization master must be idle.neutral.");
}

var arbitration = PlannedBehaviorCatalog.CreateStateMachine(seed: 1);
var bootCompleted = arbitration.Advance(nowMs: 1_800);
if (!bootCompleted.Accepted || arbitration.CurrentStateId is not PlannedBehaviorCatalog.Idle)
{
    throw new InvalidOperationException("Behavior smoke setup could not leave startup.");
}

var directReaction = arbitration.RequestTransition(
    new TransitionRequest(PlannedBehaviorCatalog.Heart, TransitionCause.DirectInteraction),
    nowMs: 1_801);
if (!directReaction.Accepted)
{
    throw new InvalidOperationException("Direct reaction should interrupt scheduled idle.");
}

var dragOverride = arbitration.RequestTransition(
    new TransitionRequest(PlannedBehaviorCatalog.Dragging, TransitionCause.Drag),
    nowMs: 1_802);
if (!dragOverride.Accepted || arbitration.CurrentStateId is not PlannedBehaviorCatalog.Dragging)
{
    throw new InvalidOperationException("Drag must override a non-interruptible direct reaction.");
}

var menuOverride = arbitration.RequestTransition(
    new TransitionRequest(PlannedBehaviorCatalog.Menu, TransitionCause.Menu),
    nowMs: 1_803);
if (!menuOverride.Accepted || arbitration.CurrentStateId is not PlannedBehaviorCatalog.Menu)
{
    throw new InvalidOperationException("Menu must override Drag according to the priority hierarchy.");
}

Console.WriteLine(
    $"OK: loaded {manifest.Assets.Count} semantic poses; " +
    $"{timelinePoseIds.Count} are referenced by planned timelines; " +
    $"scale motion <= 0.5%; direct clip switching; " +
    $"{(args.Length == 2 ? "20 archived clips / 160 frames; " : string.Empty)}" +
    "priority arbitration passed.");
return 0;
