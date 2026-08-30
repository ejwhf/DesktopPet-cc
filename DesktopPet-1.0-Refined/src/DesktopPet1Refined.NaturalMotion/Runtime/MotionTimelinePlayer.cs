using DesktopPet1Refined.NaturalMotion.Models;

namespace DesktopPet1Refined.NaturalMotion.Runtime;

public enum MotionPlaybackState
{
    Stopped,
    Playing,
    Paused
}

public sealed class MotionTimelinePlayer
{
    private MotionDefinition? _motion;
    private TimeSpan _startedAt;
    private TimeSpan _pausedAt;
    private TimeSpan _pausedDuration;
    private bool _interruptRequested;
    private int _interruptLoop;

    public MotionPlaybackState State { get; private set; } = MotionPlaybackState.Stopped;

    public double Speed { get; set; } = 1;

    public MotionDefinition? Motion => _motion;

    public void Start(MotionDefinition motion, TimeSpan timestamp)
    {
        ArgumentNullException.ThrowIfNull(motion);
        _motion = motion;
        _startedAt = timestamp;
        _pausedAt = default;
        _pausedDuration = default;
        _interruptRequested = false;
        _interruptLoop = 0;
        State = MotionPlaybackState.Playing;
    }

    public void Pause(TimeSpan timestamp)
    {
        if (State != MotionPlaybackState.Playing)
        {
            return;
        }
        _pausedAt = timestamp;
        State = MotionPlaybackState.Paused;
    }

    public void Resume(TimeSpan timestamp)
    {
        if (State != MotionPlaybackState.Paused)
        {
            return;
        }
        _pausedDuration += timestamp - _pausedAt;
        State = MotionPlaybackState.Playing;
    }

    public void Restart(TimeSpan timestamp)
    {
        if (_motion is not null)
        {
            Start(_motion, timestamp);
        }
    }

    public void Stop() => State = MotionPlaybackState.Stopped;

    public bool RequestInterrupt(TimeSpan timestamp)
    {
        if (_motion is null || State == MotionPlaybackState.Stopped)
        {
            return true;
        }
        switch (_motion.InterruptPolicy)
        {
            case MotionInterruptPolicy.Immediate:
                Stop();
                return true;
            case MotionInterruptPolicy.Locked:
                return false;
            case MotionInterruptPolicy.FinishCurrentLoop:
                var current = GetSample(timestamp);
                _interruptRequested = true;
                _interruptLoop = current.CompletedLoops;
                return false;
            default:
                return false;
        }
    }

    public MotionSample GetSample(TimeSpan timestamp)
    {
        if (_motion is null)
        {
            throw new InvalidOperationException("No motion has been started.");
        }
        var effectiveTimestamp = State == MotionPlaybackState.Paused ? _pausedAt : timestamp;
        var elapsed = effectiveTimestamp - _startedAt - _pausedDuration;
        var speed = Speed is 0.25 or 0.5 or 1 ? Speed : Math.Clamp(Speed, 0.01, 4);
        return MotionSampler.Sample(_motion, elapsed.TotalMilliseconds * speed);
    }

    public MotionSample Tick(TimeSpan timestamp)
    {
        if (State == MotionPlaybackState.Stopped)
        {
            throw new InvalidOperationException("The motion player is stopped.");
        }
        var sample = GetSample(timestamp);
        if (sample.IsComplete || (_interruptRequested && sample.CompletedLoops > _interruptLoop))
        {
            State = MotionPlaybackState.Stopped;
        }
        return sample;
    }
}
