namespace DesktopPet1Refined.NaturalMotion.Runtime;

public enum BlinkPhase
{
    Idle,
    Closing,
    Closed,
    Opening
}

public enum BlinkFrame
{
    Open,
    Forty,
    SeventyFive,
    Closed
}

public readonly record struct BlinkSample(BlinkPhase Phase, BlinkFrame Frame, double Progress, bool IsComplete);

public sealed class BlinkTimeline
{
    public const double ClosingDurationMs = 100;
    public const double ClosedDurationMs = 50;
    public const double OpeningDurationMs = 150;

    private TimeSpan _phaseStartedAt;
    private TimeSpan _pausedAt;

    public BlinkPhase Phase { get; private set; } = BlinkPhase.Idle;

    public bool IsPaused { get; private set; }

    public double Speed { get; set; } = 1;

    public void Start(TimeSpan now)
    {
        Phase = BlinkPhase.Closing;
        IsPaused = false;
        _phaseStartedAt = now;
    }

    public void Stop()
    {
        Phase = BlinkPhase.Idle;
        IsPaused = false;
    }

    public void Pause(TimeSpan now)
    {
        if (Phase == BlinkPhase.Idle || IsPaused)
        {
            return;
        }
        _pausedAt = now;
        IsPaused = true;
    }

    public void Resume(TimeSpan now)
    {
        if (!IsPaused)
        {
            return;
        }
        _phaseStartedAt += now - _pausedAt;
        IsPaused = false;
    }

    public BlinkSample Tick(TimeSpan now)
    {
        if (Phase == BlinkPhase.Idle)
        {
            return new BlinkSample(BlinkPhase.Idle, BlinkFrame.Open, 0, true);
        }
        if (!double.IsFinite(Speed) || Speed <= 0)
        {
            throw new InvalidOperationException("Blink speed must be finite and greater than zero.");
        }

        var sampleTime = IsPaused ? _pausedAt : now;
        while (true)
        {
            var duration = Phase switch
            {
                BlinkPhase.Closing => ClosingDurationMs,
                BlinkPhase.Closed => ClosedDurationMs,
                BlinkPhase.Opening => OpeningDurationMs,
                _ => 0
            };
            var elapsed = Math.Max(0, (sampleTime - _phaseStartedAt).TotalMilliseconds * Speed);
            if (elapsed < duration)
            {
                var progress = Phase switch
                {
                    BlinkPhase.Closing => elapsed / ClosingDurationMs,
                    BlinkPhase.Closed => 1,
                    BlinkPhase.Opening => 1 - (elapsed / OpeningDurationMs),
                    _ => 0
                };
                return new BlinkSample(Phase, SelectFrame(progress), progress, false);
            }

            _phaseStartedAt += TimeSpan.FromMilliseconds(duration / Speed);
            Phase = Phase switch
            {
                BlinkPhase.Closing => BlinkPhase.Closed,
                BlinkPhase.Closed => BlinkPhase.Opening,
                BlinkPhase.Opening => BlinkPhase.Idle,
                _ => BlinkPhase.Idle
            };
            if (Phase == BlinkPhase.Idle)
            {
                IsPaused = false;
                return new BlinkSample(BlinkPhase.Idle, BlinkFrame.Open, 0, true);
            }
        }
    }

    public static BlinkFrame SelectFrame(double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return progress switch
        {
            < 0.20 => BlinkFrame.Open,
            < 0.60 => BlinkFrame.Forty,
            < 0.90 => BlinkFrame.SeventyFive,
            _ => BlinkFrame.Closed
        };
    }
}
