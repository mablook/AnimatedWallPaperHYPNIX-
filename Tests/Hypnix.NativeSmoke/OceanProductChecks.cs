using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using ListBox=System.Windows.Controls.ListBox;
using RadioButton=System.Windows.Controls.RadioButton;
using Size=System.Windows.Size;

internal static class OceanProductChecks
{
    // Runs without showing windows, changing wallpaper assignments or interacting with the desktop.
    internal static void CheckOffscreen(string output)
    {
        Directory.CreateDirectory(output);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,213,121,preview:true);
        var pixels=new byte[213*121*4];byte[]? previous=null;
        foreach(var time in new[]{0d,1d})
        {
            var p=new OceanPreferences(DayCycle:false);
            renderer.Render(time,p.ToRenderSettings(),present:false,celestialUtc:p.Normalize().SkyUtc,weatherTime:time);
            renderer.PreviewSurface!.CopyPixels(pixels);
            Check(pixels.SequenceEqual(renderer.Pixels()),"Reusable WPF readback differs from GPU frame.");
            if(previous is not null)Check(!pixels.SequenceEqual(previous),"Ocean offscreen animation is frozen.");
            previous=(byte[])pixels.Clone();
        }
        CheckEditor(output);
        CheckWpfPreviewLifecycle();
        File.WriteAllText(Path.Combine(output,"offscreen-checks.json"),JsonSerializer.Serialize(new
        {windowsShown=false,desktopModified=false,reusableReadbackExact=true,frames=2,width=213,height=121,editorApplyChecks=true,wpfStartupAnimationPausedEdits=true}));
        Console.WriteLine("Ocean offscreen readback and editor apply checks passed; no windows shown.");
    }

    private static void CheckWpfPreviewLifecycle()
    {
        // WPF needs a PresentationSource for Loaded. This test owner has no WS_VISIBLE;
        // it is never shown, attached to Explorer or used as a GPU presentation target.
        using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Ocean hidden lifecycle test")
        {Width=160,Height=90,WindowStyle=unchecked((int)0x80000000)});
        using var preview=new OceanPreviewControl{Width=160,Height=90};
        var runtime=new OceanRuntime(new OceanPreferences(DayCycle:false));
        preview.Select(new("ocean",WallpaperKind.Ocean,Ocean:runtime));
        source.RootVisual=preview;
        preview.Measure(new Size(160,90));preview.Arrange(new Rect(0,0,160,90));preview.UpdateLayout();
        Pump(()=>preview.PresentedFrames>=3,50);
        Check(preview.Source is WriteableBitmap,"WPF preview never produced its first image.");
        preview.SetUserPaused(true);
        // Drain an already prepared frame without allowing a hidden user-facing window.
        var settle=Stopwatch.StartNew();Pump(()=>settle.ElapsedMilliseconds>=250,3);
        var stable=preview.PresentedFrames;
        var freeze=runtime.Frame().Snapshot.WaterTime;
        settle.Restart();Pump(()=>settle.ElapsedMilliseconds>=150,3);
        Check(preview.PresentedFrames==stable,"Paused preview kept rendering.");
        var image=(WriteableBitmap)preview.Source!;
        var stride=image.PixelWidth*4;
        var pixelsBefore=new byte[stride*image.PixelHeight];image.CopyPixels(pixelsBefore,stride,0);
        runtime.Update(new OceanPreferences(Moment:OceanMoment.Moon,SkyUtc:OceanPreferences.MomentUtc(OceanMoment.Moon),DayCycle:false));
        preview.RefreshOcean();Pump(()=>preview.PresentedFrames>stable,10);
        var pixelsAfter=new byte[pixelsBefore.Length];((WriteableBitmap)preview.Source!).CopyPixels(pixelsAfter,stride,0);
        Check(!pixelsBefore.SequenceEqual(pixelsAfter),"Paused Sun-to-Moon edit did not repaint.");
        Check(runtime.Frame().Snapshot.WaterTime==freeze,"Paused edit advanced the water clock.");
        // Queue another edit while the first frame is pending on the dispatcher.
        stable=preview.PresentedFrames;
        runtime.Update(new OceanPreferences(Moment:OceanMoment.Day,SkyUtc:OceanPreferences.MomentUtc(OceanMoment.Day),DayCycle:false));
        preview.RefreshOcean();
        Thread.Sleep(200); // dispatcher intentionally blocked while the worker prepares a frame
        runtime.Update(new OceanPreferences(Moment:OceanMoment.Moon,SkyUtc:OceanPreferences.MomentUtc(OceanMoment.Moon),DayCycle:false));
        preview.RefreshOcean();Pump(()=>preview.PresentedFrames>=stable+2,10);
        preview.SetUserPaused(false);stable=preview.PresentedFrames;
        Pump(()=>preview.PresentedFrames>=stable+2,10);
        source.RootVisual=null;
    }
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    public static void Run(string output)
    {
        var watch=Stopwatch.StartNew();
        CaptureMoments(output);
        CheckEditor(output);
        CheckHost(output);
        File.WriteAllText(Path.Combine(output,"product-checks.json"),JsonSerializer.Serialize(new{
            curatedMoments=4,draftIsolation=true,perDisplayPersistence=true,failedApplyPreservesPrevious=true,
            editorSizes=new[]{"1280x820","960x740","640x520"},offscreenPreview=true,desktopSwapChain=true,
            heldSkyAnimatedWater=true,hostPause=true,seconds=watch.Elapsed.TotalSeconds
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS: ocean product moments, editor drafts, display isolation, failure recovery, responsive layout and native presentation.");
    }
    private static void CaptureMoments(string output)
    {
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,960,540);
        foreach(var moment in Enum.GetValues<OceanMoment>())
        {
            var p=new OceanPreferences(Moment:moment,SkyUtc:OceanPreferences.MomentUtc(moment),DayCycle:false);
            renderer.Render(8,p.ToRenderSettings(),celestialUtc:p.SkyUtc,weatherTime:8);
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,moment.ToString().ToLowerInvariant()+"-state.png"),960,540);
        }
        foreach(var coverage in new[]{0f,.37f,1f})
        {
            var settings=new OceanPreferences(Coverage:coverage,DayCycle:false).ToRenderSettings();
            renderer.Render(8,settings,celestialUtc:settings.Celestial!.EpochUtc,weatherTime:8);
            Check(renderer.Pixels().Any(x=>x>0),"Coverage value failed to render.");
        }
    }
    private static void CheckEditor(string output)
    {
        var context=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var targets=new[]{new DesktopWorker.WallpaperTarget(0,0,1920,1080,"ocean-test-a","DISPLAY1",96,96),
            new DesktopWorker.WallpaperTarget(1920,0,1080,1920,"ocean-test-b","DISPLAY2",96,96)};
        var fail=false;var starts=0;
        using var controller=new DisplayWallpaperController((_,_)=>
        {
            starts++;
            return fail?Task.FromException<IWallpaperSession>(new IOException("Deliberate preparation failure")):
                Task.FromResult<IWallpaperSession>(new FakeSession());
        });
        var store=new AppSettingsStore(Path.Combine(output,"ocean-editor-settings.json"));store.Save(new(){AudioReactive=false,AppPauseMode=0});
        var window=new MainWindow(store,Path.Combine(output,"editor-library"),controller,LicenseWindowChecks.CreateOwnedLicense(),()=>targets);
        try
        {
            var gallery=(ListBox)window.FindName("WallpaperGallery");gallery.SelectedItem=gallery.Items.Cast<WallpaperEntry>().Single(e=>e.Kind==WallpaperKind.Ocean);
            window.OpenOceanEditor();var editor=(OceanEditorControl)window.FindName("OceanEditor");
            Check(editor.Visibility==Visibility.Visible,"Ocean page not shown.");
            Check(((FrameworkElement)window.FindName("LibraryWorkspace")).Visibility==Visibility.Collapsed,"Library remained over the ocean page.");
            var draft=editor.Draft!;draft.ChooseMoment(OceanMoment.Moon);draft.Edit(draft.Preferences with{Coverage=.75f});editor.RefreshLabels();
            Check(starts==0,"Editing modified the desktop.");
            var apply=typeof(MainWindow).GetMethod("ApplyOceanAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            void Apply(bool all){var task=(Task)apply.Invoke(window,[all])!;Pump(()=>task.IsCompleted,5);task.GetAwaiter().GetResult();}
            Apply(false);Check(starts==1&&controller.ActiveRequests.Count==1,"Apply changed the wrong number of monitors.");
            var active=controller.ActiveRequests[targets[0].DeviceId];Check(!ReferenceEquals(active.Ocean,draft.Runtime),"Desktop shares mutable preview runtime.");
            draft.Edit(draft.Preferences with{Coverage=0});Check(active.Ocean!.Frame().Snapshot.Preferences.Coverage==.75f,"Draft leaked into applied ocean.");
            fail=true;Apply(false);Check(ReferenceEquals(controller.ActiveRequests[targets[0].DeviceId].Ocean,active.Ocean),"Failure replaced the healthy wallpaper.");
            Check(draft.IsDirty,"Failed apply lost draft.");fail=false;draft.Discard();Check(draft.Preferences.Coverage==.75f,"Discard did not restore applied settings.");
            Apply(true);Check(controller.ActiveRequests.Count==2,"Apply all omitted a display.");
            Check(!ReferenceEquals(controller.ActiveRequests[targets[0].DeviceId].Ocean,controller.ActiveRequests[targets[1].DeviceId].Ocean),"Displays share mutable runtime.");
            typeof(MainWindow).GetMethod("SavePreferences",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            var saved=store.Load();Check(saved.DisplayWallpapers.Values.All(d=>d.Oceans["ocean"].Coverage==.75f),"Ocean settings were not saved per display.");
            foreach(var size in new[]{new Size(1280,820),new Size(960,740),new Size(640,520)})
            {
                var root=(FrameworkElement)window.Content;root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                foreach(var button in Descendants(editor).OfType<Button>().Where(b=>b.IsVisible&&b.IsEnabled&&b.Content is string text&&text.StartsWith("Apply to ",StringComparison.Ordinal)))
                {
                    var bounds=button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                    Check(bounds.Right<=size.Width+1&&bounds.Bottom<=size.Height+1,"Apply button clipped at "+size);
                }
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"editor-{size.Width}x{size.Height}.png"));encoder.Save(file);
            }
            Check(Descendants(editor).OfType<ComboBox>().All(c=>!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(c))||c.DisplayMemberPath=="Label"),"Unlabelled editor choice.");
        }
        finally
        {
            typeof(MainWindow).GetField("_isQuitting",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);window.Close();
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
    private static void CheckHost(string output)
    {
        var parent=CreateWindowEx(0,"STATIC","Ocean product test",0x80000000,0,0,640,360,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(parent==IntPtr.Zero)throw new InvalidOperationException("Cannot create test surface.");
        try
        {
            foreach(var preview in new[]{true,false})
            {
                var runtime=new OceanRuntime(new(DayCycle:false));
                using var host=new NativeWallpaperHost(NativeRenderMode.Ocean,preview:preview?new(parent,640,360):null,
                    target:preview?null:new DesktopWorker.WallpaperTarget(0,0,640,360,"test","test",96,96),ocean:runtime);
                var start=host.StartAsync(30,reveal:false);Pump(()=>start.IsCompleted,60);start.GetAwaiter().GetResult();
                var renderer=(OceanGpuRenderer)typeof(NativeWallpaperHost).GetField("_oceanRenderer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
                Check((renderer.PreviewSurface is not null)==preview,"Wrong ocean presentation backend.");
                Check(host.PresentedFrameCount>0,"Host did not prepare an ocean frame.");
                var first=runtime.Frame().Snapshot;host.Show();var firstCount=host.PresentedFrameCount;Pump(()=>host.PresentedFrameCount>firstCount+3,10);
                host.Pause();var paused=runtime.Frame().Snapshot;var limit=Stopwatch.StartNew();Pump(()=>limit.ElapsedMilliseconds>150,2);
                Check(runtime.Frame().Snapshot.SkyUtc==paused.SkyUtc&&runtime.Frame().Snapshot.WaterTime==paused.WaterTime,"Paused runtime advanced.");
                Check(paused.WaterTime>first.WaterTime&&paused.SkyUtc==first.SkyUtc,"Held sky stopped water or advanced the sky.");
                host.RefreshOcean();var count=host.PresentedFrameCount;Pump(()=>host.PresentedFrameCount>count,10);
                Check(host.IsHealthy,"Ocean host failed while paused.");host.Resume();
            }
        }
        finally {DestroyWindow(parent);}
    }
    internal static void ShowProduct(System.Windows.Application app,string output)
    {
        var store=new AppSettingsStore(Path.Combine(output,"interactive-settings.json"));
        store.Save(new(){SelectedWallpaperId="ocean",AudioReactive=false,PreviewPaneCollapsed=true});
        var window=new MainWindow(store,Path.Combine(output,"interactive-library"),license:LicenseWindowChecks.CreateOwnedLicense());
        window.Loaded+=(_,_)=>window.OpenOceanEditor();app.Run(window);
    }
    private static void Pump(Func<bool> done,int seconds)
    {
        var clock=Stopwatch.StartNew();
        while(!done())
        {
            if(clock.Elapsed.TotalSeconds>seconds)throw new TimeoutException("Ocean product check timed out.");
            Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.Background);Thread.Sleep(10);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private sealed class FakeSession:IWallpaperSession
    {
        public int? ProcessId=>null;public bool IsHealthy=>true;public void Show(){}public void SetFrameCap(int fps){}public void Resume(){}public void Pause(){}
        public void SetPausedMonitors(IReadOnlyList<int> indices){}public void UpdateVisualizerSettings(VisualizerSettings settings){}public void SetAudioEnabled(bool enabled){}public void Dispose(){}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr window);
}
