using System.Runtime.InteropServices;

namespace AnimatedWallPaper.Services;

internal sealed class EffekseerBridge : IDisposable
{
    private IntPtr _state;
    private double _lastTime = double.NaN;

    private EffekseerBridge(IntPtr state) => _state = state;

    public static EffekseerBridge? TryCreate(IntPtr device, IntPtr context, string effectPath)
    {
        try
        {
            var state = Native.HypnixEfkCreate(device, context, 16000);
            if (state == IntPtr.Zero)
            {
                AppLog.Write("Effekseer bridge creation returned null; procedural fallback remains active");
                return null;
            }

            if (Native.HypnixEfkLoad(state, effectPath) == 0)
            {
                Native.HypnixEfkDestroy(state);
                AppLog.Write($"Effekseer effect load failed. Path={effectPath}");
                return null;
            }

            AppLog.Write($"Effekseer runtime initialized. Effect={effectPath}; Capacity=16000");
            return new EffekseerBridge(state);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            AppLog.WriteException("Effekseer native bridge unavailable; procedural fallback remains active", exception);
            return null;
        }
    }

    public void Update(double time, AethelisAudioProfile profile, float intensity)
    {
        if (_state == IntPtr.Zero) return;
        var delta = double.IsNaN(_lastTime) ? 1d / 60d : Math.Clamp(time - _lastTime, 0, 0.05);
        _lastTime = time;
        Native.HypnixEfkUpdate(_state, (float)delta, profile.Bass, profile.Mids, profile.Highs, intensity);
    }

    public void Render(float aspectRatio)
    {
        if (_state != IntPtr.Zero) Native.HypnixEfkRender(_state, aspectRatio);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_state == IntPtr.Zero) return;
        Native.HypnixEfkDestroy(_state);
        _state = IntPtr.Zero;
    }

    // Safety net: runs only if Dispose was skipped. The native state holds Direct3D references
    // that are unsafe to release on the finalizer thread, so we record the missed disposal for
    // diagnosis; the OS reclaims the memory at process exit. A finalizer must never throw.
    ~EffekseerBridge()
    {
        try
        {
            if (_state != IntPtr.Zero)
                AppLog.Write("EffekseerBridge finalized without Dispose(); native state left to OS reclamation.");
        }
        catch { }
    }

    private static class Native
    {
        private const string Library = "Hypnix.EffekseerBridge.dll";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr HypnixEfkCreate(IntPtr device, IntPtr context, int maxInstances);

        [DllImport(Library, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int HypnixEfkLoad(IntPtr state, string effectPath);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void HypnixEfkUpdate(IntPtr state, float deltaSeconds, float bass, float mids, float highs, float intensity);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void HypnixEfkRender(IntPtr state, float aspectRatio);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void HypnixEfkDestroy(IntPtr state);
    }
}
