using System.Globalization;
using System.IO;
using System.Resources;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper.Controls;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;
using RadioButton = System.Windows.Controls.RadioButton;
using Panel = System.Windows.Controls.Panel;
using CheckBox = System.Windows.Controls.CheckBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AnimatedWallPaper;

internal static class OceanText
{
    private static readonly ResourceManager Resources = new("AnimatedWallPaper.Resources.Ocean",typeof(OceanText).Assembly);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,System.Text.CompositeFormat> Formats = new();
    public static string Format(string key,object value) => string.Format(CultureInfo.CurrentCulture,Formats.GetOrAdd(T(key),System.Text.CompositeFormat.Parse),value);
    public static string T(string text) => Resources.GetString(text,CultureInfo.CurrentUICulture) ?? text;
}

// Dedicated in-shell editor. The native preview and all WPF controls occupy separate areas.
public sealed class OceanEditorControl : UserControl, IDisposable
{
    private static readonly int[] DayDurations = [10,20,60,120];
    private static readonly float[] WaveRates = [.5f,1f,1.5f];
    private readonly WallpaperPreviewControl _preview = new();
    private readonly AspectRatioDecorator _aspect = new() { MaxHeight=340 };
    private readonly Grid _columns = new();
    private readonly Style _choiceStyle;
    private readonly StackPanel _left = new(), _right = new();
    private readonly TextBlock _status = new(), _renderStatus = new(), _momentLabel = new(), _timeHelp = new(), _cloudHelp = new(), _cloudValue = new(), _fps = new();
    private readonly TextBlock _qualityHelp = new();
    private readonly Button _apply = new(), _all = new(), _discard = new(), _pause = new();
    private readonly Button _retry = new() { Visibility=Visibility.Collapsed };
    private readonly ComboBox _monitor = new() { MinWidth=180, MaxWidth=300, DisplayMemberPath="Label" };
    private readonly ComboBox _duration, _shape, _wind, _fog, _view, _wave, _quality;
    private readonly CheckBox _bloom = new(), _magnification = new();
    private readonly Slider _coverage = new() { Minimum=0, Maximum=100, TickFrequency=1, IsSnapToTickEnabled=true };
    private readonly List<RadioButton> _moments = [], _seas = [], _modes = [];
    private readonly StackPanel _durationPanel = new();
    private readonly DispatcherTimer _editTimer = new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly Image _placeholder = new() { Stretch=Stretch.Uniform };
    private readonly Grid _previewFrame = new();
    private readonly ProgressBar _progress = new() { IsIndeterminate=true, Height=3, Visibility=Visibility.Collapsed };
    private OceanDraft? _draft;
    private string? _deviceId;
    private bool _loading, _userPaused, _suspended=true, _busy, _canApply;
    private int _frameCap=30;
    internal event Action? BackRequested, GeneralSettingsRequested;
    internal event Action<string>? DisplayRequested;
    internal event Action<bool>? ApplyRequested;
    internal sealed record DisplayChoice(string Id,string Label,double Aspect);
    internal OceanDraft? Draft => _draft;
    internal WallpaperPreviewControl Preview => _preview;

    public OceanEditorControl()
    {
        _choiceStyle=(Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="RadioButton">
              <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RadioButton">
                <Border x:Name="ChoiceBorder" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Padding="8,6" CornerRadius="3" BorderThickness="1" BorderBrush="{DynamicResource InputBorderBrush}" Background="{DynamicResource PanelAltBrush}">
                  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsChecked" Value="True"><Setter TargetName="ChoiceBorder" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/><Setter Property="FontWeight" Value="SemiBold"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="ChoiceBorder" Property="BorderThickness" Value="2"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.55"/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
        AccessibleName(_monitor,"Display");
        var root=new Grid(); Content=root;
        root.RowDefinitions.Add(new() { Height=GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height=GridLength.Auto });
        var header=new DockPanel { Margin=new(0,0,0,16), LastChildFill=true };
        var back=ActionButton("Back to library",()=>BackRequested?.Invoke()); DockPanel.SetDock(back,Dock.Right); header.Children.Add(back);
        DockPanel.SetDock(_monitor,Dock.Right); _monitor.Margin=new(12,0,12,0); header.Children.Add(_monitor);
        header.Children.Add(Label("Ocean alive",24)); root.Children.Add(header);
        var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Content=_columns };
        Grid.SetRow(scroll,1); root.Children.Add(scroll);
        _columns.ColumnDefinitions.Add(new()); _columns.ColumnDefinitions.Add(new() { Width=new GridLength(320) });
        _columns.RowDefinitions.Add(new() {Height=GridLength.Auto}); _columns.RowDefinitions.Add(new() {Height=GridLength.Auto});
        _columns.Children.Add(_left); _columns.Children.Add(_right); Grid.SetColumn(_right,1);
        _left.Margin=new(0,0,24,0); _right.Margin=new(0,0,4,0);
        _preview.SetSuspended(true);
        _aspect.Child=_previewFrame; _previewFrame.Children.Add(_placeholder); _previewFrame.Children.Add(_preview);
        _left.Children.Add(_aspect); _left.Children.Add(_progress);
        var playback=new DockPanel { Margin=new(0,8,0,14) };
        _pause.Content=T("Pause preview"); _pause.Click+=(_,_)=>{_userPaused=!_userPaused;_preview.SetUserPaused(_userPaused);RefreshLabels();};
        DockPanel.SetDock(_pause,Dock.Right); playback.Children.Add(_pause); _renderStatus.Text=T("Preparing ocean…"); _renderStatus.TextWrapping=TextWrapping.Wrap; playback.Children.Add(_renderStatus); _left.Children.Add(playback);
        _retry.Content=T("Try again");_retry.Click+=(_,_)=>BindPreview();_left.Children.Add(_retry);
        _momentLabel.FontWeight=FontWeights.SemiBold; _momentLabel.Margin=new(0,0,0,8); _left.Children.Add(_momentLabel);
        var moments=new UniformGrid { Columns=4 }; _left.Children.Add(moments);
        foreach(var moment in Enum.GetValues<OceanMoment>())
        {
            var label=MomentName(moment); var radio=Radio("moment",label,moment);
            var panel=new StackPanel(); var image=new Image { Height=58, Stretch=Stretch.UniformToFill, Margin=new(0,0,0,6) };
            var path=Path.Combine(AppContext.BaseDirectory,"Assets","Wallpapers","ocean",moment.ToString().ToLowerInvariant()+"-state.png");
            if(File.Exists(path)) image.Source=new BitmapImage(new Uri(path));
            panel.Children.Add(image); panel.Children.Add(Label(label)); radio.Content=panel;
            radio.Click+=(_,_)=>{if(!_loading&&_draft is not null){_draft.ChooseMoment(moment);Changed();}};
            _moments.Add(radio); moments.Children.Add(radio);
        }
        _right.Children.Add(Label("Your ocean",18,new(0,0,0,15)));
        _right.Children.Add(Label("Time",16,new(0,0,0,8)));
        var modes=new WrapPanel(); _right.Children.Add(modes);
        foreach(var (label,cycle) in new[]{("Keep this moment",false),("Day cycle",true)})
        {
            var radio=Radio("time",label,cycle); radio.Checked+=(_,_)=>{if(!_loading&&_draft is not null){_draft.SetCycle(cycle);Changed();}}; _modes.Add(radio); modes.Children.Add(radio);
        }
        _timeHelp.TextWrapping=TextWrapping.Wrap; _timeHelp.FontSize=12; _timeHelp.Margin=new(0,8,0,0); _right.Children.Add(_timeHelp);
        _duration=Choice("A day lasts",["10 minutes","20 minutes","1 hour","2 hours"]);
        _durationPanel.Margin=new(0,12,0,0); _durationPanel.Children.Add(Label("A day lasts")); _durationPanel.Children.Add(_duration); _right.Children.Add(_durationPanel);
        _right.Children.Add(Label("Sea",16,new(0,20,0,6)));
        var seas=new WrapPanel(); _right.Children.Add(seas);
        foreach(var (label,amount) in new[]{("Calm",0f),("Moderate",.65f),("Rough",1f)})
        {var radio=Radio("sea",label,amount);radio.Checked+=(_,_)=>Edit(p=>p with{Agitation=amount});_seas.Add(radio);seas.Children.Add(radio);}
        var coverageLabel=new DockPanel {Margin=new(0,20,0,6)}; DockPanel.SetDock(_cloudValue,Dock.Right);coverageLabel.Children.Add(_cloudValue);coverageLabel.Children.Add(Label("Clouds",16)); _right.Children.Add(coverageLabel);
        AccessibleName(_coverage,"Cloud amount"); _right.Children.Add(_coverage);
        var ends=new DockPanel();var many=Label("Many");DockPanel.SetDock(many,Dock.Right);ends.Children.Add(many);ends.Children.Add(Label("None")); _right.Children.Add(ends);
        _shape=Choice("Cloud shape",["In groups","Puffy","In layers"]); Field(_right,"Cloud shape",_shape);
        _cloudHelp.TextWrapping=TextWrapping.Wrap;_cloudHelp.FontSize=12;_right.Children.Add(_cloudHelp);
        var more=new StackPanel(); var expander=new Expander { Header=T("More adjustments"), Content=more, Margin=new(0,22,0,0) };_right.Children.Add(expander);
        expander.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        _wind=Choice("Wind",["No wind","Moderate","Strong"]);Field(more,"Wind",_wind);
        more.Children.Add(Help("Wind changes gradually. Clouds slowly change shape even without wind."));
        _fog=Choice("Mist",["No mist","Sea haze","Low mist","Mist banks"]); Field(more,"Mist",_fog);
        _view=Choice("View",["With horizon","Sea only"]); Field(more,"View",_view);
        _wave=Choice("Wave motion",["Slow","Natural","Fast"]); Field(more,"Wave motion",_wave);
        more.Children.Add(Help("Changes only the motion of the water."));
        _bloom.Content=T("Soft light glow");_magnification.Content=T("Emphasize horizon sun and moon");
        _bloom.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");_magnification.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        _bloom.Margin=_magnification.Margin=new(0,12,0,0); more.Children.Add(_bloom);more.Children.Add(_magnification);
        _quality=Choice("Quality",["Economy","Balanced — recommended","High"]);Field(more,"Quality",_quality);
        _qualityHelp.TextWrapping=TextWrapping.Wrap;_qualityHelp.FontSize=12;more.Children.Add(_qualityHelp);
        _fps.Margin=new(0,12,0,0);more.Children.Add(_fps);more.Children.Add(ActionButton("Playback and power settings",()=>GeneralSettingsRequested?.Invoke()));
        var footer=new Border {BorderThickness=new(0,1,0,0),Padding=new(0,12,0,0),Margin=new(0,12,0,0)};footer.SetResourceReference(Border.BorderBrushProperty,"InputBorderBrush");Grid.SetRow(footer,2);root.Children.Add(footer);
        var footerStack=new StackPanel();footer.Child=footerStack;
        _status.TextWrapping=TextWrapping.Wrap;_status.Margin=new(0,0,0,6);AutomationProperties.SetLiveSetting(_status,AutomationLiveSetting.Polite);footerStack.Children.Add(_status);
        var actions=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};footerStack.Children.Add(actions);
        _discard.Content=T("Discard changes");_discard.Click+=(_,_)=>{_draft?.Discard();BindPreview();LoadValues();};actions.Children.Add(_discard);
        _all.Content=T("Apply to all displays");_all.Click+=(_,_)=>ApplyRequested?.Invoke(true);actions.Children.Add(_all);
        _apply.SetResourceReference(StyleProperty,"PrimaryButtonStyle");_apply.Click+=(_,_)=>ApplyRequested?.Invoke(false);actions.Children.Add(_apply);
        _monitor.SelectionChanged+=(_,_)=>{if(!_loading&&_monitor.SelectedItem is DisplayChoice d)DisplayRequested?.Invoke(d.Id);};
        _coverage.ValueChanged+=(_,_)=>Edit(p=>p with{Coverage=(float)_coverage.Value/100});
        _duration.SelectionChanged+=(_,_)=>Edit(p=>p with{DayMinutes=DayDurations[Math.Max(0,_duration.SelectedIndex)]});
        _shape.SelectionChanged+=(_,_)=>Edit(p=>p with{Clouds=(OceanCloudType)Math.Max(0,_shape.SelectedIndex)});
        _wind.SelectionChanged+=(_,_)=>Edit(p=>p with{Wind=Math.Max(0,_wind.SelectedIndex)*9});
        _fog.SelectionChanged+=(_,_)=>Edit(p=>p with{Fog=(OceanFog)Math.Max(0,_fog.SelectedIndex)});
        _view.SelectionChanged+=(_,_)=>Edit(p=>p with{Horizon=_view.SelectedIndex==0});
        _wave.SelectionChanged+=(_,_)=>Edit(p=>p with{WaveSpeed=WaveRates[Math.Max(0,_wave.SelectedIndex)]});
        _quality.SelectionChanged+=(_,_)=>Edit(p=>p with{Quality=(OceanQuality)Math.Max(0,_quality.SelectedIndex)});
        _bloom.Checked+=(_,_)=>Edit(p=>p with{Bloom=true});_bloom.Unchecked+=(_,_)=>Edit(p=>p with{Bloom=false});
        _magnification.Checked+=(_,_)=>Edit(p=>p with{HorizonMagnification=true});_magnification.Unchecked+=(_,_)=>Edit(p=>p with{HorizonMagnification=false});
        _editTimer.Tick+=(_,_)=>{_editTimer.Stop();_preview.RefreshOcean();};
        _preview.StatusChanged+=PreviewStatus;
        SizeChanged+=(_,_)=>Reflow();
    }

    private static string T(string key)=>OceanText.T(key);
    private static void AccessibleName(DependencyObject control,string label)=>AutomationProperties.SetName(control,T(label));
    private static TextBlock Label(string text,double size=13,Thickness? margin=null)=>new(){Text=T(text),FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=margin??new(0,0,0,6)};
    private static TextBlock Help(string text)=>Label(text,12,new(0,6,0,0));
    private static Button ActionButton(string text,Action action){var b=new Button{Content=T(text)};b.Click+=(_,_)=>action();return b;}
    private RadioButton Radio(string group,string text,object tag)
    {var r=new RadioButton {Style=_choiceStyle,GroupName="Ocean-"+group,Content=T(text),Tag=tag,Margin=new(0,4,8,4),VerticalContentAlignment=VerticalAlignment.Center};AccessibleName(r,text);return r;}
    private static ComboBox Choice(string name,string[] choices){var c=new ComboBox{ItemsSource=choices.Select(T).ToArray(),Margin=new(0,0,0,3)};AccessibleName(c,name);return c;}
    private static void Field(Panel panel,string name,UIElement control){panel.Children.Add(Label(name,13,new(0,14,0,6)));panel.Children.Add(control);}
    private static string MomentName(OceanMoment moment)=>moment switch{OceanMoment.Day=>"Day",OceanMoment.Sunset=>"Sunset",OceanMoment.Moon=>"Moonlight",_=>"Dawn"};
    private void Edit(Func<OceanPreferences,OceanPreferences> edit){if(_loading||_draft is null)return;_draft.Edit(edit(_draft.Preferences));Changed();}
    private void Changed(){LoadValues();_editTimer.Stop();_editTimer.Start();}

    internal void SetDraft(string deviceId,OceanDraft draft,IReadOnlyList<DisplayChoice> displays,int fps)
    {
        var changed=_deviceId!=deviceId||!ReferenceEquals(_draft,draft);
        _deviceId=deviceId;_draft=draft;_frameCap=fps;
        _loading=true;_monitor.ItemsSource=displays;_monitor.SelectedItem=displays.FirstOrDefault(x=>x.Id==deviceId);_loading=false;
        _all.Visibility=displays.Count>1?Visibility.Visible:Visibility.Collapsed;
        _aspect.AspectRatio=displays.FirstOrDefault(x=>x.Id==deviceId)?.Aspect??16d/9;
        if(changed){_userPaused=false;_preview.SetUserPaused(false);BindPreview();}
        LoadValues();
    }
    private void BindPreview()
    {
        if(_draft is null)return;
        var path=Path.Combine(AppContext.BaseDirectory,"Assets","Wallpapers","ocean","preview.png");
        if(File.Exists(path))_placeholder.Source=new BitmapImage(new Uri(path));
        _preview.Visibility=Visibility.Visible;
        _preview.Select(new("ocean",WallpaperKind.Ocean,_frameCap,OceanPreferences:_draft.Preferences,Ocean:_draft.Runtime));
    }
    private void LoadValues()
    {
        if(_draft is null)return;_loading=true;var p=_draft.Preferences;
        foreach(var b in _moments)b.IsChecked=p.Moment==(OceanMoment)b.Tag;
        foreach(var b in _seas)b.IsChecked=Math.Abs(p.Agitation-(float)b.Tag)<.01;
        foreach(var b in _modes)b.IsChecked=p.DayCycle==(bool)b.Tag;
        _duration.SelectedIndex=Array.IndexOf(DayDurations,p.DayMinutes);_coverage.Value=p.Coverage*100;
        _shape.SelectedIndex=(int)p.Clouds;_wind.SelectedIndex=p.Wind==0?0:p.Wind<=9?1:2;_fog.SelectedIndex=(int)p.Fog;
        _view.SelectedIndex=p.Horizon?0:1;_wave.SelectedIndex=p.WaveSpeed<1?0:p.WaveSpeed>1?2:1;_quality.SelectedIndex=(int)p.Quality;
        _bloom.IsChecked=p.Bloom;_magnification.IsChecked=p.HorizonMagnification;_loading=false;RefreshLabels();
    }
    internal void RefreshLabels()
    {
        if(_draft is null)return;var p=_draft.Preferences;
        _momentLabel.Text=T(p.DayCycle?"Start with":p.Moment is null?"Current moment":"Moment");
        _timeHelp.Text=T(p.DayCycle?"Sun and moon follow their paths. Water and clouds keep their own pace.":"The sky stays at this moment. Water and clouds keep moving.");
        _durationPanel.Visibility=p.DayCycle?Visibility.Visible:Visibility.Collapsed;
        _cloudValue.Text=$"{p.Coverage*100:0}%";_shape.IsEnabled=p.Coverage>0;
        _cloudHelp.Text=T(p.Coverage==0?"Increase the amount to see clouds.":"Visible coverage also depends on the shape and view.");
        _qualityHelp.Text=T(p.Quality switch{OceanQuality.Economy=>"Lower consumption, with less detail.",OceanQuality.High=>"More detail; requires more GPU power.",_=>"Balance between detail and consumption."});
        _pause.Content=T(_userPaused?"Resume preview":"Pause preview");
        if(_userPaused && !_suspended)_renderStatus.Text=T("Preview paused");
        _fps.Text=OceanText.Format("HYPNIX limit: {0} FPS",_frameCap);
        _apply.Content=_busy?T("Applying…"):OceanText.Format("Apply to {0}",(_monitor.SelectedItem as DisplayChoice)?.Label??T("display"));
        _apply.IsEnabled=_all.IsEnabled=_canApply&&!_busy;
        _discard.IsEnabled=_draft.IsDirty&&!_busy;
        _status.Text=T(_busy?"Preparing the ocean. Your current wallpaper is kept until it is ready.":_draft.IsDirty?"Changes are in the preview only. Apply when ready.":"Preview only · your desktop changes when you apply.");
    }
    internal void SetApplyState(bool allowed,bool busy){_canApply=allowed;_busy=busy;RefreshLabels();}
    internal void SetResult(string message){RefreshLabels();_status.Text=message;}
    internal void SetSuspended(bool suspended,string? reason=null)
    {
        _suspended=suspended;_preview.SetSuspended(suspended);
        if(suspended)_renderStatus.Text=reason??T("Preview paused");
    }
    internal void SetFrameCap(int fps){_frameCap=fps;_preview.SetFrameCap(fps);RefreshLabels();}
    private void PreviewStatus(string status)
    {
        if(status=="Preparing preview…") {_retry.Visibility=Visibility.Collapsed;_progress.Visibility=Visibility.Visible;_renderStatus.Text=T("Preparing ocean…");}
        else if(status=="Live preview") {_progress.Visibility=Visibility.Collapsed;_renderStatus.Text=T(_userPaused?"Preview paused":"Live preview");}
        else {_retry.Visibility=Visibility.Visible;_progress.Visibility=Visibility.Collapsed;_renderStatus.Text=T("Preview unavailable. Reopen the ocean to retry.");_status.Text=status;}
    }
    private void Reflow()
    {
        var narrow=ActualWidth<940;
        _columns.ColumnDefinitions[1].Width=narrow?new GridLength(0):new GridLength(310);
        Grid.SetRow(_right,narrow?1:0);Grid.SetColumn(_right,narrow?0:1);
        _left.Margin=new(0,0,narrow?4:24,20);_right.Margin=new(0,narrow?8:0,4,0);
        _aspect.MaxHeight=ActualHeight<550?230:340;
    }
    public void Dispose(){_editTimer.Stop();_preview.Dispose();}
}
