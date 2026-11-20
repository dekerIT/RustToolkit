using System.Media;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace RustToolkit.Desktop.Services;

public sealed class AlarmService : IDisposable
{
    private readonly SoundPlayer _player;
    private bool _audioAvailable;
    private bool _disposed;

    public AlarmService()
    {
        var sirenPath = CreateSirenFile();
        _player = new SoundPlayer(sirenPath);
        try
        {
            _player.Load();
            _audioAvailable = true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or Win32Exception)
        {
            Trace.TraceError($"Could not load the alarm sound: {exception}");
        }
    }

    public bool IsActive { get; private set; }

    public event Action<string>? Triggered;
    public event Action? Stopped;

    public void Trigger(string reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Alarm" : reason.Trim();
        IsActive = true;
        if (_audioAvailable)
        {
            try
            {
                _player.PlayLooping();
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or Win32Exception)
            {
                _audioAvailable = false;
                Trace.TraceError($"Could not play the alarm sound: {exception}");
            }
        }

        if (!_audioAvailable)
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception)
            {
                Trace.TraceError($"Could not play the fallback alarm sound: {exception}");
            }
        }

        Triggered?.Invoke(normalizedReason);
    }

    public void Stop()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _player.Stop();
        Stopped?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _player.Dispose();
        _disposed = true;
    }

    private static string CreateSirenFile()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustToolkit");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "alarm-tone.wav");
        const int sampleRate = 22050;
        const double durationSeconds = 1.4;
        const int bitsPerSample = 16;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataLength = sampleCount * sizeof(short);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * bitsPerSample / 8);
        writer.Write((short)(bitsPerSample / 8));
        writer.Write((short)bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (double)sampleRate;
            var sweep = 720 + 300 * Math.Sin(2 * Math.PI * 0.9 * time);
            var pulse = 0.68 + 0.32 * Math.Sin(2 * Math.PI * 2.2 * time);
            var sample = (short)(short.MaxValue * 0.42 * pulse * Math.Sin(2 * Math.PI * sweep * time));
            writer.Write(sample);
        }

        return path;
    }
}
