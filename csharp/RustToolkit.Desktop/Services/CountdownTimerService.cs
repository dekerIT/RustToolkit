using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class CountdownTimerService : IDisposable
{
    private readonly AppStateStore _store;
    private readonly DispatcherTimer _tickTimer;

    public CountdownTimerService(AppStateStore store)
    {
        _store = store;
        var savedTimers = _store.LoadTimers();
        Timers = new ObservableCollection<TimerEntry>(
            savedTimers.Where(timer => timer.EndsAtUtc > DateTimeOffset.UtcNow));
        if (Timers.Count != savedTimers.Count)
        {
            Save();
        }

        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += Tick;
        UpdateTimers();
        if (Timers.Count > 0)
        {
            _tickTimer.Start();
        }
    }

    public ObservableCollection<TimerEntry> Timers { get; }

    public event Action<string>? NotificationRequested;
    public event Action<string>? StorageError;

    public void Add(string name, int minutes)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("Enter a name for the timer.", nameof(name));
        }

        if (minutes is < 1 or > 10080)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), "Timer duration must be from 1 to 10080 minutes.");
        }

        var duration = TimeSpan.FromMinutes(minutes);
        var timer = new TimerEntry
        {
            Name = normalizedName,
            EndsAtUtc = DateTimeOffset.UtcNow.Add(duration),
            TenMinuteAlertSent = duration <= TimeSpan.FromMinutes(10),
            ThreeMinuteAlertSent = duration <= TimeSpan.FromMinutes(3),
        };
        timer.UpdateRemaining(duration);
        Timers.Add(timer);
        try
        {
            Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Timers.Remove(timer);
            throw;
        }

        _tickTimer.Start();
    }

    public void Remove(Guid id)
    {
        var timer = Timers.FirstOrDefault(item => item.Id == id);
        if (timer is null)
        {
            return;
        }

        var index = Timers.IndexOf(timer);
        Timers.RemoveAt(index);
        try
        {
            Save();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Timers.Insert(index, timer);
            throw;
        }

        if (Timers.Count == 0)
        {
            _tickTimer.Stop();
        }
    }

    public void Dispose()
    {
        _tickTimer.Stop();
        _tickTimer.Tick -= Tick;
    }

    private void Tick(object? sender, EventArgs e) => UpdateTimers();

    private void UpdateTimers()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var notifications = new List<string>();
        var previousState = Timers.Select(Clone).ToList();

        foreach (var timer in Timers.ToList())
        {
            var remaining = timer.EndsAtUtc - now;
            timer.UpdateRemaining(remaining);

            if (remaining <= TimeSpan.Zero)
            {
                notifications.Add($"Timer \"{timer.Name}\" has finished.");
                Timers.Remove(timer);
                changed = true;
                continue;
            }

            if (!timer.TenMinuteAlertSent && remaining <= TimeSpan.FromMinutes(10))
            {
                timer.TenMinuteAlertSent = true;
                notifications.Add($"Timer \"{timer.Name}\" has 10 minutes left.");
                changed = true;
            }

            if (!timer.ThreeMinuteAlertSent && remaining <= TimeSpan.FromMinutes(3))
            {
                timer.ThreeMinuteAlertSent = true;
                notifications.Add($"Timer \"{timer.Name}\" has 3 minutes left.");
                changed = true;
            }
        }

        if (changed)
        {
            try
            {
                Save();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                Timers.Clear();
                foreach (var timer in previousState)
                {
                    timer.UpdateRemaining(timer.EndsAtUtc - now);
                    Timers.Add(timer);
                }

                _tickTimer.Stop();
                StorageError?.Invoke($"Timer changes could not be saved. Active timers have been paused.\n{exception.Message}");
                return;
            }
        }

        foreach (var notification in notifications)
        {
            NotificationRequested?.Invoke(notification);
        }

        if (Timers.Count == 0)
        {
            _tickTimer.Stop();
        }
    }

    private void Save() => _store.SaveTimers(Timers);

    private static TimerEntry Clone(TimerEntry timer) => new()
    {
        Id = timer.Id,
        Name = timer.Name,
        EndsAtUtc = timer.EndsAtUtc,
        TenMinuteAlertSent = timer.TenMinuteAlertSent,
        ThreeMinuteAlertSent = timer.ThreeMinuteAlertSent,
    };
}
