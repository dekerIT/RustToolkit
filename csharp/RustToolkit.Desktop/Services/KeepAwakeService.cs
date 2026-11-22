using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RustToolkit.Desktop.Services;

public sealed class KeepAwakeService : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private bool _enabled;
    private bool _disposed;

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_enabled == enabled)
        {
            return;
        }

        var flags = enabled ? EsContinuous | EsSystemRequired : EsContinuous;
        if (SetThreadExecutionState(flags) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _enabled = enabled;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SetEnabled(false);
        _disposed = true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);
}
