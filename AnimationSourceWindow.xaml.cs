using System.Windows;

namespace AnimatedWallPaper;

public partial class AnimationSourceWindow : Window
{
    public AnimationSourceWindow(int targetFramesPerSecond)
    {
        InitializeComponent();
        Backdrop.TargetFramesPerSecond = targetFramesPerSecond;

        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        // Keep one pixel on-screen so DWM continues composing the source,
        // without letting the source window cover the desktop or applications.
        Left = SystemParameters.VirtualScreenLeft - Width + 1;
        Top = SystemParameters.VirtualScreenTop;
    }

    public void SetFrameCap(int framesPerSecond) =>
        Backdrop.TargetFramesPerSecond = framesPerSecond;

    public void Resume() => Backdrop.Start();

    public void Pause() => Backdrop.Pause();

    protected override void OnClosed(EventArgs e)
    {
        Backdrop.Dispose();
        base.OnClosed(e);
    }
}
