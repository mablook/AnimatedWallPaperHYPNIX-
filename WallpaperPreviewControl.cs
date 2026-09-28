using System.Windows;
using System.Windows.Controls;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

// Ocean frames participate in WPF composition: scroll clips, overlays and DPI all share one surface.
public sealed class WallpaperPreviewControl : Grid, IDisposable
{
    private NativeWallpaperPreviewControl? _native;
    private OceanPreviewControl? _ocean;
    private bool _suspended, _userPaused, _audioEnabled=true, _disposed;

    public event Action<string>? StatusChanged;
    internal WallpaperController? NativeController => _native?.Controller;
    internal bool UsesWpfOcean => _ocean is not null;

    internal void Select(WallpaperRequest? request)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(request?.Kind==WallpaperKind.Ocean)
        {
            if(_native is not null){Children.Remove(_native);_native.Dispose();_native=null;}
            if(_ocean is null)
            {
                _ocean=new();_ocean.StatusChanged+=ForwardStatus;Children.Add(_ocean);
                _ocean.SetSuspended(_suspended);_ocean.SetUserPaused(_userPaused);
            }
            _ocean.Select(request);return;
        }
        if(_ocean is not null){Children.Remove(_ocean);_ocean.Dispose();_ocean=null;}
        if(request is null && _native is null)return;
        if(_native is null)
        {
            _native=new();_native.StatusChanged+=ForwardStatus;Children.Add(_native);
            _native.SetSuspended(_suspended);if(_userPaused)_native.Controller.Pause();_native.SetAudioEnabled(_audioEnabled);
        }
        _native.Select(request);
    }
    private void ForwardStatus(string status)=>StatusChanged?.Invoke(status);
    internal void SetSuspended(bool value){_suspended=value;_native?.SetSuspended(value);_ocean?.SetSuspended(value);}
    internal void SetUserPaused(bool value){_userPaused=value;if(_native is not null){if(value)_native.Controller.Pause();else _native.Controller.Resume();}_ocean?.SetUserPaused(value);}
    internal void SetFrameCap(int fps){_native?.SetFrameCap(fps);_ocean?.SetFrameCap(fps);}
    internal void UpdateSettings(VisualizerSettings settings)=>_native?.UpdateSettings(settings);
    internal void SetAudioEnabled(bool enabled){_audioEnabled=enabled;_native?.SetAudioEnabled(enabled);}
    internal void RefreshOcean()=>_ocean?.RefreshOcean();
    public void Dispose(){if(_disposed)return;_disposed=true;_native?.Dispose();_ocean?.Dispose();Children.Clear();}
}
