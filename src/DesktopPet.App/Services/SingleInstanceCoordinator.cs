namespace DesktopPet.App.Services;

/// <summary>Coordinates one desktop-pet process per interactive Windows session.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\DesktopPet.Application";
    private const string ActivationEventName = @"Local\DesktopPet.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly EventWaitHandle? _stopEvent;
    private readonly Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;

        if (!IsPrimaryInstance)
        {
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset);
        _listenerTask = Task.Run(ListenForActivation);
    }

    public bool IsPrimaryInstance { get; }

    public event EventHandler? ActivationRequested;

    public void SignalPrimaryInstance()
    {
        if (IsPrimaryInstance)
        {
            return;
        }

        try
        {
            using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
            activationEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The primary process is still starting or has already stopped.
        }
    }

    private void ListenForActivation()
    {
        if (_activationEvent is null || _stopEvent is null)
        {
            return;
        }

        var handles = new WaitHandle[] { _stopEvent, _activationEvent };
        while (WaitHandle.WaitAny(handles) == 1)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsPrimaryInstance)
        {
            _stopEvent?.Set();
            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }

            _activationEvent?.Dispose();
            _stopEvent?.Dispose();
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }
}
