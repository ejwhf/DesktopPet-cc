using DesktopPet1Refined.NaturalMotion.Catalog;
using DesktopPet1Refined.NaturalMotion.Models;
using DesktopPet1Refined.NaturalMotion.Runtime;
using DesktopPet1Refined.NaturalMotion.Validation;

var motion = new MotionDefinition
{
    Id = "engine-smoke",
    BasePose = "06_idle",
    DurationMs = 1000,
    LoopMode = MotionLoopMode.Once,
    Targets =
    [
        new MotionTargetDefinition
        {
            Id = "probe-layer",
            Kind = MotionTargetKind.Layer,
            Source = "natural-motion/rigs/probe/layer.png",
            Bounds = new MotionRect(0, 0, 64, 64),
            Pivot = new MotionPoint(32, 32)
        }
    ],
    Tracks =
    [
        new MotionTrack
        {
            Target = "probe-layer",
            Property = MotionProperty.Rotate,
            Keyframes =
            [
                new MotionKeyframe { TimeMs = 0, Value = 0 },
                new MotionKeyframe { TimeMs = 1000, Value = 10 }
            ]
        }
    ]
};

Require(NaturalMotionValidator.Validate(motion).Count == 0, "valid layer motion rejected");
var midpoint = MotionSampler.Sample(motion, 500);
Require(Math.Abs(midpoint.Values[new MotionValueKey("probe-layer", MotionProperty.Rotate)] - 5) < 0.001,
    "timeline interpolation is not time based");
Require(MotionSampler.Sample(motion, 1000).IsComplete, "one-shot motion did not complete");

var player = new MotionTimelinePlayer { Speed = 0.5 };
player.Start(motion, TimeSpan.Zero);
var slowed = player.Tick(TimeSpan.FromMilliseconds(1000));
Require(Math.Abs(slowed.TimeMs - 500) < 0.001, "playback speed was not applied");
player.Pause(TimeSpan.FromMilliseconds(1000));
var paused = player.GetSample(TimeSpan.FromMilliseconds(5000));
Require(Math.Abs(paused.TimeMs - 500) < 0.001, "paused playback continued advancing");

var loopMotion = motion with
{
    LoopMode = MotionLoopMode.Loop,
    InterruptPolicy = MotionInterruptPolicy.FinishCurrentLoop
};
player.Speed = 1;
player.Start(loopMotion, TimeSpan.Zero);
_ = player.Tick(TimeSpan.FromMilliseconds(250));
Require(!player.RequestInterrupt(TimeSpan.FromMilliseconds(250)), "finish-loop interrupt stopped immediately");
_ = player.Tick(TimeSpan.FromMilliseconds(1001));
Require(player.State == MotionPlaybackState.Stopped, "finish-loop interrupt did not stop at the boundary");

var invalidWindowMotion = motion with
{
    Targets = [new MotionTargetDefinition { Id = "window", Kind = MotionTargetKind.Window }],
    Tracks =
    [
        new MotionTrack
        {
            Target = "window",
            Property = MotionProperty.Rotate,
            Keyframes = [new MotionKeyframe { TimeMs = 0, Value = 0 }]
        }
    ]
};
Require(NaturalMotionValidator.Validate(invalidWindowMotion).Count > 0,
    "window rotation should be rejected; Window Motion is position-only");

var blink = new BlinkTimeline();
for (var iteration = 0; iteration < 1000; iteration++)
{
    blink.Start(TimeSpan.Zero);
    Require(blink.Tick(TimeSpan.Zero).Frame == BlinkFrame.Open, "blink must start from OPEN");
    Require(blink.Tick(TimeSpan.FromMilliseconds(20)).Frame == BlinkFrame.Forty,
        "closing did not enter the 40% frame");
    Require(blink.Tick(TimeSpan.FromMilliseconds(60)).Frame == BlinkFrame.SeventyFive,
        "closing did not enter the 75% frame");
    Require(blink.Tick(TimeSpan.FromMilliseconds(90)).Frame == BlinkFrame.Closed,
        "closing did not reach CLOSED");
    var held = blink.Tick(TimeSpan.FromMilliseconds(125));
    Require(held.Phase == BlinkPhase.Closed && held.Frame == BlinkFrame.Closed,
        "50 ms CLOSED hold was not preserved");
    Require(blink.Tick(TimeSpan.FromMilliseconds(180)).Frame == BlinkFrame.SeventyFive,
        "opening did not reverse through the 75% frame");
    Require(blink.Tick(TimeSpan.FromMilliseconds(240)).Frame == BlinkFrame.Forty,
        "opening did not reverse through the 40% frame");
    Require(blink.Tick(TimeSpan.FromMilliseconds(285)).Frame == BlinkFrame.Open,
        "opening did not return to OPEN before completion");
    var completed = blink.Tick(TimeSpan.FromMilliseconds(300));
    Require(completed.IsComplete && completed.Frame == BlinkFrame.Open && blink.Phase == BlinkPhase.Idle,
        "blink did not complete at 300 ms");
}

blink.Start(TimeSpan.Zero);
_ = blink.Tick(TimeSpan.FromMilliseconds(60));
blink.Pause(TimeSpan.FromMilliseconds(60));
var pausedBlink = blink.Tick(TimeSpan.FromSeconds(5));
Require(pausedBlink.Phase == BlinkPhase.Closing && pausedBlink.Frame == BlinkFrame.SeventyFive,
    "paused blink continued advancing");
blink.Resume(TimeSpan.FromSeconds(5));
Require(blink.Tick(TimeSpan.FromMilliseconds(5180)).Frame == BlinkFrame.Forty,
    "resumed blink did not preserve its paused offset");
blink.Stop();

foreach (var speed in new[] { 0.25, 0.5, 1.0 })
{
    blink.Speed = speed;
    blink.Start(TimeSpan.Zero);
    Require(!blink.Tick(TimeSpan.FromMilliseconds(299 / speed)).IsComplete,
        $"blink completed too early at {speed}x");
    Require(blink.Tick(TimeSpan.FromMilliseconds(300 / speed)).IsComplete,
        $"blink did not respect {speed}x timing");
}
blink.Speed = 1;

if (args.Length == 1)
{
    var catalog = NaturalMotionCatalog.Load(args[0]);
    Require(catalog.Poses.ContainsKey("06_idle"), "06_idle pose profile was not loaded");
    Require(catalog.Motions.Count == 0, "formal motion directory must remain empty at this stage");
}

Console.WriteLine("NaturalMotion smoke checks passed, including 1,000 blink state-machine cycles.");
return;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
