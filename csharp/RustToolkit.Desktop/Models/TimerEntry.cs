using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RustToolkit.Desktop.Models;

public sealed class TimerEntry : INotifyPropertyChanged
{
    private string _remainingText = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset EndsAtUtc { get; set; }
    public bool TenMinuteAlertSent { get; set; }
    public bool ThreeMinuteAlertSent { get; set; }

    [JsonIgnore]
    public string RemainingText
    {
        get => _remainingText;
        private set
        {
            if (_remainingText == value)
            {
                return;
            }

            _remainingText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            RemainingText = "Finished";
            return;
        }

        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        var span = TimeSpan.FromSeconds(seconds);
        RemainingText = span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
