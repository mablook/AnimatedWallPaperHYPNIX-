using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AnimatedWallPaper.Services;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AnimatedWallPaper.Controls;

public sealed class PositionPad : UserControl
{
    readonly TranslateTransform marker = new();
    const double Radius = 55;
    public static readonly DependencyProperty XProperty = DependencyProperty.Register(nameof(X), typeof(double), typeof(PositionPad), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Updated));
    public static readonly DependencyProperty YProperty = DependencyProperty.Register(nameof(Y), typeof(double), typeof(PositionPad), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Updated));
    public double X { get => (double)GetValue(XProperty); set => SetValue(XProperty, value); }
    public double Y { get => (double)GetValue(YProperty); set => SetValue(YProperty, value); }
    public PositionPad()
    {
        Width=204; Height=204;
        var root = new Grid(); Content=root;
        var circle = new Ellipse { StrokeThickness=1.5 };
        circle.SetResourceReference(Shape.FillProperty,"SettingsControlSurface");
        circle.SetResourceReference(Shape.StrokeProperty,"SettingsStroke"); root.Children.Add(circle);
        var thumb = new Thumb { Width=44,Height=44,RenderTransform=marker,Cursor=Cursors.SizeAll,Focusable=true };
        thumb.SetResourceReference(StyleProperty,"PositionThumbStyle");
        System.Windows.Automation.AutomationProperties.SetName(thumb,"Position: drag or use arrow keys");
        thumb.ToolTip="Drag to position · Arrow keys to adjust";
        Point origin=new(); double totalX=0,totalY=0;
        thumb.DragStarted+=(_,_)=>{origin=PositionPadMapping.ToDisk(X,Y);totalX=totalY=0;};
        thumb.DragDelta+=(_,e)=>{
            totalX+=e.HorizontalChange;totalY-=e.VerticalChange;
            var p=PositionPadMapping.FromDisk(origin.X+totalX/Radius,origin.Y+totalY/Radius);
            SetCurrentValue(XProperty,p.X);SetCurrentValue(YProperty,p.Y);
        };
        thumb.PreviewKeyDown+=(_,e)=>{
            switch(e.Key) {case Key.Left:Nudge(-.02,0);break;case Key.Right:Nudge(.02,0);break;case Key.Up:Nudge(0,.02);break;case Key.Down:Nudge(0,-.02);break;default:return;} e.Handled=true;
        };
        root.Children.Add(thumb);
        Add("‹","Move left",HorizontalAlignment.Left,VerticalAlignment.Center,-.02,0);
        Add("›","Move right",HorizontalAlignment.Right,VerticalAlignment.Center,.02,0);
        Add("⌃","Move up",HorizontalAlignment.Center,VerticalAlignment.Top,0,.02);
        Add("⌄","Move down",HorizontalAlignment.Center,VerticalAlignment.Bottom,0,-.02);
        void Add(string text,string name,HorizontalAlignment h,VerticalAlignment v,double x,double y) {
            var button=new RepeatButton {Content=text,Width=40,Height=40,HorizontalAlignment=h,VerticalAlignment=v,Delay=350,Interval=65,ToolTip=name};
            button.SetResourceReference(StyleProperty,"PadArrowStyle");
            System.Windows.Automation.AutomationProperties.SetName(button,name);
            button.Click+=(_,_)=>Nudge(x,y);root.Children.Add(button);
        }
    }
    void Nudge(double x,double y) {SetCurrentValue(XProperty,Math.Clamp(X+x,-1,1));SetCurrentValue(YProperty,Math.Clamp(Y+y,-1,1));}
    static void Updated(DependencyObject d,DependencyPropertyChangedEventArgs e) {
        var pad=(PositionPad)d;var p=PositionPadMapping.ToDisk(pad.X,pad.Y);pad.marker.X=p.X*Radius;pad.marker.Y=-p.Y*Radius;
    }
}
