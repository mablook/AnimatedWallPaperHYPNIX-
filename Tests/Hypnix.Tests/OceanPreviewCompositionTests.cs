using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using Grid=System.Windows.Controls.Grid;
using StackPanel=System.Windows.Controls.StackPanel;
using Border=System.Windows.Controls.Border;
using ScrollViewer=System.Windows.Controls.ScrollViewer;
using ScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility;
using Brushes=System.Windows.Media.Brushes;

namespace Hypnix.Tests;

public sealed class OceanPreviewCompositionTests
{
    [Fact]
    public Task OceanIsCompositedAndScrollCannotPaintOverHeaderOrFooter()=>OnSta(()=>
    {
        using var preview=new WallpaperPreviewControl{Width=80,Height=80};
        preview.SetSuspended(true);preview.Select(new("ocean",WallpaperKind.Ocean));
        Assert.True(preview.UsesWpfOcean);Assert.Null(preview.NativeController);
        var image=Assert.IsType<OceanPreviewControl>(preview.Children[0]);
        Assert.False(preview.Children.OfType<HwndHost>().Any());
        var pixels=Enumerable.Range(0,80*80).SelectMany(_=>new byte[]{0,0,255,255}).ToArray();
        image.Source=BitmapSource.Create(80,80,96,96,PixelFormats.Bgr32,null,pixels,320);
        var root=new Grid{Width=100,Height=100,Background=Brushes.Blue};
        root.RowDefinitions.Add(new(){Height=new GridLength(20)});
        root.RowDefinitions.Add(new(){Height=new GridLength(60)});
        root.RowDefinitions.Add(new(){Height=new GridLength(20)});
        var stack=new StackPanel();stack.Children.Add(preview);stack.Children.Add(new Border{Height=80,Background=Brushes.Green});
        var scroll=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Grid.SetRow(scroll,1);root.Children.Add(scroll);
        foreach(var offset in new[]{0d,30,90,0,45,0})
        {
            root.Measure(new System.Windows.Size(100,100));root.Arrange(new Rect(0,0,100,100));root.UpdateLayout();
            scroll.ScrollToVerticalOffset(offset);root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(100,100,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var result=new byte[40000];bitmap.CopyPixels(result,400,0);
            foreach(var y in new[]{5,15,85,95})Assert.Equal(new byte[]{255,0,0,255},result.Skip((y*100+50)*4).Take(4));
            Assert.Contains((byte)255,result);
            if(offset==0)Assert.Equal(new byte[]{0,0,255,255},result.Skip((40*100+50)*4).Take(4));
            if(offset==90)Assert.Equal(new byte[]{0,128,0,255},result.Skip((40*100+50)*4).Take(4));
        }
    });

    [Fact]
    public Task SwitchingKindsDisposesOldBackendWithoutLeavingNativeChildren()=>OnSta(()=>
    {
        using var preview=new WallpaperPreviewControl();preview.SetSuspended(true);
        preview.Select(new("fire",WallpaperKind.LivingFire));Assert.NotNull(preview.NativeController);
        preview.Select(new("ocean",WallpaperKind.Ocean));Assert.True(preview.UsesWpfOcean);Assert.Single(preview.Children.Cast<UIElement>());
        Assert.Null(preview.NativeController);preview.Select(null);Assert.Empty(preview.Children.Cast<UIElement>());
        preview.Select(new("ocean",WallpaperKind.Ocean));Assert.Single(preview.Children.Cast<UIElement>());
    });

    private static Task OnSta(Action action)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{action();done.SetResult();}catch(Exception e){done.SetException(e);}finally{Dispatcher.CurrentDispatcher.InvokeShutdown();}}){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
