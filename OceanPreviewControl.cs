using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper.Services;
using Image = System.Windows.Controls.Image;
using Grid = System.Windows.Controls.Grid;

namespace AnimatedWallPaper;

// No HWND, DXGI window association, GDI drawing or UI-thread wait. Only complete BGRA frames
// cross to WPF; one reusable buffer and at most one outstanding dispatcher callback per session.
internal sealed class OceanPreviewControl : Grid, IDisposable
{
    private readonly Image _image=new(){Stretch=Stretch.Fill};
    internal ImageSource? Source { get=>_image.Source; set=>_image.Source=value; }
    internal long PresentedFrames { get; private set; }
    private sealed class Session(OceanRuntime runtime,int width,int height)
    {
        internal readonly OceanRuntime Runtime=runtime;
        internal readonly int Width=width,Height=height;
        internal readonly byte[] Pixels=new byte[checked(width*height*4)];
        internal OceanGpuRenderer? Renderer;
        internal WallpaperRenderWorker? Worker;
        internal WriteableBitmap? Bitmap;
        internal volatile bool Active,Stopped,Failed,Ready;
        internal int Pending,Repaint=1,Interval=33,RenderMs;
    }
    private readonly DispatcherTimer _resize=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private OceanRuntime? _runtime;
    private OceanPreferences? _preferences;
    private Session? _session;
    private bool _suspended,_userPaused,_disposed;
    private int _fps=30;
    internal event Action<string>? StatusChanged;
    internal OceanPreviewControl()
    {
        // An empty Image arranges to 0x0 before its first Source. The panel must own the
        // viewport size so startup can render the very frame that supplies that Source.
        ClipToBounds=true;Children.Add(_image);
        _resize.Tick+=async(_,_)=>{_resize.Stop();await RefreshAsync();};
        SizeChanged+=(_,_)=>Schedule();IsVisibleChanged+=(_,_)=>Schedule();
        Loaded+=(_,_)=>Schedule();Unloaded+=(_,_)=>StopSession();
    }
    internal void Select(WallpaperRequest request)
    {
        var runtime=request.Ocean??(_preferences==request.OceanPreferences?_runtime:null)??new OceanRuntime(request.OceanPreferences);
        _preferences=request.OceanPreferences;_fps=request.FramesPerSecond;
        if(!ReferenceEquals(runtime,_runtime)){StopSession();Source=null;_runtime=runtime;}
        else if(_session?.Failed==true)StopSession();
        Schedule();
    }
    internal void SetSuspended(bool value){_suspended=value;Schedule();}
    internal void SetUserPaused(bool value){_userPaused=value;Schedule();}
    internal void SetFrameCap(int fps){_fps=fps;if(_session is { } s)s.Interval=1000/Math.Clamp(fps,1,120);}
    internal void RefreshOcean(){if(_session is { } s){Interlocked.Exchange(ref s.Repaint,1);s.Worker?.Signal();}}
    private void Schedule()
    {
        _resize.Stop();
        var active=!_disposed&&!_suspended&&IsVisible&&IsLoaded&&!_userPaused;
        if(_session is { } s)
        {
            s.Active=active;s.Runtime.SetPaused(s,!active);s.Worker?.Signal();
            if(active&&s.Ready&&!s.Failed)StatusChanged?.Invoke("Live preview");
        }
        if(!_disposed&&!_suspended&&IsVisible&&IsLoaded)_resize.Start();
    }
    private async Task RefreshAsync()
    {
        if(_disposed||_suspended||!IsVisible||!IsLoaded||_runtime is null||ActualWidth<2||ActualHeight<2)return;
        var dpi=VisualTreeHelper.GetDpi(this);
        // Preview is bounded; desktop retains full selected resolution/quality.
        var scale=Math.Min(1,1280/(ActualWidth*dpi.DpiScaleX));
        var width=Math.Max(2,(int)(ActualWidth*dpi.DpiScaleX*scale));
        var height=Math.Max(2,(int)(ActualHeight*dpi.DpiScaleY*scale));
        if(_session is { } ready && ready.Width==width&&ready.Height==height&&!ready.Failed)return;
        StopSession();
        var session=new Session(_runtime,width,height){Active=!_userPaused,Interval=1000/Math.Clamp(_fps,1,120)};
        _session=session;StatusChanged?.Invoke("Preparing preview…");
        session.Worker=new WallpaperRenderWorker(()=>
        {
            session.Renderer=new OceanGpuRenderer(IntPtr.Zero,width,height,preview:true);
            Render(session);
        },()=>Render(session),()=>session.Active&&!session.Stopped&&!session.Failed
            ?Math.Max(1,session.Interval-session.RenderMs):Timeout.Infinite,()=>
        {
            session.Runtime.Release(session);session.Renderer?.Dispose();
        });
        try
        {
            await session.Worker.StartAsync(TimeSpan.FromSeconds(45));
            if(_session!=session||_disposed)return;
            session.Runtime.Activate(session,!session.Active);
            session.Ready=true;
            AppLog.Write($"Ocean WPF preview ready. Size={width}x{height}; PreviewHwnd=False; WindowSwapChain=False");
            StatusChanged?.Invoke("Live preview");
        }
        catch(Exception exception)
        {
            if(_session!=session||_disposed)return;
            session.Failed=true;session.Runtime.Release(session);
            AppLog.WriteException("Ocean WPF preview",exception);StatusChanged?.Invoke("Preview unavailable: "+exception.Message);
        }
    }
    private void Render(Session s)
    {
        if(s.Stopped||s.Failed||(!s.Active&&Volatile.Read(ref s.Repaint)==0)||
            Interlocked.CompareExchange(ref s.Pending,1,0)!=0)return;
        try
        {
            Interlocked.Exchange(ref s.Repaint,0);
            var start=Stopwatch.GetTimestamp();var (frame,settings)=s.Runtime.Frame();
            s.Renderer!.Render(frame.WaterTime,settings,present:false,celestialUtc:frame.SkyUtc,
                weatherTime:frame.WeatherTime,cloudTrajectory:frame.Clouds);
            s.Renderer.PreviewSurface!.CopyPixels(s.Pixels);
            s.RenderMs=(int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>
            {
                try
                {
                    if(s!=_session||s.Stopped||_disposed)return;
                    s.Bitmap??=new WriteableBitmap(s.Width,s.Height,96,96,PixelFormats.Bgr32,null);
                    s.Bitmap.WritePixels(new Int32Rect(0,0,s.Width,s.Height),s.Pixels,s.Width*4,0);
                    Source=s.Bitmap;
                    PresentedFrames++;
                }
                finally
                {
                    Volatile.Write(ref s.Pending,0);
                    // An edit can arrive while the previous frame awaits WPF. Preserve that
                    // redraw even with animation paused (the worker otherwise waits forever).
                    if(!s.Stopped&&Volatile.Read(ref s.Repaint)!=0)s.Worker?.Signal();
                }
            }));
        }
        catch(Exception exception)
        {
            Volatile.Write(ref s.Pending,0);s.Failed=true;s.Runtime.Release(s);
            AppLog.WriteException("Ocean WPF preview frame",exception);
            Dispatcher.BeginInvoke(new Action(()=>{if(s==_session&&!_disposed)StatusChanged?.Invoke("Preview unavailable: "+exception.Message);}));
            throw;
        }
    }
    private void StopSession()
    {
        if(_session is not { } s)return;
        _session=null;s.Stopped=true;s.Runtime.Release(s);s.Worker?.Stop(TimeSpan.Zero);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_resize.Stop();StopSession();Source=null;}
}
