using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AnimatedWallPaper.Services;

// Scoped high-resolution wait; does not change the machine/process timer period or busy-spin.
// Used only by the ocean fixture and its benchmark, not other wallpaper workers.
internal sealed class OceanFramePacer : IDisposable
{
    private readonly SafeWaitHandle _timer;
    public OceanFramePacer()
    {
        _timer = CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);
        if (_timer.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Wait(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0) return;
        var due = -(long)Math.Ceiling(Math.Min(milliseconds,1000)*10000);
        if (!SetWaitableTimer(_timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (WaitForSingleObject(_timer,2000)!=0) throw new Win32Exception("Ocean frame timer did not signal.");
    }
    public void Dispose() => _timer.Dispose();
    [DllImport("kernel32.dll",EntryPoint="CreateWaitableTimerExW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer,ref long due,int period,IntPtr completion,IntPtr arg,[MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle,uint milliseconds);
}
