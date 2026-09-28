using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button=System.Windows.Controls.Button;
using ListBox=System.Windows.Controls.ListBox;

internal static class LibraryStartupChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    private static Task InvokeTask(MainWindow window,string name)=>(Task)typeof(MainWindow).GetMethod(name,Private)!.Invoke(window,null)!;
    private static void Pump(Func<bool> done)
    {
        var clock=Stopwatch.StartNew();
        while(!done()){if(clock.Elapsed.TotalSeconds>5)throw new TimeoutException("Library startup check timed out.");Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.Background);Thread.Sleep(5);}
    }
    internal static void Run(string output)
    {
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            foreach(var scenario in new[]{0,1,2})RunCase(output,scenario);
            File.WriteAllText(Path.Combine(output,"checks.json"),System.Text.Json.JsonSerializer.Serialize(new
            {freshApply=true,manualApplyDuringRestore=true,lateRestoreRejected=true,otherDisplayRestored=true,applyAfterRestoreFailure=true,oceanFirst=true,windowsShown=false}));
            Console.WriteLine("PASS: library Apply works without Ocean editor at startup and during saved-wallpaper restore; late restore cannot overwrite manual choice.");
        }
        finally{SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static void RunCase(string output,int scenario)
    {
        var restoreSaved=scenario!=0;
        var target=new DesktopWorker.WallpaperTarget(0,0,1920,1080,"library-startup-test","DISPLAY1");
        var other=new DesktopWorker.WallpaperTarget(1920,0,1920,1080,"library-startup-other","DISPLAY2");
        var store=new AppSettingsStore(Path.Combine(output,$"scenario-{scenario}.json"));
        var settings=new AppSettings{SelectedWallpaperId="living-fire",AudioReactive=false,AppPauseMode=0,PreviewPaneCollapsed=true};
        if(restoreSaved)settings.DisplayWallpapers[target.DeviceId]=new(){Enabled=true,WallpaperId="ocean"};
        if(restoreSaved)settings.DisplayWallpapers[other.DeviceId]=new(){Enabled=true,WallpaperId="living-fire"};
        store.Save(settings);
        var pending=new TaskCompletionSource<IWallpaperSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0;var late=new Session();
        using var controller=new DisplayWallpaperController((_,_)=>++calls==1&&restoreSaved?pending.Task:Task.FromResult<IWallpaperSession>(new Session()));
        var window=new MainWindow(store,Path.Combine(output,"library"),controller,LicenseWindowChecks.CreateOwnedLicense(),()=>[target,other]);
        try
        {
            var button=(Button)window.FindName("StartButton");var gallery=(ListBox)window.FindName("WallpaperGallery");
            Check(((WallpaperEntry)gallery.Items[0]).Kind==WallpaperKind.Ocean,"Ocean is not the first library card.");
            Check(button.IsEnabled,"Fresh library disabled Apply before opening any editor.");
            Task? restoration=null;
            if(restoreSaved)
            {
                restoration=InvokeTask(window,"RestoreMissingDisplaysAsync");
                Check(!restoration.IsCompleted,"Fixture failed to delay initial restore.");
                gallery.SelectedItem=gallery.Items.Cast<WallpaperEntry>().Single(e=>e.Id=="living-fire");
                Check(button.IsEnabled,"Initial restore globally disables library Apply.");
                if(scenario==2)
                {
                    pending.SetException(new IOException("Deliberate startup preparation failure"));
                    Pump(()=>restoration.IsCompleted);
                    try{restoration.GetAwaiter().GetResult();throw new InvalidOperationException("Failure fixture unexpectedly restored.");}
                    catch(AggregateException){ }
                    Check(button.IsEnabled,"Failed startup restore left Apply disabled.");
                    restoration=null;
                }
            }
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(()=>controller.ActiveRequests.ContainsKey(target.DeviceId));
            Check(controller.ActiveRequests[target.DeviceId].Id=="living-fire","Library Apply did not use selected card.");
            Check(((OceanEditorControl)window.FindName("OceanEditor")).Draft is null,"Applying another wallpaper needed an Ocean draft.");
            if(restoration is not null)
            {
                pending.SetResult(late);Pump(()=>restoration.IsCompleted);restoration.GetAwaiter().GetResult();
                Check(late.Disposed&&!late.Shown,"Late saved restore replaced the manual wallpaper.");
                Pump(()=>controller.ActiveRequests.ContainsKey(other.DeviceId));
                Check(controller.ActiveRequests[target.DeviceId].Id=="living-fire","Resuming other displays reverted the manual choice.");
            }
            gallery.SelectedItem=gallery.Items.Cast<WallpaperEntry>().Single(e=>e.Kind==WallpaperKind.Ocean);
            Check(button.IsEnabled,"Ocean card could not apply directly from library.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(()=>controller.ActiveRequests[target.DeviceId].Kind==WallpaperKind.Ocean);
            Check(((FrameworkElement)window.FindName("OceanEditor")).Visibility==Visibility.Collapsed,"Direct Apply opened Customize Ocean.");
            Check(button.IsEnabled,"Apply remained disabled after completing the action.");
        }
        finally
        {
            pending.TrySetCanceled();typeof(MainWindow).GetField("_isQuitting",Private)!.SetValue(window,true);window.Close();
        }
    }
    private sealed class Session:IWallpaperSession
    {
        internal bool Disposed,Shown;
        public int? ProcessId=>null;public bool IsHealthy=>!Disposed;public void Show()=>Shown=true;
        public void Dispose()=>Disposed=true;public void Pause(){}public void Resume(){}public void SetFrameCap(int fps){}
        public void SetAudioEnabled(bool value){}public void UpdateVisualizerSettings(VisualizerSettings settings){}public void SetPausedMonitors(IReadOnlyList<int> indices){}
    }
}
