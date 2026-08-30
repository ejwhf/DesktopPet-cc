using System.IO;
using System.Windows.Threading;
using DesktopPet.Core.Configuration;

namespace DesktopPet.App.Services;

/// <summary>Debounces app-owned changes into Core's atomic, versioned settings store.</summary>
public sealed class PetSettingsCoordinator : IDisposable
{
    private readonly AtomicSettingsStore _store = new();
    private readonly string _settingsPath;
    private readonly DispatcherTimer _saveTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private PetSettings _current = new();
    private bool _disposed;
    private bool _saveInProgress;
    private bool _saveRequested;

    public PetSettingsCoordinator(Dispatcher dispatcher, string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "settings.json");
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public PetSettings Current => _current;

    public async Task<PetSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _store.LoadAsync(_settingsPath, cancellationToken: cancellationToken);
            _current = result.Settings;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            AggregateException or
            SettingsValidationException or
            NotSupportedException or
            System.Text.Json.JsonException)
        {
            _current = new PetSettings();
        }

        return _current;
    }

    public void Update(Func<PetSettings, PetSettings> update, bool saveImmediately = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(update);
        var candidate = update(_current) with { SchemaVersion = PetSettings.CurrentSchemaVersion };
        PetSettingsValidator.Validate(candidate).ThrowIfInvalid();
        if (candidate == _current)
        {
            return;
        }

        _current = candidate;
        _saveTimer.Stop();
        if (saveImmediately)
        {
            SaveInBackground();
        }
        else
        {
            _saveTimer.Start();
        }
    }

    public async Task FlushAsync()
    {
        if (_disposed)
        {
            return;
        }

        _saveTimer.Stop();
        var snapshot = _current;
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_disposed)
            {
                await _store.SaveAsync(_settingsPath, snapshot).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        SaveInBackground();
    }

    private async void SaveInBackground()
    {
        if (_saveInProgress)
        {
            _saveRequested = true;
            return;
        }

        _saveInProgress = true;
        try
        {
            do
            {
                _saveRequested = false;
                await FlushAsync();
            }
            while (_saveRequested && !_disposed);
        }
        finally
        {
            _saveInProgress = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        _disposed = true;
    }
}
