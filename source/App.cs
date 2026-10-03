using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Prism;

public class App : Application
{
    [STAThread] public static void Main(string[] args)
    {
        bool offline=args.Contains("--offline-selftest");
        bool diagnostic=args.Contains("--preview")||args.Contains("--selftest")||offline,created=true;
        string identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        using var instance=diagnostic?null:new System.Threading.Mutex(true,"Local\\PrismWidget_"+identity,out created);
        if(!created){ActivateExisting(identity);return;}
        var app = new App();
        L.Set(diagnostic?"en":WidgetPreferences.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"data","preferences.json")).Language);
        app.DispatcherUnhandledException += (_,e) => { try { File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"error.log"),DateTime.UtcNow.ToString("O")+" "+e.Exception.GetType().Name+Environment.NewLine); } catch {} e.Handled=true;if(diagnostic)app.Shutdown(1); };
        app.Run(new Widget(args.Contains("--preview"), args.Contains("--selftest")||offline,offline,args.Contains("--tray")));
    }
    static void ActivateExisting(string identity)
    {
        TrayIcon.PostMessage(new IntPtr(0xffff),TrayIcon.RegisterWindowMessage("Prism.Show."+identity),IntPtr.Zero,IntPtr.Zero);
        foreach(var process in Process.GetProcessesByName("Prism"))using(process)try{
            if(process.Id!=Environment.ProcessId&&process.MainModule?.FileName==Environment.ProcessPath&&process.MainWindowHandle!=IntPtr.Zero){ShowWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);return;}
        }catch{}
    }
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
}

public sealed partial class Widget : Window
{
    static readonly Color Ink=Color.FromRgb(248,251,255), Muted=Color.FromRgb(185,205,225);
    static readonly string Root=AppContext.BaseDirectory;
    static readonly string Data=System.IO.Path.Combine(Root,"data");
    readonly StackPanel body=new();
    readonly StackPanel expandedBody=new(),metricBody=new();
    readonly UniformGrid metricGrid=new(){Columns=2};
    readonly Dictionary<string,MetricTile> metricTiles=new();
    Grid metricHeader=null!;
    bool metricsOnly;
    ResetReminders resetReminders=new();
    readonly Dictionary<string,QuotaSnapshot> latestReadings=new();
    readonly Popup reminderPopup=new(){AllowsTransparency=true,StaysOpen=true,Placement=PlacementMode.Bottom,PopupAnimation=PopupAnimation.Fade};
    readonly DispatcherTimer reminderTimer=new(){Interval=TimeSpan.FromSeconds(12)};
    ClockPreferences clockPreferences=new();
    readonly TextBlock firstTime=Text("—",19),secondTime=Text("—",19),firstZoneLabel=Text("",10,Muted),secondZoneLabel=Text("",10,Muted),compactTimes=Text("",12);
    Grid worldClockRow=null!;
    Border firstClock=null!,secondClock=null!;
    StackPanel compactBrand=null!;
    bool narrowClocks;
    double expandedHeight=858;
    readonly UniformGrid providerGrid=new(){Columns=1};
    readonly Viewbox fittedBody=new(){Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
    Border frame=null!,cpuPanel=null!,ramPanel=null!;
    Grid greeting=null!,extras=null!,section=null!,bottom=null!,systemMetrics=null!;
    StackPanel diskStack=null!,netStack=null!;
    TextBlock sectionLabel=null!;
    ActionRow fullActions=null!,compactActions=null!;
    Grid fullHeader=null!;
    TextBlock tagline=null!,hint=null!,networkNote=null!;
    Sparkline cpuGraph=null!,ramGraph=null!;
    bool adapting;
    int density;
    readonly TextBlock cpuValue=Text("—",38), ramValue=Text("—",38), ramDetail=Text("Reading memory",11,Muted), diskValue=Text("—",14), netValue=Text("—",14);
    readonly TextBlock clock=Text("",21), footer=Text("Connecting to your workspace",10,Muted);


    readonly Meter ramBar=Bar("#C2ADFF"), diskBar=Bar("#91C6FF");
    readonly Dictionary<string,ProviderCard> cards=new();
    readonly List<double> cpuHistory=new(), ramHistory=new();
    readonly UsageGauge cpuGauge=new("#6AF5E2"),ramGauge=new("#C2ADFF");
    readonly DispatcherTimer systemTimer=new(){Interval=TimeSpan.FromSeconds(2)}, providerTimer=new(){Interval=TimeSpan.FromSeconds(5)};
    readonly SystemCapacity hardware=new();
    HardwareSnapshot? hardwareFixture;bool systemChecks;
    bool busy,closed,compact;
    bool cpuReady,memoryReady,diskReady;
    Process? collector;
    readonly System.Threading.CancellationTokenSource lifetime=new();
    readonly List<Button> refreshButtons=new();
    readonly bool preview,selftest,offline;
    StackPanel systems=null!;
    Button pin=null!;
    Button metricPin=null!;
    JsonElement providers;
    public Widget(bool preview,bool selftest,bool offline=false,bool startInTray=false)
    {
        this.preview=preview; this.selftest=selftest;this.offline=offline;
        preferences=preview||selftest?new():WidgetPreferences.Load(System.IO.Path.Combine(Data,"preferences.json"));
        if(!preview&&!selftest){clockPreferences=ClockPreferences.Load(System.IO.Path.Combine(Data,"clocks.json"));resetReminders=ResetReminders.Load(System.IO.Path.Combine(Data,"reset-reminders.json"));}
        Directory.CreateDirectory(Data);
        Title="Prism · workspace pulse"; Width=442; Height=858; WindowStyle=WindowStyle.None;
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/prism.ico"));
        AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.CanResize;
        MinWidth=340; MinHeight=220; MaxWidth=SystemParameters.WorkArea.Width;
        UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI"); Foreground=new SolidColorBrush(Ink);
        Resources.Add(typeof(ScrollBar),(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
          <Setter Property="Width" Value="8"/><Setter Property="Background" Value="#162235"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
            <Border Background="{TemplateBinding Background}" CornerRadius="4"><Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" IsDirectionReversed="True" Orientation="Vertical">
              <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#536780" CornerRadius="4" Margin="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track></Border>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));
        WindowStartupLocation=WindowStartupLocation.Manual;
        Left=SystemParameters.WorkArea.Right-Width-24; Top=SystemParameters.WorkArea.Top+24;
        MaxHeight=SystemParameters.WorkArea.Height-24;
        Build();InitializeFeatures(startInTray);
        if(!offline)LoadPosition();
        reminderPopup.PlacementTarget=frame;reminderTimer.Tick+=(_,_)=>DismissReminder();
        SizeChanged+=(_,_)=>AdaptLayout();
        systemTimer.Tick+=(_,_)=>UpdateSystem(); providerTimer.Tick+=async (_,_)=>await UpdateProviders();
        Loaded+=async (_,_)=>{
            UpdateSystem(); systemTimer.Start(); providerTimer.Start(); await UpdateProviders(true);
            if(preview || selftest) {
                await System.Threading.Tasks.Task.Delay(5000); UpdateSystem();
                if(selftest){systemChecks=SystemCapacityChecks.Verify();forecastChecks=ForecastChecks.Verify(System.IO.Path.Combine(Root,"build","test-tmp"));featureChecks=await FeatureChecks.Verify(System.IO.Path.Combine(Root,"build","test-tmp"));RunSelfTest();}
                SavePreview(); Close();
            }
        };
        Closed+=(_,_)=>{closed=true;DismissReminder();trayView?.Close();tray?.Dispose();taskbarPreview?.Dispose();hardware.Dispose(); systemTimer.Stop();providerTimer.Stop(); lifetime.Cancel();StopCollector();if(!preview&&!selftest)SavePosition();};
        KeyDown+=async (_,e)=>{
            if(e.Key==Key.F5){await UpdateProviders(true);e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.P){SetPinned(!Topmost);e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.M){ToggleCompact();e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.OemComma){ShowPreferences();e.Handled=true;}
            else if(e.Key==Key.Escape)Close();
        };
    }
    static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    static TextBlock Text(string value,double size=12,Color? color=null){var text=new TextBlock{Text=L.T(value),FontSize=size,Foreground=new SolidColorBrush(color??Ink),TextWrapping=TextWrapping.Wrap,FontWeight=size>=18?FontWeights.Medium:FontWeights.Normal};if(L.Known(value))text.SetResourceReference(TextBlock.TextProperty,"t."+value);return text;}
    static Meter Bar(string color)=>new(){Height=4,Foreground=Brush(color),Background=Brush("#26354A"),Margin=new Thickness(0,9,0,0)};
    static Button Button(string text,string tip,Action click)
    {
        var b=new Button {Content=L.T(text),ToolTip=L.T(tip),Foreground=new SolidColorBrush(Ink),Background=Brush("#253A54"),BorderBrush=Brush("#657E9F"),BorderThickness=new Thickness(1),Padding=new Thickness(9,5,9,5),Cursor=Cursors.Hand,FontSize=12,Margin=new Thickness(4,0,0,0)};
        if(L.Known(text))b.SetResourceReference(ContentControl.ContentProperty,"t."+text);
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent}); border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetBinding(FrameworkElement.MarginProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        border.AppendChild(content); template.VisualTree=border; b.Template=template;
        System.Windows.Automation.AutomationProperties.SetName(b,tip);ToolTipService.SetInitialShowDelay(b,250);
        b.Click+=(_,_)=>click();b.MouseEnter+=(_,_)=>b.Background=Brush("#3B5778");b.MouseLeave+=(_,_)=>b.Background=Brush("#253A54");return b;
    }
    static Border Panel(UIElement child,string color="#85324A66",int radius=18)=>new(){Background=new LinearGradientBrush(Brush(color).Color,Color.FromArgb(95,30,37,64),70),BorderBrush=Brush("#68869EBE"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(radius),Padding=new Thickness(16),Child=child};
    void SetPinned(bool value){Topmost=value;pin.Content=metricPin.Content=value?"◆":"◇";}
    sealed class ActionRow
    {
        public StackPanel Element {get;}=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        public List<Button> Buttons {get;}=new();
        readonly List<TextBlock> labels=new();
        public bool IconsOnly {get;private set;}
        public Button Add(string label,string icon,string tip,Action action)
        {
            var button=Button("",tip,action);var content=new StackPanel{Orientation=Orientation.Horizontal};
            content.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(icon),Stroke=new SolidColorBrush(Ink),StrokeThickness=1.3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center});
            var text=Text(label,12);text.TextWrapping=TextWrapping.NoWrap;text.Margin=new Thickness(6,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;content.Children.Add(text);labels.Add(text);
            button.Content=content;button.IsEnabledChanged+=(_,_)=>button.Opacity=button.IsEnabled?1:.45;
            Buttons.Add(button);Element.Children.Add(button);return button;
        }
        public void SetIconsOnly(bool value){IconsOnly=value;foreach(var label in labels)label.Visibility=value?Visibility.Collapsed:Visibility.Visible;}
    }
    ActionRow CreateActions(bool iconsOnly)
    {
        var row=new ActionRow();
        row.Add("Free RAM","M3,4 L13,4 L13,12 L3,12 Z M5,6 L5,10 M8,6 L8,10 M11,6 L11,10 M1,6 L3,6 M1,10 L3,10 M13,6 L15,6 M13,10 L15,10","Free RAM · trim selected apps without closing them",ShowMemory);
        refreshButtons.Add(row.Add("Refresh","M13,5 A5.5,5.5 0 1 0 13,11 M13,1 L13,5 L9,5","Refresh AI readings · F5",async()=>await UpdateProviders(true)));
        row.Add("Settings","M2,4 L14,4 M2,8 L14,8 M2,12 L14,12 M6,2 L6,6 M10,6 L10,10 M6,10 L6,14","Settings · accounts, clocks and reminders",ShowPreferences);
        row.SetIconsOnly(iconsOnly);return row;
    }
    Button FitHeightButton()
    {
        var button=Button("","Fit height · fit the widget to its contents",FitHeight);
        button.Content=new System.Windows.Shapes.Path{Data=Geometry.Parse("M2,1 L14,1 M2,15 L14,15 M8,4 L8,12 M5,7 L8,4 L11,7 M5,9 L8,12 L11,9"),Stroke=new SolidColorBrush(Ink),StrokeThickness=1.3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center};
        return button;
    }
    void RestoreDetails(){compact=false;Height=Math.Clamp(expandedHeight,Math.Min(640,MaxHeight),MaxHeight);AdaptLayout();}
    void ApplyPreset(double width,double height)
    {
        compact=false;Width=Math.Clamp(width,MinWidth,MaxWidth);MinHeight=Width>=1002?120:Width>=522?165:220;
        Height=Math.Clamp(height,MinHeight,MaxHeight);if(Height>=640)expandedHeight=Height;
        Left=Math.Clamp(Left,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width);
        Top=Math.Clamp(Top,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height);
        AdaptLayout();
    }
    Button MenuButton()
    {
        var button=Button("···","More controls",()=>{});
        button.Click+=(_,_)=>{
            var menu=new ContextMenu{Background=Brush("#162235"),Foreground=new SolidColorBrush(Ink),BorderBrush=Brush("#486079"),Padding=new Thickness(6)};
            void Item(string label,string shortcut,Action action){var item=new MenuItem{Header=L.T(label),InputGestureText=shortcut,Padding=new Thickness(10,6,10,6)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Item(Topmost?"Unpin widget":"Keep on top","Ctrl+P",()=>SetPinned(!Topmost));
            Item("Fit to content","",FitHeight);Item("Toggle compact view","Ctrl+M",ToggleCompact);
            menu.Items.Add(new Separator());
            Item("Dashboard · 442 × 800","",()=>ApplyPreset(442,800));
            Item("Focus · 560 × 220","",()=>ApplyPreset(560,220));
            Item("Ribbon · 1200 × 120","",()=>ApplyPreset(1200,120));
            menu.Items.Add(new Separator());Item("Free RAM","",ShowMemory);Item("Settings","Ctrl+,",ShowPreferences);Item("Connections…","",()=>ShowConnection("cursor","Cursor"));
            Item("System capacity","",ShowSystemCapacity);Item("Minimize to taskbar","",MinimizeToTaskbar);Item("Taskbar ribbon","",DockRibbon);Item("Capacity & resets","",ShowCapacity);Item("Usage history","",ShowHistory);if(tray is not null)Item("Hide to tray","",HideToTray);
            Item("World clocks…","",ShowClocks);Item("Reset reminders…","",ShowResetReminders);
            Item("Diagnostics…","",async()=>await ShowDiagnostics());
            menu.Items.Add(new Separator());Item("Close Prism","Esc",Close);
            menu.PlacementTarget=button;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;
        };return button;
    }
    static Grid Columns(params double[] widths)
    { var g=new Grid();foreach(var w in widths)g.ColumnDefinitions.Add(new(){Width=w<0?new GridLength(-w,GridUnitType.Star):new GridLength(w)});return g; }
    static void Add(Grid grid,UIElement el,int col){Grid.SetColumn(el,col);grid.Children.Add(el);}
    StackPanel Brand(bool small)
    {
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var logo=new Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/prism-icon.png")),Width=small?20:28,Height=small?20:28,Margin=new Thickness(0,0,small?5:9,0)};RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality);brand.Children.Add(logo);
        var text=Text(small?"PRISM":"P R I S M",small?13:20);text.FontWeight=FontWeights.SemiBold;text.VerticalAlignment=VerticalAlignment.Center;brand.Children.Add(text);
        brand.MouseLeftButtonDown+=(_,_)=>DragMove();return brand;
    }
    void Build()
    {
        var outer=new Border{CornerRadius=new CornerRadius(25),Margin=new Thickness(10),Padding=new Thickness(21),BorderThickness=new Thickness(1)};frame=outer;
        outer.Background=Brushes.Transparent;
        outer.BorderBrush=new LinearGradientBrush(Brush("#D4C1F5FF").Color,Brush("#688796CC").Color,45);
        fittedBody.Child=body;outer.Child=fittedBody;
        var surface=new Grid();
        surface.Children.Add(new Border{CornerRadius=new CornerRadius(25),Margin=new Thickness(10),Background=Brush("#182538"),IsHitTestVisible=false,Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=18,ShadowDepth=4,Opacity=.4}});
        surface.Children.Add(new GlassSurface{Margin=new Thickness(10)});
        surface.Children.Add(outer);Content=surface;
        var grip=new Thumb{Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,15,15),Cursor=Cursors.SizeNWSE,ToolTip="Drag to resize · double-click to fit height"};
        var gripTemplate=new ControlTemplate(typeof(Thumb));var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetValue(TextBlock.TextProperty,"◢");glyph.SetValue(TextBlock.ForegroundProperty,Brush("#889AB5"));glyph.SetValue(TextBlock.FontSizeProperty,18d);gripTemplate.VisualTree=glyph;grip.Template=gripTemplate;
        grip.DragDelta+=(_,e)=>{Width=Math.Clamp(ActualWidth+e.HorizontalChange,MinWidth,MaxWidth);Height=Math.Clamp(ActualHeight+e.VerticalChange,MinHeight,MaxHeight);};
        grip.MouseDoubleClick+=(_,_)=>FitHeight();surface.Children.Add(grip);
        var header=Columns(-1,0);fullHeader=header;header.ColumnDefinitions[1].Width=GridLength.Auto;
        var brand=new StackPanel();brand.Children.Add(Brand(false));
        tagline=Text("SYSTEMS  /  INTELLIGENCE",9,Muted);brand.Children.Add(tagline); Add(header,brand,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        pin=Button("◇","Keep on top",()=>{Topmost=!Topmost;pin.Content=Topmost?"◆":"◇";metricPin.Content=pin.Content;});actions.Children.Add(pin);
        actions.Children.Add(Button("−","Compact / expanded",()=>ToggleCompact()));
        actions.Children.Add(FitHeightButton());
        actions.Children.Add(MenuButton());Add(header,actions,1);
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==header)DragMove();};expandedBody.Children.Add(header);
        greeting=Columns(-1,100); greeting.Margin=new Thickness(0,25,0,19);
        var greetingText=new StackPanel();greetingText.Children.Add(Text("Workspace pulse",23));greetingText.Children.Add(Text("Live system health. AI capacity in view.",11,Muted));Add(greeting,greetingText,0);
        clock.HorizontalAlignment=HorizontalAlignment.Right;clock.FontWeight=FontWeights.Light;Add(greeting,clock,1);expandedBody.Children.Add(greeting);
        worldClockRow=Columns(-1,8,-1);worldClockRow.Margin=new Thickness(0,0,0,12);
        Border ClockTile(TextBlock label,TextBlock time,string color){
            var row=Columns(-1,72);label.VerticalAlignment=VerticalAlignment.Center;label.TextTrimming=TextTrimming.CharacterEllipsis;label.TextWrapping=TextWrapping.NoWrap;
            time.Foreground=Brush(color);time.HorizontalAlignment=HorizontalAlignment.Right;time.TextWrapping=TextWrapping.NoWrap;Add(row,label,0);Add(row,time,1);
            var tile=Panel(row,"#75263853",12);tile.Padding=new Thickness(10,7,10,7);tile.Cursor=Cursors.Hand;tile.MouseLeftButtonUp+=(_,_)=>ShowClocks();tile.Focusable=true;
            tile.KeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space){ShowClocks();e.Handled=true;}};return tile;
        }
        firstClock=ClockTile(firstZoneLabel,firstTime,"#6AF5E2");secondClock=ClockTile(secondZoneLabel,secondTime,"#C2ADFF");
        Add(worldClockRow,firstClock,0);Add(worldClockRow,secondClock,2);expandedBody.Children.Add(worldClockRow);
        systems=new StackPanel();expandedBody.Children.Add(systems);
        var metrics=Columns(-1,12,-1);systemMetrics=metrics;
        var cpu=new StackPanel();cpu.Children.Add(Text("PROCESSOR",10,Muted));var cpuReadout=Columns(-1,48);Add(cpuReadout,cpuValue,0);Add(cpuReadout,cpuGauge,1);cpu.Children.Add(cpuReadout);cpu.Children.Add(Text($"{Environment.ProcessorCount} logical cores",11,Muted));cpuGraph=new Sparkline("#6AF5E2"){Height=44,Margin=new Thickness(0,9,0,0)};cpu.Children.Add(cpuGraph);
        var ram=new StackPanel();ram.Children.Add(Text("MEMORY",10,Muted));var ramReadout=Columns(-1,48);Add(ramReadout,ramValue,0);Add(ramReadout,ramGauge,1);ram.Children.Add(ramReadout);ram.Children.Add(ramDetail);ramGraph=new Sparkline("#C2ADFF"){Height=44,Margin=new Thickness(0,9,0,0)};ram.Children.Add(ramGraph);
        cpuPanel=Panel(cpu);ramPanel=Panel(ram,"#80534379");Add(metrics,cpuPanel,0);Add(metrics,ramPanel,2);systems.Children.Add(metrics);
        extras=Columns(-1,18,-1);extras.Margin=new Thickness(0,14,0,18);
        var disk=new StackPanel();diskStack=disk;disk.Children.Add(Text("SYSTEM DRIVE",9,Muted));diskValue.Margin=new Thickness(0,5,0,0);disk.Children.Add(diskValue);disk.Children.Add(diskBar);
        var net=new StackPanel();netStack=net;net.Children.Add(Text("NETWORK · ↓ / ↑",9,Muted));netValue.Margin=new Thickness(0,5,0,0);net.Children.Add(netValue);networkNote=Text("Active physical adapters",9,Muted);net.Children.Add(networkNote);Add(extras,disk,0);Add(extras,net,2);systems.Children.Add(extras);
        section=Columns(-1,0);section.ColumnDefinitions[1].Width=GridLength.Auto;section.Margin=new Thickness(0,3,0,10);var label=Text("AI CAPACITY",10,Muted);sectionLabel=label;label.TextWrapping=TextWrapping.NoWrap;label.VerticalAlignment=VerticalAlignment.Center;Add(section,label,0);
        fullActions=CreateActions(false);Add(section,fullActions.Element,1);expandedBody.Children.Add(section);
        foreach(var spec in new[]{("codex","Codex","⌘","#6AF5E2"),("cursor","Cursor","↗","#A9D2FF"),("opencode","OpenCode Go","▣","#C2ADFF"),("claude","Claude","✳","#FFC39F")})
        {
            var card=new ProviderCard(spec.Item1,spec.Item2,spec.Item3,spec.Item4,()=>ShowConnection(spec.Item1,spec.Item2));
            cards[spec.Item1]=card;providerGrid.Children.Add(card.Element);
        }
        expandedBody.Children.Add(providerGrid);
        bottom=Columns(-1);bottom.Margin=new Thickness(0,10,0,0);footer.VerticalAlignment=VerticalAlignment.Center;Add(bottom,footer,0);expandedBody.Children.Add(bottom);
        hint=Text("System live · AI readings carry their own timestamp",9,Muted);hint.Margin=new Thickness(0,9,0,0);expandedBody.Children.Add(hint);
        body.Children.Add(expandedBody);
        BuildMetricBody();body.Children.Add(metricBody);
    }
    void BuildMetricBody()
    {
        metricHeader=Columns(-1,0);metricHeader.ColumnDefinitions[1].Width=GridLength.Auto;
        var headerInfo=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        compactBrand=Brand(true);headerInfo.Children.Add(compactBrand);compactTimes.Margin=new Thickness(8,0,0,0);compactTimes.VerticalAlignment=VerticalAlignment.Center;
        compactTimes.TextWrapping=TextWrapping.NoWrap;compactTimes.Cursor=Cursors.Hand;compactTimes.MouseLeftButtonUp+=(_,_)=>ShowClocks();headerInfo.Children.Add(compactTimes);Add(metricHeader,headerInfo,0);
        metricHeader.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==metricHeader)DragMove();};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        compactActions=CreateActions(true);actions.Children.Add(compactActions.Element);
        metricPin=Button("◇","Toggle keep on top",()=>SetPinned(!Topmost));
        actions.Children.Add(FitHeightButton());
        actions.Children.Add(MenuButton());
        Add(metricHeader,actions,1);
        metricHeader.Margin=new Thickness(0,0,0,6);metricBody.Children.Add(metricHeader);
        foreach(var spec in new[]{("cpu","CPU","▤","#6AF5E2"),("ram","Memory","▥","#C2ADFF"),("disk","Free disk","◴","#91C6FF"),("net","Network download / upload","↕","#9BCCD7"),("codex","Codex","⌘","#6AF5E2"),("cursor","Cursor","↗","#A9D2FF"),("opencode","OpenCode Go","▣","#C2ADFF"),("claude","Claude","✳","#FFC39F")}){
            var tile=new MetricTile(spec.Item2,spec.Item3,spec.Item4);metricTiles[spec.Item1]=tile;metricGrid.Children.Add(tile.Element);
            if(cards.ContainsKey(spec.Item1)){
                tile.Element.Cursor=Cursors.Hand;tile.Element.MouseLeftButtonUp+=(_,_)=>ShowConnection(spec.Item1,spec.Item2);
                tile.Element.KeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space)ShowConnection(spec.Item1,spec.Item2);};
            }
        }
        foreach(string id in WidgetPreferences.ExtraMetricIds){var tile=new MetricTile(MetricName(id),id=="gpu"?"▧":id=="battery"?"▰":id=="read"?"↓":id=="write"?"↑":id=="frequency"?"≋":"▥","#9FCFFF");metricTiles[id]=tile;tile.Element.Cursor=Cursors.Hand;tile.Element.MouseLeftButtonUp+=(_,_)=>ShowSystemCapacity();tile.Element.KeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space)ShowSystemCapacity();};}
        metricBody.Children.Add(metricGrid);metricBody.Children.Add(emptyMetrics);
    }
    void SyncMetricValues()
    {
        metricTiles["cpu"].Set(cpuValue.Text,$"CPU · {cpuValue.Text} · {Environment.ProcessorCount} logical cores",cpuReady&&cpuHistory.Count>0?cpuHistory[^1]:null,cpuHistory,health:cpuReady?"ready":"unknown");
        metricTiles["ram"].Set(ramValue.Text,$"Memory · {ramValue.Text} · {ramDetail.Text}",memoryReady?ramBar.Value:null,ramHistory,health:memoryReady?"ready":"unknown");
        metricTiles["disk"].Set(diskReady?$"{freeDiskGb:0.0} GB":"—",L.T("Free disk")+" · "+diskValue.Text,diskReady?diskBar.Value:null,health:diskReady?"ready":"unknown");
        metricTiles["net"].Set(netValue.Text.Replace(" KB/s","K").Replace(" MB/s","M"),"Network download / upload · "+netValue.Text,null,health:netValue.Text!="—"?"ready":"unknown");
        SyncHardwareTiles();
        foreach(var (key,card) in cards)metricTiles[key].Set(card.CompactValue,card.Element.ToolTip?.ToString()??key,card.Remaining,health:card.Health);
    }
    void AdaptLayout()
    {
        if(adapting||cards.Count!=4)return;adapting=true;
        try{
            double width=ActualWidth>0?ActualWidth:Width,height=ActualHeight>0?ActualHeight:Height;
            MinHeight=MinimumMetricHeight(width);
            worldClockRow.Visibility=clockPreferences.Enabled?Visibility.Visible:Visibility.Collapsed;
            compactTimes.Visibility=clockPreferences.Enabled?Visibility.Visible:Visibility.Collapsed;
            narrowClocks=width<650;compactBrand.Visibility=clockPreferences.Enabled&&narrowClocks?Visibility.Collapsed:Visibility.Visible;
            UpdateClocks();
            metricPin.Content=Topmost?"◆":"◇";
            metricsOnly=compact||height<640||metricGrid.Children.Count==0||preferences.VisibleMetrics.Any(WidgetPreferences.ExtraMetricIds.Contains);
            expandedBody.Visibility=metricsOnly?Visibility.Collapsed:Visibility.Visible;
            metricBody.Visibility=metricsOnly?Visibility.Visible:Visibility.Collapsed;
            if(metricsOnly){
                density=3;frame.Padding=new Thickness(10);double availableWidth=width-42;
                body.Width=Math.Max(1,availableWidth);metricGrid.Columns=Math.Max(1,Math.Min(metricGrid.Children.Count,availableWidth>=960?8:availableWidth>=480?4:2));
                emptyMetrics.Visibility=metricGrid.Children.Count==0?Visibility.Visible:Visibility.Collapsed;
                bool tileTight=height<280||height<400&&metricGrid.Children.Count>8;
                bool charts=height>=340&&availableWidth/metricGrid.Columns>=130;
                foreach(var tile in metricTiles.Values)tile.Arrange(availableWidth/metricGrid.Columns,charts,tileTight,height>=400);
                SyncMetricValues();
                metricGrid.InvalidateMeasure();metricBody.InvalidateMeasure();body.InvalidateMeasure();
                body.Measure(new Size(availableWidth,double.PositiveInfinity));
                // A UniformGrid gives every row the chart row's height. Check the whole grid,
                // not just the window threshold, before enabling charts in a narrow layout.
                if(charts&&body.DesiredSize.Height>height-42){
                    foreach(var tile in metricTiles.Values)tile.Arrange(availableWidth/metricGrid.Columns,false,tileTight,height>=400);
                    SyncMetricValues();metricGrid.InvalidateMeasure();metricBody.InvalidateMeasure();body.InvalidateMeasure();body.Measure(new Size(availableWidth,double.PositiveInfinity));
                }
                if(body.DesiredSize.Height>height-42){foreach(var tile in metricTiles.Values)tile.Arrange(availableWidth/metricGrid.Columns,false,true,false);SyncMetricValues();metricGrid.InvalidateMeasure();metricBody.InvalidateMeasure();body.InvalidateMeasure();body.Measure(new Size(availableWidth,double.PositiveInfinity));}
                return;
            }
            for(int level=compact?2:0;level<=2;level++){
                density=level;bool dense=level==2,tight=level>0;
                double padding=dense?12:tight?16:21;frame.Padding=new Thickness(padding);
                double availableWidth=width-22-padding*2,availableHeight=height-22-padding*2;
                body.Width=Math.Max(1,availableWidth);
                providerGrid.Columns=availableWidth>=1100?4:availableWidth>=690||dense?2:1;
                greeting.Visibility=dense?Visibility.Collapsed:Visibility.Visible;
                tagline.Visibility=tight?Visibility.Collapsed:Visibility.Visible;
                hint.Visibility=tight?Visibility.Collapsed:Visibility.Visible;
                networkNote.Visibility=tight?Visibility.Collapsed:Visibility.Visible;
                firstClock.Padding=secondClock.Padding=new Thickness(tight?8:10,tight?5:7,tight?8:10,tight?5:7);
                firstTime.FontSize=secondTime.FontSize=tight?17:19;worldClockRow.Margin=new Thickness(0,0,0,tight?8:12);
                greeting.Margin=new Thickness(0,tight?10:25,0,tight?10:19);
                systems.Margin=new Thickness(0,dense?8:0,0,0);
                cpuValue.FontSize=ramValue.FontSize=dense?26:tight?32:38;
                cpuPanel.Padding=ramPanel.Padding=new Thickness(dense?8:tight?11:16);
                cpuGraph.Height=ramGraph.Height=dense?12:tight?30:44;
                cpuGauge.Width=cpuGauge.Height=ramGauge.Width=ramGauge.Height=dense?28:tight?36:44;
                cpuGraph.Margin=ramGraph.Margin=new Thickness(0,tight?3:9,0,0);
                extras.Margin=new Thickness(0,tight?6:14,0,tight?6:18);
                diskValue.FontSize=netValue.FontSize=tight?12:14;
                diskValue.Margin=netValue.Margin=new Thickness(0,tight?2:5,0,0);
                diskBar.Margin=new Thickness(0,tight?4:9,0,0);
                fullActions.SetIconsOnly(width<430||height<760);fullActions.Element.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));sectionLabel.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                if(fullActions.Element.DesiredSize.Width+sectionLabel.DesiredSize.Width+8>availableWidth)fullActions.SetIconsOnly(true);
                section.Margin=new Thickness(0,3,0,tight?4:10);
                bottom.Margin=new Thickness(0,tight?3:10,0,0);
                foreach(var card in cards.Values)card.SetDensity(level,availableWidth/providerGrid.Columns);
                body.Measure(new Size(availableWidth,double.PositiveInfinity));
                if(dense&&!compact&&availableHeight-body.DesiredSize.Height>=80){
                    greeting.Visibility=Visibility.Visible;
                    body.Measure(new Size(availableWidth,double.PositiveInfinity));
                }
                if(body.DesiredSize.Height<=availableHeight)break;
            }
            cpuGraph.SetSamples(cpuHistory);ramGraph.SetSamples(ramHistory);
        }finally{adapting=false;}
    }
    void FitHeight(){AdaptLayout();Height=Math.Clamp(body.DesiredSize.Height+22+frame.Padding.Top*2,metricsOnly?MinHeight:Math.Max(MinHeight,640),MaxHeight);}
    void ToggleCompact(){if(compact||metricsOnly){RestoreDetails();}else{expandedHeight=Height;compact=true;AdaptLayout();FitHeight();}}
    void LoadPosition()
    {
        // Restore once an HWND exists, so work areas use the window's actual DPI.
        SourceInitialized+=(_,_)=>{
            try{
                var saved=LocalJson.Load<WindowPlacement>(System.IO.Path.Combine(Data,"window.json"),4096);if(saved is null)return;
                var displays=DesktopLayout.Displays(this);var display=displays.FirstOrDefault(d=>d.Id==saved.monitor)??DesktopLayout.Current(this);
                var work=display.Work;MaxWidth=Math.Max(MinWidth,work.Width);MaxHeight=Math.Max(MinHeight,work.Height);
                double width=double.IsFinite(saved.width)?Math.Clamp(saved.width,MinWidth,MaxWidth):442;
                Width=width;MinHeight=MinimumMetricHeight(width);
                double height=double.IsFinite(saved.height)?Math.Clamp(saved.height,MinHeight,MaxHeight):Math.Min(858,MaxHeight);
                bool relative=saved.monitor==display.Id&&double.IsFinite(saved.offsetX)&&double.IsFinite(saved.offsetY);
                double left=relative?work.Left+saved.offsetX:saved.left,top=relative?work.Top+saved.offsetY:saved.top;
                if(!double.IsFinite(left)||!double.IsFinite(top)){left=work.Right-width;top=work.Top;}
                var restored=DesktopLayout.Constrain(new Rect(left,top,width,height),work,false);
                Height=restored.Height;Left=restored.Left;Top=restored.Top;compact=saved.compact;SetPinned(saved.topmost);
                if(double.IsFinite(saved.expandedHeight))expandedHeight=Math.Clamp(saved.expandedHeight,Math.Min(640,MaxHeight),MaxHeight);
            }catch(IOException){}catch(ArgumentException){}
        };
    }
    sealed record WindowPlacement(double left,double top,bool topmost,double width,double height,double expandedHeight,bool compact,string? monitor,double offsetX,double offsetY);
    void SavePosition(){try{var display=DesktopLayout.Current(this);LocalJson.Save(System.IO.Path.Combine(Data,"window.json"),new WindowPlacement(Left,Top,Topmost,Width,Height,metricsOnly?expandedHeight:Height,compact,display.Id,Left-display.Work.Left,Top-display.Work.Top));}catch(IOException){}catch(UnauthorizedAccessException){}}
    void UpdateSystem()
    {
        UpdateClocks();RefreshDisplayedProviders();CheckResetReminders();CheckLowQuota();CheckCapacityAlerts();
        ReadSystemSnapshot();ReconcileMetricVisibility();if(metricsOnly&&IsVisible&&WindowState==WindowState.Normal)SyncMetricValues();UpdateTaskbarProgress();taskbarPreview?.Invalidate();
        // Values update while hidden/minimized; resize/density work runs on actual layout changes.
        if(IsVisible&&WindowState==WindowState.Normal){cpuGraph.SetSamples(cpuHistory);ramGraph.SetSamples(ramHistory);}
    }
    static string Rate(double bytes)=>bytes>1048576?$"{bytes/1048576:0.0} MB/s":$"{bytes/1024:0} KB/s";
    static void Plot(List<double> history,Sparkline chart,double value){history.Add(value);if(history.Count>36)history.RemoveAt(0);chart.SetSamples(history);}
    void StopCollector(){try{if(collector is {HasExited:false})collector.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    async System.Threading.Tasks.Task UpdateProviders(bool manual=false)
    {
        if(offline){
            using var fixture=JsonDocument.Parse("{\"codex\":{\"status\":\"Connect\"},\"cursor\":{\"status\":\"Connect\"},\"opencode\":{\"status\":\"Connect\"},\"claude\":{\"status\":\"Connect\"}}");
            providers=fixture.RootElement.Clone();foreach(var (key,card) in cards)card.Update(providers.GetProperty(key));
            footer.Text="Offline verification · no account access";AdaptLayout();return;
        }
        if(busy||closed)return;
        var due=schedule.Due(DateTimeOffset.UtcNow,manual);if(due.Length==0)return;
        schedule.Mark(due,DateTimeOffset.UtcNow,preferences);busy=true;foreach(var button in refreshButtons)button.IsEnabled=false;footer.Text="●  "+L.T("Refresh");
        try {
            var start=new ProcessStartInfo{FileName=RuntimeSupport.Python(Root),WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(System.IO.Path.Combine(Root,"providers.py"));
            start.ArgumentList.Add("--providers");foreach(var id in due)start.ArgumentList.Add(id);
            collector=Process.Start(start)!; var stdout=collector.StandardOutput.ReadToEndAsync();var stderr=collector.StandardError.ReadToEndAsync();
            using var cts=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);cts.CancelAfter(TimeSpan.FromSeconds(45));await collector.WaitForExitAsync(cts.Token);
            if(closed)return;
            var output=await stdout;await stderr;if(collector.ExitCode!=0)throw new Exception("Collector unavailable. Check Python installation.");
            using var doc=JsonDocument.Parse(output);bool changed=false;var now=DateTimeOffset.UtcNow;
            foreach(var key in due){
                if(doc.RootElement.TryGetProperty(key,out var data)){providerData[key]=data.Clone();if(!preview&&!selftest&&preferences.HistoryEnabled)changed|=history.Record(key,QuotaSnapshot.Read(data,now),now);}
                else providerData.Remove(key);
            }
            using(var merged=JsonDocument.Parse(JsonSerializer.Serialize(providerData)))providers=merged.RootElement.Clone();
            if(changed)try{history.Save(HistoryPath);}catch(IOException){}catch(UnauthorizedAccessException){}
            RefreshForecasts(true);RefreshDisplayedProviders();CheckResetReminders();CheckLowQuota();CheckCapacityAlerts();
        }catch(Exception){if(!closed){foreach(var key in due){providerData.Remove(key);latestReadings.Remove(ProviderName(key));cards[key].MarkOffline();}footer.Text=L.T("Refresh failed; scheduled checks will retry.");}StopCollector();}
        finally{collector?.Dispose();collector=null;busy=false;if(!closed){foreach(var button in refreshButtons)button.IsEnabled=true;AdaptLayout();}}
    }
    static string ProviderName(string key)=>key switch{"codex"=>"Codex","cursor"=>"Cursor","claude"=>"Claude",_=>"OpenCode Go"};
    void CheckResetReminders()
    {
        if(preview||selftest||closed||(!IsVisible&&tray is null)||!resetReminders.Enabled||reminderPopup.IsOpen)return;
        var now=DateTimeOffset.UtcNow;var notices=resetReminders.Unseen(ResetReminders.Due(latestReadings,now,resetReminders.LeadMinutes));
        if(notices.Count==0)return;
        var messages=notices.Select(n=>n.Message(now)).ToArray();
        if(!DeliverNotice("Quota resets soon",messages))return;
        resetReminders.Mark(notices,now);
        try{resetReminders.Save(System.IO.Path.Combine(Data,"reset-reminders.json"));}catch(Exception e) when(e is IOException or UnauthorizedAccessException){}
    }
    void DismissReminder(){reminderTimer.Stop();reminderPopup.IsOpen=false;}
    Border ReminderContent(IReadOnlyList<string> messages,bool isPreview)
    {
        var content=new StackPanel();var header=Columns(-1,30);
        var title=Text(isPreview?"Preview · Quota resets soon":"Quota resets soon",14);title.Foreground=Brush("#FFE0A1");title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;Add(header,title,0);
        var dismiss=Button("×","Dismiss reset reminder",DismissReminder);dismiss.Padding=new Thickness(5,2,5,2);Add(header,dismiss,1);content.Children.Add(header);
        foreach(string message in messages.Take(3)){var line=Text(message,12);line.Margin=new Thickness(0,8,0,0);content.Children.Add(line);}
        if(messages.Count>3){var more=Text($"+ {messages.Count-3} more windows · hover for details",11,Muted);more.Margin=new Thickness(0,8,0,0);content.Children.Add(more);}
        var panel=Panel(content,"#F02A344A",14);panel.Width=310;panel.Padding=new Thickness(14);panel.BorderBrush=Brush("#C2E8BD77");panel.ToolTip=string.Join("\n",messages);
        System.Windows.Documents.TextElement.SetFontFamily(panel,FontFamily);
        System.Windows.Automation.AutomationProperties.SetName(panel,(isPreview?"Preview. ":"")+"Quota resets soon. "+string.Join(". ",messages));
        return panel;
    }
    void ShowReminder(IReadOnlyList<string> messages,bool isPreview)
    {
        DismissReminder();reminderPopup.Child=ReminderContent(messages,isPreview);reminderPopup.IsOpen=true;reminderTimer.Start();
    }
    void ShowResetReminders()
    {
        var dialog=new Window{Title="Prism · Reset reminders",Width=440,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};stack.Children.Add(Text("A nudge before the reset",23));
        var info=Text("Show a quiet, 12-second reminder when a quota window is about to reset and more than 10% remains. Each window alerts once per reset, even across restarts.",12,Muted);info.Margin=new Thickness(0,12,0,18);stack.Children.Add(info);
        var enabled=new CheckBox{Content=L.T("Enable reset reminders"),IsChecked=resetReminders.Enabled,Foreground=new SolidColorBrush(Ink),Margin=new Thickness(0,0,0,12)};stack.Children.Add(enabled);
        stack.Children.Add(Text("Remind me before reset",11,Muted));
        var lead=new ComboBox{ItemsSource=ResetReminders.LeadOptions,SelectedItem=resetReminders.LeadMinutes,ItemStringFormat="{0} minutes",Padding=new Thickness(7),Margin=new Thickness(0,6,0,14),Foreground=Brush("#172438"),Background=Brush("#EDF4FC")};stack.Children.Add(lead);
        stack.Children.Add(Text("Only current readings with a known reset time qualify. Prism must be running, either visible or in the system tray.",11,Muted));
        var status=Text("",11,Muted);status.Margin=new Thickness(0,10,0,10);stack.Children.Add(status);
        stack.Children.Add(Button("Preview reminder","Show a sample reminder without changing alert history",()=>ShowReminder(new[]{"Codex · 5-hour: resets in 20m · 42% left"},true)));
        var save=Button("Save reminders","Save reminder preferences",()=>{
            var next=new ResetReminders{Enabled=enabled.IsChecked==true,LeadMinutes=lead.SelectedItem is int minutes?minutes:30,Notified=resetReminders.Notified};
            try{next.Save(System.IO.Path.Combine(Data,"reset-reminders.json"));resetReminders=next;DismissReminder();dialog.Close();CheckResetReminders();}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Could not save reminders. Check folder access.";}
        });save.Margin=new Thickness(4,8,0,0);stack.Children.Add(save);dialog.ShowDialog();
    }
    void UpdateClocks()
    {
        var now=DateTimeOffset.UtcNow;clock.Text=L.Time(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.Local),preferences.Use24Hour);clock.FontSize=preferences.Use24Hour?21:18;
        if(!clockPreferences.Enabled)return;
        var first=WorldClocks.At(now,clockPreferences.FirstZone);var second=WorldClocks.At(now,clockPreferences.SecondZone);
        firstZoneLabel.Text=clockPreferences.FirstLabel;secondZoneLabel.Text=clockPreferences.SecondLabel;
        firstTime.Text=L.Time(first,preferences.Use24Hour);secondTime.Text=L.Time(second,preferences.Use24Hour);
        ((Grid)firstClock.Child).ColumnDefinitions[1].Width=((Grid)secondClock.Child).ColumnDefinitions[1].Width=new GridLength(preferences.Use24Hour?72:96);
        firstClock.ToolTip=WorldClocks.Description(now,clockPreferences.FirstZone,clockPreferences.FirstLabel);
        secondClock.ToolTip=WorldClocks.Description(now,clockPreferences.SecondZone,clockPreferences.SecondLabel);
        string Short(string label)=>label.Length>12?label[..11]+"…":label;
        compactTimes.FontSize=narrowClocks&&!preferences.Use24Hour?10:12;
        compactTimes.Text=narrowClocks?$"{L.Time(first,preferences.Use24Hour,true)} · {L.Time(second,preferences.Use24Hour,true)}":$"{Short(clockPreferences.FirstLabel)} {L.Time(first,preferences.Use24Hour)} · {Short(clockPreferences.SecondLabel)} {L.Time(second,preferences.Use24Hour)}";
        compactTimes.ToolTip=$"{firstClock.ToolTip}\n\n{secondClock.ToolTip}\nClick to change clocks";
        System.Windows.Automation.AutomationProperties.SetName(firstClock,firstClock.ToolTip.ToString());
        System.Windows.Automation.AutomationProperties.SetName(secondClock,secondClock.ToolTip.ToString());
    }
    void ShowClocks()
    {
        var dialog=new Window{Title="Prism · World clocks",Width=490,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Two places. One glance.",23));
        var info=Text("Add two clocks alongside your local time. Daylight saving is handled by Windows. Hover a clock for its date and UTC offset.",12,Muted);info.Margin=new Thickness(0,10,0,16);stack.Children.Add(info);
        var enabled=new CheckBox{Content=L.T("Show two additional timezone clocks"),IsChecked=clockPreferences.Enabled,Foreground=new SolidColorBrush(Ink),Margin=new Thickness(0,0,0,8)};stack.Children.Add(enabled);
        var zones=TimeZoneInfo.GetSystemTimeZones();
        (ComboBox zone,TextBox label) Choice(string title,string id,string name){
            var label=Input(stack,title+" label",name);label.MaxLength=24;
            var combo=new ComboBox{ItemsSource=zones,DisplayMemberPath="DisplayName",SelectedValuePath="Id",SelectedValue=id,IsTextSearchEnabled=true,MaxDropDownHeight=260,Margin=new Thickness(0,8,0,4),Padding=new Thickness(6),Foreground=Brush("#172438"),Background=Brush("#EDF4FC")};
            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is TimeZoneInfo zone)label.Text=WorldClocks.Label(null,zone.Id);};
            stack.Children.Add(combo);return(combo,label);
        }
        var first=Choice("First clock",clockPreferences.FirstZone,clockPreferences.FirstLabel);var second=Choice("Second clock",clockPreferences.SecondZone,clockPreferences.SecondLabel);
        var note=Text("",11,Muted);note.Margin=new Thickness(0,12,0,12);stack.Children.Add(note);
        var save=Button("Save clocks","Save timezone and display preferences",()=>{
            if(first.zone.SelectedItem is not TimeZoneInfo a||second.zone.SelectedItem is not TimeZoneInfo b){note.Text="Choose both timezones.";return;}
            if(enabled.IsChecked==true&&a.Id==b.Id){note.Text="Choose two different timezones.";return;}
            var next=new ClockPreferences(enabled.IsChecked==true,a.Id,b.Id,WorldClocks.Label(first.label.Text,a.Id),WorldClocks.Label(second.label.Text,b.Id));
            try{next.Save(System.IO.Path.Combine(Data,"clocks.json"));clockPreferences=next;AdaptLayout();dialog.Close();}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){note.Text="Could not save the clock preferences. Check folder access.";}
        });stack.Children.Add(save);dialog.ShowDialog();
    }
    async System.Threading.Tasks.Task ShowDiagnostics()
    {
        string report="Prism "+typeof(Widget).Assembly.GetName().Version+"\n.NET "+Environment.Version+"\n";
        try{
            var start=new ProcessStartInfo{FileName=RuntimeSupport.Python(Root),WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(System.IO.Path.Combine(Root,"doctor.py"));
            using var process=Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try{await process.WaitForExitAsync(timeout.Token);}catch{try{process.Kill(true);}catch{}throw;}
            if(process.ExitCode!=0)throw new IOException();
            report+=await stdout;await stderr;
        }catch{report+="Python diagnostics unavailable. Run Setup.cmd and try again.";}
        if(closed)return;
        var dialog=new Window{Title="Prism · Diagnostics",Owner=this,Width=490,Height=500,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Connection diagnostics",23));
        var info=Text("Dependency availability only. This report contains no account IDs, quota values, credentials, folder paths or machine name. CLI detection here checks PATH; provider adapters also check supported installation locations.",12,Muted);info.Margin=new Thickness(0,12,0,12);stack.Children.Add(info);
        var content=new TextBox{Text=report,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Background=Brush("#1C2D43"),Foreground=new SolidColorBrush(Ink),Padding=new Thickness(12),BorderThickness=new Thickness(0)};stack.Children.Add(content);
        var copy=Button("Copy report","Copy dependency diagnostics to clipboard",()=>{try{Clipboard.SetText(report);}catch{MessageBox.Show("Clipboard is busy. Select the report and copy it manually.","Prism");}});copy.Margin=new Thickness(0,12,0,0);stack.Children.Add(copy);dialog.ShowDialog();
    }
    void ShowConnection(string key,string name)
    {
        var dialog=new Window{Title="Prism · automatic connections",Width=470,Height=570,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Automatic connections",24));
        var clockSetup=Button("World clocks…","Show or configure two additional timezone clocks",()=>{dialog.Close();ShowClocks();});clockSetup.Margin=new Thickness(0,12,0,0);stack.Children.Add(clockSetup);
        var reminderSetup=Button("Reset reminders…","Configure reminders for unused quota before reset",()=>{dialog.Close();ShowResetReminders();});reminderSetup.Margin=new Thickness(0,8,0,0);stack.Children.Add(reminderSetup);
        if(providers.ValueKind==JsonValueKind.Object&&providers.TryGetProperty(key,out var selected)){
            var current=new StackPanel();current.Children.Add(Text(name,16));
            current.Children.Add(Text(selected.TryGetProperty("detail",out var explanation)?explanation.GetString()??"":selected.TryGetProperty("status",out var state)?state.GetString()??"":"Waiting",12,Muted));
            var summary=Panel(current);summary.Margin=new Thickness(0,14,0,0);stack.Children.Add(summary);
        }
        var desc=Text("No manual numbers. Sources refresh automatically and show when their reading was captured.",12,Muted);desc.Margin=new Thickness(0,12,0,18);stack.Children.Add(desc);
        stack.Children.Add(Text("CURSOR INDIVIDUAL + OPENCODE GO",11,Muted));
        stack.Children.Add(Text("Reuse the existing app sign-ins to read account quota. Requests go only to each provider's own usage endpoint. Prism never refreshes or saves your credentials.",12));
        bool enabled=false;try {using var doc=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(Data,"connections.json")));enabled=doc.RootElement.GetProperty("allowStoredCredentials").GetBoolean();}catch{}
        var consent=new CheckBox{Content="Allow my existing Cursor / OpenCode sign-ins",IsChecked=enabled,Foreground=new SolidColorBrush(Ink),Margin=new Thickness(0,14,0,12)};stack.Children.Add(consent);
        stack.Children.Add(Button("Save connection preference","Enable or disable automatic authenticated quota reads",async()=>{
            File.WriteAllText(System.IO.Path.Combine(Data,"connections.json"),JsonSerializer.Serialize(new{allowStoredCredentials=consent.IsChecked==true}));dialog.Close();await UpdateProviders(true);
        }));
        var claudeTitle=Text("CLAUDE SUBSCRIPTION",11,Muted);claudeTitle.Margin=new Thickness(0,28,0,9);stack.Children.Add(claudeTitle);
        stack.Children.Add(Text("Prism reads Claude Code’s /usage panel every 5 minutes. Sign in with your Claude subscription and finish any first-run CLI setup. No browser extension or manual numbers are needed; Claude Code manages its own credentials.",12));
        var guide=Button("Sign in to Claude","Open Claude Code's own subscription sign-in",()=>OpenUrl(System.IO.Path.Combine(Root,"Connect Claude CLI.cmd")));guide.Margin=new Thickness(0,14,0,10);stack.Children.Add(guide);
        stack.Children.Add(Button("Connection guide","Show CLI setup and optional feed instructions",()=>OpenUrl(System.IO.Path.Combine(Root,"README.html"))));
        var status=Text("Codex is already automatic. Cursor and Go check their servers every 5 minutes; errors back off without losing the capture timestamp.",11,Muted);status.Margin=new Thickness(0,24,0,0);stack.Children.Add(status);
        dialog.ShowDialog();
    }
    static TextBox Input(StackPanel panel,string label,string value){var t=Text(label,11,Muted);t.Margin=new Thickness(0,12,0,5);panel.Children.Add(t);var input=new TextBox{Text=value,Padding=new Thickness(8),Background=Brush("#1D2C43"),Foreground=new SolidColorBrush(Ink),BorderBrush=Brush("#3E526D")};panel.Children.Add(input);return input;}
    static void OpenUrl(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{MessageBox.Show("This connection could not be opened. Check that the Prism folder is complete.","Prism");}}
    sealed record MemoryApp(int Id,long Started,string Name,long Bytes);
    static List<MemoryApp> MemoryApps()
    {
        var result=new List<MemoryApp>();int session=Process.GetCurrentProcess().SessionId;
        foreach(var p in Process.GetProcesses())using(p)try{
            if(p.SessionId!=session||new[]{"dwm","csrss","winlogon","sihost","fontdrvhost","explorer"}.Contains(p.ProcessName.ToLowerInvariant()))continue;
            if(p.WorkingSet64<1024*1024)continue;
            result.Add(new(p.Id,p.StartTime.ToUniversalTime().Ticks,p.ProcessName,p.WorkingSet64));
        }catch{}
        return result.OrderByDescending(p=>p.Bytes).ToList();
    }
    static (bool ok,long before,long after) TrimMemory(MemoryApp app)
    {
        try{
            using var p=Process.GetProcessById(app.Id);
            if(p.StartTime.ToUniversalTime().Ticks!=app.Started||p.SessionId!=Process.GetCurrentProcess().SessionId)return(false,0,0);
            IntPtr handle=Native.OpenProcess(0x1100,false,app.Id);if(handle==IntPtr.Zero)return(false,0,0);
            try{p.Refresh();long before=p.WorkingSet64;bool ok=Native.EmptyWorkingSet(handle);p.Refresh();return(ok,before,p.WorkingSet64);}finally{Native.CloseHandle(handle);}
        }catch{return(false,0,0);}
    }
    void ShowMemory()
    {
        var dialog=new Window{Title="Prism · Free RAM",Width=490,Height=580,MinWidth=420,MinHeight=420,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),FontFamily=FontFamily};
        var root=new Grid{Margin=new Thickness(24)};foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});dialog.Content=root;
        var title=Text("A little breathing room",23);root.Children.Add(title);
        var desc=Text("Release eligible RAM pages from Prism or apps you select. Apps stay open and files are untouched. Memory can return immediately; the next interaction may be slower. This does not delete files or AI conversation memory.",12,Muted);desc.Margin=new Thickness(0,12,0,16);Grid.SetRow(desc,1);root.Children.Add(desc);
        var list=new StackPanel();var choices=new List<(CheckBox check,MemoryApp app)>();
        foreach(var app in MemoryApps()){
            var check=new CheckBox{Content=$"{app.Name} · PID {app.Id} · {app.Bytes/1048576d:0} MB",IsChecked=app.Id==Environment.ProcessId,Foreground=new SolidColorBrush(Ink),Margin=new Thickness(0,6,0,6),FontSize=12};choices.Add((check,app));list.Children.Add(check);
        }
        var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,2);root.Children.Add(scroll);
        var result=Text("Only Prism is selected by default. Protected apps may be skipped.",11,Muted);result.Margin=new Thickness(0,14,0,14);Grid.SetRow(result,3);root.Children.Add(result);
        var action=Button("Free selected RAM","Trim eligible working-set pages from selected processes",()=>{});
        action.Click+=async (_,_)=>{
            var selected=choices.Where(x=>x.check.IsChecked==true).Select(x=>x.app).ToList();if(selected.Count==0){result.Text="Select at least one app.";return;}
            action.IsEnabled=false;result.Text="Releasing eligible pages…";
            var outcomes=await System.Threading.Tasks.Task.Run(()=>selected.Select(TrimMemory).ToList());
            int ok=outcomes.Count(x=>x.ok);long reduction=outcomes.Where(x=>x.ok).Sum(x=>Math.Max(0,x.before-x.after));
            result.Text=$"{ok} app(s) trimmed · {selected.Count-ok} skipped. Working sets dropped {reduction/1048576d:0} MB at measurement; this is not guaranteed free system RAM.";UpdateSystem();action.IsEnabled=true;
        };
        Grid.SetRow(action,4);root.Children.Add(action);dialog.ShowDialog();
    }
    static void ConnectClaude(Window owner)
    {
        var config=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude","settings.json");
        try {
            var settings=File.Exists(config)?System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(config))!.AsObject():new System.Text.Json.Nodes.JsonObject();
            if(settings.ContainsKey("statusLine")){MessageBox.Show(owner,"You already have a Claude status line. Prism has preserved it. See README.html for the feed command to integrate with your existing script.","Existing status line preserved");return;}
            if(MessageBox.Show(owner,"Add the Prism status-line command to your Claude Code settings? A backup will be saved; other settings are preserved.","Connect Claude",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            if(File.Exists(config))File.Copy(config,config+".prism-backup-"+DateTime.Now.ToString("yyyyMMddHHmmss"));
            settings["statusLine"]=new System.Text.Json.Nodes.JsonObject{["type"]="command",["command"]="\""+RuntimeSupport.Python(Root).Replace('\\','/')+"\" \""+System.IO.Path.Combine(Root,"claude_feed.py").Replace('\\','/')+"\""};
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(config)!);File.WriteAllText(config,settings.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            MessageBox.Show(owner,"Connected. Restart Claude Code, then complete a turn to populate the widget.","Claude connected");
        } catch(Exception e){MessageBox.Show(owner,e.Message,"Connection could not be saved");}
    }
    void SavePreview(string name="preview.png",double scale=1)
    {
        UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth*scale),(int)Math.Ceiling(ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        // The HD path renders font outlines instead of magnifying screen-DPI glyph caches.
        bitmap.Render(scale>1?VectorSnapshot.Capture(this):this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(System.IO.Path.Combine(Root,name));encoder.Save(file);
    }
    void RunSelfTest()
    {
        bool initial=Topmost;pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));bool pinOk=Topmost!=initial;pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        bool initialCompact=compact;double initialHeight=Height;compact=false;Height=Math.Min(MaxHeight,800);AdaptLayout();ToggleCompact();UpdateLayout();AdaptLayout();bool compactOk=metricsOnly;ToggleCompact();UpdateLayout();AdaptLayout();compactOk&=metricsOnly==(ActualHeight<640);compact=initialCompact;Height=initialHeight;AdaptLayout();
        bool quotaValidation=RunQuotaChecks();
        bool clocks=WorldClocks.Verify();
        var clockTestPath=System.IO.Path.Combine(Root,"build","test-tmp","clocks-"+Guid.NewGuid().ToString("N")+".json");
        try{
            var testPreferences=new ClockPreferences(true,"Eastern Standard Time","Pacific Standard Time","Eastern","Pacific");
            testPreferences.Save(clockTestPath);clocks&=ClockPreferences.Load(clockTestPath)==testPreferences;
            File.WriteAllText(clockTestPath,"{malformed");clocks&=ClockPreferences.Load(clockTestPath)==new ClockPreferences();
        }finally{if(File.Exists(clockTestPath))File.Delete(clockTestPath);}
        bool reminders;
        var reminderTestPath=System.IO.Path.Combine(Root,"build","test-tmp","reminders-"+Guid.NewGuid().ToString("N")+".json");
        try{reminders=ResetReminders.Verify(reminderTestPath);}finally{if(File.Exists(reminderTestPath))File.Delete(reminderTestPath);}
        var reminderPreview=ReminderContent(new[]{"Codex · 5-hour: resets in 20m · 42% left"},true);
        reminderPreview.Measure(new Size(310,double.PositiveInfinity));reminderPreview.Arrange(new Rect(reminderPreview.DesiredSize));reminderPreview.UpdateLayout();
        var alertImage=new RenderTargetBitmap(620,(int)Math.Ceiling(reminderPreview.ActualHeight*2),192,192,PixelFormats.Pbgra32);alertImage.Render(VectorSnapshot.Capture(reminderPreview));
        var alertEncoder=new PngBitmapEncoder();alertEncoder.Frames.Add(BitmapFrame.Create(alertImage));using(var alertFile=File.Create(System.IO.Path.Combine(Root,"preview-alert.png")))alertEncoder.Save(alertFile);
        reminders&=reminderPreview.ActualWidth<=310&&reminderPreview.ActualHeight>0;
        ShowReminder(new[]{"Codex · 5-hour: resets in 20m · 42% left"},true);
        reminders&=reminderPopup.IsOpen&&reminderTimer.IsEnabled;DismissReminder();
        reminders&=!reminderPopup.IsOpen&&!reminderTimer.IsEnabled;
        bool sensors=cpuValue.Text!="—"&&ramValue.Text!="—"&&diskValue.Text!="—";
        double oldWidth=Width,oldHeight=Height,oldLeft=Left,oldTop=Top,oldExpandedHeight=expandedHeight;bool oldCompact=compact;compact=false;
        bool presets=true;
        foreach(var preset in new[]{(442d,800d),(560d,220d),(1200d,120d)}){
            ApplyPreset(preset.Item1,preset.Item2);UpdateLayout();AdaptLayout();UpdateLayout();
            presets&=Math.Abs(ActualWidth-Math.Min(preset.Item1,MaxWidth))<1&&Math.Abs(ActualHeight-Math.Clamp(preset.Item2,MinHeight,MaxHeight))<1;
            presets&=metricsOnly==(ActualHeight<640);
        }
        var layouts=new List<object>();bool fits=true;var oldClocks=clockPreferences;double hdLogicalWidth=0,hdLogicalHeight=0;
        foreach(bool showClocks in new[]{false,true}){
        clockPreferences=oldClocks with{Enabled=showClocks};
        foreach(var size in new[]{(340d,220d),(400d,430d),(560d,165d),(442d,600d),(442d,858d),(840d,260d),(840d,800d),(1200d,120d)}){
            Width=size.Item1;Height=size.Item2;UpdateLayout();AdaptLayout();UpdateLayout();
            var bounds=body.TransformToAncestor(this).TransformBounds(new Rect(0,0,body.ActualWidth,body.ActualHeight));
            var all=metricsOnly?WidgetPreferences.BaseMetricIds.Select(id=>(FrameworkElement)metricTiles[id].Element).Concat(new FrameworkElement[]{metricHeader}):cards.Values.Select(c=>(FrameworkElement)c.Element).Concat(new FrameworkElement[]{fullHeader,cpuPanel,ramPanel,extras,section,bottom});
            all=all.Concat((metricsOnly?compactActions:fullActions).Buttons);
            if(showClocks)all=all.Concat(metricsOnly?new FrameworkElement[]{compactTimes}:new FrameworkElement[]{worldClockRow,firstTime,secondTime});
            bool visible=all.All(el=>{var box=el.TransformToAncestor(this).TransformBounds(new Rect(0,0,el.ActualWidth,el.ActualHeight));return el.IsVisible&&box.Left>=10&&box.Top>=10&&box.Right<=ActualWidth-9&&box.Bottom<=ActualHeight-9;});
            bool fit=visible&&bounds.Bottom<=ActualHeight-10&&bounds.Right<=ActualWidth-10;fits&=fit;
            layouts.Add(new{clocksEnabled=showClocks,width=ActualWidth,height=ActualHeight,density,metricsOnly,columns=metricsOnly?metricGrid.Columns:providerGrid.Columns,scale=Math.Round(bounds.Width/body.ActualWidth,3),allReadingsVisible=visible,fits=fit});
            if(showClocks)SavePreview($"preview-{(int)size.Item1}x{(int)size.Item2}.png");
            if(showClocks&&size.Item1==442&&size.Item2==858){hdLogicalWidth=ActualWidth;hdLogicalHeight=ActualHeight;SavePreview("preview-hd.png",4);}
        }
        }
        var transitions=new List<object>();
        foreach(bool showClocks in new[]{false,true}){
            clockPreferences=oldClocks with{Enabled=showClocks,FirstLabel="A long first clock label",SecondLabel="Another long clock label"};
            var sizes=(from width in new[]{340d,429d,430d,521d,522d,649d,650d,731d,732d,1001d,1002d,1200d} from height in new[]{220d,279d,280d,339d,340d,639d,640d,759d,760d,858d} select (width,height)).ToArray();
            foreach(var size in sizes.Concat(sizes.Reverse())){
                ApplyPreset(size.width,size.height);UpdateLayout();AdaptLayout();UpdateLayout();
                var check=CheckLayout();
                transitions.Add(new{clocksEnabled=showClocks,width=ActualWidth,height=ActualHeight,check.fits,check.controls,check.scale});
            }
        }
        var customized=new List<object>();var originalPreferences=preferences.Copy();
        foreach(string language in new[]{"en","hi","es","fr"})foreach(bool use24 in new[]{true,false}){
            L.Set(language);preferences.Language=language;preferences.Use24Hour=use24;clockPreferences=oldClocks with{Enabled=true};
            using(var fixture=JsonDocument.Parse(JsonSerializer.Serialize(new{status="Live",at=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),windows=new[]{new{label="Session",remaining=100.0,reset=DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds()}}})))foreach(var card in cards.Values)card.Update(fixture.RootElement);
            diskValue.Text=L.F("{0:0.0} GB free",freeDiskGb);
            if(use24)ShowPreferences();
            foreach(var visible in new[]{WidgetPreferences.BaseMetricIds,new[]{"cpu"},new[]{"ram","disk","net"},new[]{"claude","cursor","codex"}}){
                preferences.VisibleMetrics=visible.ToList();preferences.MetricOrder=WidgetPreferences.MetricIds.Reverse().ToList();ApplyMetricPreferences();
                foreach(var size in new[]{(340d,220d),(442d,858d),(560d,165d),(840d,800d),(1200d,120d)}){
                    ApplyPreset(size.Item1,size.Item2);UpdateLayout();AdaptLayout();UpdateLayout();var check=CheckLayout();
                    bool selection=metricGrid.Children.Count==visible.Length&&metricGrid.Children.OfType<FrameworkElement>().All(e=>e.Visibility==Visibility.Visible);
                    customized.Add(new{language,use24,metrics=visible.Length,width=ActualWidth,height=ActualHeight,check.fits,check.controls,check.scale,selection});
                    if(use24&&visible.Length==8&&size.Item1==442)SavePreview("preview-"+language+".png",2);
                }
            }
        }
        preferences=originalPreferences;L.Set("en");ApplyMetricPreferences();clockPreferences=oldClocks;VerifyCapacityViews();VerifySystemViews();
        Width=oldWidth;Height=oldHeight;Left=oldLeft;Top=oldTop;expandedHeight=oldExpandedHeight;compact=oldCompact;UpdateLayout();AdaptLayout();UpdateLayout();
        File.WriteAllText(System.IO.Path.Combine(Root,"selftest.json"),JsonSerializer.Serialize(new{pin=pinOk,compact=compactOk,quotaValidation,presets,clocks,reminders,featureChecks,forecastChecks,systemChecks,systemLayouts,featureOpenTimes,forecast40kMs=ForecastChecks.BenchmarkMilliseconds,customized,sensors,responsive=fits,layouts,transitions,offline,hdLogicalWidth,hdLogicalHeight,providers=providers.ValueKind==JsonValueKind.Object,cpu=cpuValue.Text,memory=ramDetail.Text,drive=diskValue.Text}));
    }
    (bool fits,bool controls,double scale) CheckLayout()
    {
        Rect Bounds(FrameworkElement el)=>el.TransformToAncestor(this).TransformBounds(new Rect(0,0,el.ActualWidth,el.ActualHeight));
        bool Inside(FrameworkElement el){var b=Bounds(el);return el.IsVisible&&b.Width>0&&b.Height>0&&b.Left>=10&&b.Top>=10&&b.Right<=ActualWidth-9&&b.Bottom<=ActualHeight-9;}
        bool RowFits(Panel panel){
            var children=panel.Children.OfType<FrameworkElement>().Where(el=>el.IsVisible).ToArray();
            if(!children.All(Inside))return false;
            for(int i=1;i<children.Length;i++)if(Bounds(children[i-1]).Right>Bounds(children[i]).Left+.5)return false;
            return true;
        }
        var actions=metricsOnly?compactActions:fullActions;
        var header=metricsOnly?metricHeader:fullHeader;
        bool controls=actions.Buttons.Count==3&&actions.Buttons.All(Inside)&&RowFits(actions.Element)&&RowFits(header)
            &&(!(metricsOnly||ActualWidth<430||ActualHeight<760)||actions.IconsOnly)
            &&(metricsOnly||RowFits(section));
        IEnumerable<FrameworkElement> content=metricsOnly?metricTiles.Values.Select(tile=>(FrameworkElement)tile.Element):cards.Values.Select(card=>(FrameworkElement)card.Element).Concat(new FrameworkElement[]{cpuPanel,ramPanel,extras,section,bottom});
        if(clockPreferences.Enabled)content=content.Concat(metricsOnly?new FrameworkElement[]{compactTimes}:new FrameworkElement[]{firstClock,secondClock});
        var bounds=Bounds(body);double scale=bounds.Width/body.ActualWidth;
        return(content.Where(el=>el.Visibility==Visibility.Visible).All(Inside)&&bounds.Bottom<=ActualHeight-10&&bounds.Right<=ActualWidth-10,controls,Math.Round(scale,3));
    }
    static bool RunQuotaChecks()
    {
        var now=DateTimeOffset.UtcNow;
        QuotaSnapshot Read(object data){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(data));return QuotaSnapshot.Read(doc.RootElement,now);}
        object Window(object remaining,object? reset=null)=>new{label="Test",remaining,reset};
        object Data(params object[] windows)=>new{status="Live",at=now.ToUnixTimeSeconds(),windows};
        bool zero=Read(Data(Window(0))).Remaining==0;
        bool unknown=new[]{Window("bad"),Window(101),Window(50,"bad"),Window(50,1e20)}.All(window=>Read(Data(window)).Remaining==null);
        bool expiry=Read(Data(Window(5,now.AddSeconds(-1).ToUnixTimeSeconds()),Window(70))).Remaining==70;
        bool future=Read(new{status="Live",at=now.AddHours(1).ToUnixTimeSeconds(),windows=new[]{Window(50)}}).Remaining==null;
        bool reset=Read(Data(Window(20,now.AddHours(2).ToUnixTimeSeconds()))).ResetSummary(now)=="Resets in 2h";
        bool mixed=Read(Data(Window(80),Window("bad"))).Remaining==null;
        return zero&&unknown&&expiry&&future&&reset&&mixed;
    }
    sealed class ProviderCard
    {
        public Border Element{get;}
        public string Health{get;private set;}="unknown";
        public string CompactValue=>Remaining is double number?$"{number:0.#}%":"—";
        public double? Remaining{get;private set;}
        readonly TextBlock value=Text("—",22),detail=Text("Connecting…",10,Muted),status=Text("WAITING",9,Muted);
        readonly Meter bar;readonly string name;readonly Brush accent;
        readonly StackPanel labels=new();
        readonly Grid row=Columns(37,-1,115),readout=Columns(-1,-1);
        readonly TextBlock title;
        readonly Border mark;
        bool tileLayout;
        public ProviderCard(string key,string name,string icon,string color,Action click)
        {
            this.name=name;accent=Brush(color);var stack=new StackPanel();
            mark=new Border{Background=Brush(color),CornerRadius=new CornerRadius(10),Width=29,Height=29,VerticalAlignment=VerticalAlignment.Top,Child=new TextBlock{Text=icon,FontSize=21,Foreground=Brush("#12212E"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};Add(row,mark,0);
            title=Text(name,14);title.FontWeight=FontWeights.SemiBold;labels.Children.Add(title);labels.Children.Add(status);Add(row,labels,1);
            value.HorizontalAlignment=HorizontalAlignment.Right;value.Foreground=Brush(color);Add(row,value,2);stack.Children.Add(row);readout.Visibility=Visibility.Collapsed;stack.Children.Add(readout);
            bar=Bar(color);stack.Children.Add(bar);detail.Margin=new Thickness(0,6,0,0);stack.Children.Add(detail);
            Element=Panel(stack,"#75263853",14);Element.Padding=new Thickness(13,11,13,11);Element.Margin=new Thickness(0,0,8,8);Element.Cursor=Cursors.Hand;Element.ToolTip="Click to manage "+name+" connection";
            Element.MouseLeftButtonUp+=(_,_)=>click();Element.MouseEnter+=(_,_)=>Element.BorderBrush=Brush(color);Element.MouseLeave+=(_,_)=>Element.BorderBrush=Brush("#304C6078");
            Element.Focusable=true;Element.KeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space)click();};System.Windows.Automation.AutomationProperties.SetName(Element,name+" connection");
        }
        public void SetDensity(int level,double width)
        {
            bool tile=width<260,tight=level>0;
            if(tile!=tileLayout){
                if(tile){row.Children.Remove(value);labels.Children.Remove(status);Add(readout,value,0);Add(readout,status,1);}
                else{readout.Children.Remove(value);readout.Children.Remove(status);Add(row,value,2);labels.Children.Add(status);}
                tileLayout=tile;
            }
            readout.Visibility=tile?Visibility.Visible:Visibility.Collapsed;
            readout.ColumnDefinitions[0].Width=GridLength.Auto;
            readout.Margin=new Thickness(0,tile?3:0,0,0);
            row.ColumnDefinitions[0].Width=new GridLength(tight?28:37);
            row.ColumnDefinitions[2].Width=new GridLength(tile?0:Math.Min(150,width*.40));
            value.HorizontalAlignment=tile?HorizontalAlignment.Left:HorizontalAlignment.Right;
            value.FontSize=tile?18:tight?19:22;
            value.Text=Remaining is double remaining?(tile?$"{remaining:0.#}%":L.F("{0:0.#}% left",remaining)):"—";
            value.TextWrapping=TextWrapping.NoWrap;
            status.HorizontalAlignment=tile?HorizontalAlignment.Right:HorizontalAlignment.Left;
            status.Margin=new Thickness(tile?6:0,0,0,0);
            status.VerticalAlignment=VerticalAlignment.Center;
            status.FontSize=9;status.MaxHeight=tile?25:double.PositiveInfinity;
            title.FontSize=tile?12:14;title.VerticalAlignment=VerticalAlignment.Center;
            mark.Width=mark.Height=tight?23:29;
            ((TextBlock)mark.Child).FontSize=tight?17:21;
            Element.Padding=new Thickness(tight?8:13,tight?7:11,tight?8:13,tight?7:11);
            Element.Margin=new Thickness(0,0,tight?5:8,tight?5:8);
            detail.Visibility=level==2?Visibility.Collapsed:Visibility.Visible;
            detail.MaxHeight=tight?26:42;
            detail.TextTrimming=TextTrimming.CharacterEllipsis;
            detail.Margin=new Thickness(0,tight?3:6,0,0);
            bar.Margin=new Thickness(0,tight?4:9,0,0);
        }
        public void MarkOffline(){Remaining=null;bar.Value=0;Health="error";Element.Opacity=1;status.Text="REFRESH FAILED";bar.Opacity=.3;detail.Text="Reading unavailable · click to connect";value.Text="—";Element.ToolTip=$"{name} · {status.Text}\n{detail.Text}";}
        public void Update(JsonElement data)
        {
            var now=DateTimeOffset.UtcNow;var reading=QuotaSnapshot.Read(data,now);
            Remaining=reading.Remaining;var state=reading.Status;status.Text=L.T(state);
            if(reading.Captured is DateTimeOffset at){double seconds=Math.Max(0,(now-at).TotalSeconds);status.Text+=" · "+(seconds<60?L.T("just now"):seconds<3600?L.F("{0:0}m ago",seconds/60):L.F("{0:0}h ago",seconds/3600));}
            bar.Value=Remaining??0;bar.Opacity=Remaining.HasValue?1:.15;
            if(Remaining is double remaining){
                value.Text=tileLayout?$"{remaining:0.#}%":L.F("{0:0.#}% left",remaining);
                var parts=reading.Windows.Select(w=>$"{w.Label} {w.Remaining:0.#}%").ToList();
                string reset=reading.ResetSummary(now);if(reset.Length>0)parts.Add(reset);
                detail.Text=string.Join(" · ",parts);
            }else{value.Text="—";detail.Text=L.T(reading.Windows.Count==0&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("windows",out _)?"No current quota window · waiting for a fresh reading":reading.Detail);}
            Health=Remaining.HasValue?(state.Contains("Stale",StringComparison.OrdinalIgnoreCase)?"stale":"ready"):(state=="Connect"||state=="Waiting"?"unknown":"error");
            if(Health=="stale"){bar.Opacity=.4;detail.Text="Last reported · "+detail.Text;}
            var capacityColor=Remaining<=10?Brush("#F0A29A"):Remaining<=25?Brush("#E8C083"):accent;
            value.Foreground=bar.Foreground=capacityColor;
            Element.Opacity=1;
            status.Foreground=Health=="stale"?Brush("#E8B96D"):Health=="error"?Brush("#DEA1A5"):new SolidColorBrush(Muted);
            var resetDetails=reading.Windows.Where(w=>w.Reset.HasValue).Select(w=>$"{w.Label} resets {w.Reset!.Value.LocalDateTime:ddd, MMM d HH:mm}");
            Element.ToolTip=$"{name} · {reading.Source}\n{status.Text}\n{detail.Text}\n"+string.Join("\n",resetDetails)+"\nClick to manage connection";
            System.Windows.Automation.AutomationProperties.SetName(Element,$"{name} · {value.Text} · {status.Text}");
        }
    }

    sealed class MetricTile
    {
        public Border Element{get;}
        readonly TextBlock value=Text("—",20);
        readonly Sparkline chart;
        readonly Ellipse stateDot=new(){Width=4,Height=4,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,0,0)};
        readonly Meter meter;
        readonly string name;readonly TextBlock label;
        double fontSize=20;
        bool showChart;
        public MetricTile(string name,string icon,string color)
        {
            this.name=name;var content=new StackPanel();label=Text(name,11,Muted);label.Margin=new Thickness(0,0,0,5);label.Visibility=Visibility.Collapsed;content.Children.Add(label);var row=Columns(24,-1,8);
            var mark=Text(icon,18);mark.Foreground=Brush(color);mark.VerticalAlignment=VerticalAlignment.Center;Add(row,mark,0);
            value.Foreground=Brush(color);value.TextWrapping=TextWrapping.NoWrap;value.HorizontalAlignment=HorizontalAlignment.Right;Add(row,value,1);Add(row,stateDot,2);content.Children.Add(row);
            chart=new Sparkline(color){Height=28,Margin=new Thickness(0,5,0,0)};content.Children.Add(chart);
            meter=Bar(color);meter.Height=2;meter.Margin=new Thickness(0,4,0,0);content.Children.Add(meter);
            Element=Panel(content,"#75263853",12);Element.Padding=new Thickness(7,5,7,5);Element.Margin=new Thickness(0,0,5,5);Element.Focusable=true;
            System.Windows.Automation.AutomationProperties.SetName(Element,name);
        }
        public void Arrange(double width,bool charts,bool tight,bool labels=false){
            label.Visibility=labels?Visibility.Visible:Visibility.Collapsed;
            fontSize=tight?16:width<140?17:21;showChart=charts;
            Element.Padding=new Thickness(7,tight?1:5,7,tight?1:5);Element.Margin=new Thickness(0,0,5,tight?3:5);
            meter.Margin=new Thickness(0,tight?2:4,0,0);
        }
        public void Set(string text,string tooltip,double? percent,List<double>? history=null,string health="ready")
        {
            value.Text=text;value.FontSize=text.Length>8?Math.Min(fontSize,13):fontSize;
            Element.ToolTip=tooltip;System.Windows.Automation.AutomationProperties.SetName(Element,L.T(name)+" · "+text+" · "+L.T(health=="ready"?"Current":health=="stale"?"Stale":health=="error"?"Error":"Unavailable"));
            meter.Value=percent??0;meter.Opacity=percent.HasValue?(health=="stale"?.35:1):.15;
            Element.Opacity=1;
            stateDot.Fill=Brush(health=="ready"?"#6AF5E2":health=="stale"?"#E8B96D":health=="error"?"#DEA1A5":"#7A8A9D");
            var visibility=showChart&&history!=null?Visibility.Visible:Visibility.Collapsed;
            if(chart.Visibility!=visibility){
                chart.Visibility=visibility;
                // WPF normally propagates this invalidation on its next layout pass.
                // The adaptive grid must measure the new chart state in this pass.
                Element.Child.InvalidateMeasure();Element.InvalidateMeasure();
            }
            if(history!=null)chart.SetSamples(history);
        }
    }
    static class Native
    {
        [DllImport("kernel32.dll")]public static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
        [StructLayout(LayoutKind.Sequential)]public struct MemoryStatus{public uint Length,MemoryLoad;public ulong TotalPhys,AvailPhys,TotalPageFile,AvailPageFile,TotalVirtual,AvailVirtual,AvailExtendedVirtual;}
        [DllImport("kernel32.dll",SetLastError=true)]public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll",SetLastError=true)]public static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("psapi.dll",SetLastError=true)]public static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("kernel32.dll")]public static extern bool CloseHandle(IntPtr handle);
    }
}
