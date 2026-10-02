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
        bool diagnostic=args.Contains("--preview")||args.Contains("--selftest"),created=true;
        string identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        using var instance=diagnostic?null:new System.Threading.Mutex(true,"Local\\PrismWidget_"+identity,out created);
        if(!created){ActivateExisting();return;}
        var app = new App();
        app.DispatcherUnhandledException += (_,e) => { try { File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"error.log"),DateTime.UtcNow.ToString("O")+" "+e.Exception.GetType().Name+Environment.NewLine); } catch {} e.Handled=true; };
        app.Run(new Widget(args.Contains("--preview"), args.Contains("--selftest")));
    }
    static void ActivateExisting()
    {
        foreach(var process in Process.GetProcessesByName("Prism"))using(process)try{
            if(process.Id!=Environment.ProcessId&&process.MainModule?.FileName==Environment.ProcessPath&&process.MainWindowHandle!=IntPtr.Zero){ShowWindow(process.MainWindowHandle,9);SetForegroundWindow(process.MainWindowHandle);return;}
        }catch{}
    }
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
}

public sealed class Widget : Window
{
    static readonly Color Ink=Color.FromRgb(239,246,255), Muted=Color.FromRgb(151,170,191);
    static readonly string Root=AppContext.BaseDirectory;
    static readonly string Data=System.IO.Path.Combine(Root,"data");
    readonly StackPanel body=new();
    readonly StackPanel expandedBody=new(),metricBody=new();
    readonly UniformGrid metricGrid=new(){Columns=2};
    readonly Dictionary<string,MetricTile> metricTiles=new();
    Grid metricHeader=null!;
    bool metricsOnly;
    double expandedHeight=858;
    readonly UniformGrid providerGrid=new(){Columns=1};
    readonly Viewbox fittedBody=new(){Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
    Border frame=null!,cpuPanel=null!,ramPanel=null!;
    Grid greeting=null!,extras=null!,section=null!,bottom=null!;
    StackPanel utilities=null!;
    TextBlock tagline=null!,hint=null!,networkNote=null!;
    Sparkline cpuGraph=null!,ramGraph=null!;
    bool adapting;
    int density;
    readonly TextBlock cpuValue=Text("—",38), ramValue=Text("—",38), ramDetail=Text("Reading memory",11,Muted), diskValue=Text("—",14), netValue=Text("—",14);
    readonly TextBlock clock=Text("",21), footer=Text("Connecting to your workspace",10,Muted);


    readonly Meter ramBar=Bar("#B3A1FF"), diskBar=Bar("#82BDFF");
    readonly Dictionary<string,ProviderCard> cards=new();
    readonly List<double> cpuHistory=new(), ramHistory=new();
    readonly UsageGauge cpuGauge=new("#7EEAD5"),ramGauge=new("#B3A1FF");
    readonly DispatcherTimer systemTimer=new(){Interval=TimeSpan.FromSeconds(2)}, providerTimer=new(){Interval=TimeSpan.FromSeconds(60)};
    ulong oldIdle,oldKernel,oldUser;
    long oldReceived,oldSent; DateTime networkAt=DateTime.UtcNow;
    bool busy,closed,compact;
    bool memoryReady,diskReady,networkReady;
    Process? collector;
    readonly System.Threading.CancellationTokenSource lifetime=new();
    readonly List<Button> refreshButtons=new();
    readonly bool preview,selftest;
    StackPanel systems=null!;
    Button pin=null!;
    Button metricPin=null!;
    JsonElement providers;
    public Widget(bool preview,bool selftest)
    {
        this.preview=preview; this.selftest=selftest;
        Directory.CreateDirectory(Data);
        Title="Prism · workspace pulse"; Width=442; Height=858; WindowStyle=WindowStyle.None;
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Assets/prism.ico"));
        AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.CanResize;
        MinWidth=340; MinHeight=220; MaxWidth=SystemParameters.WorkArea.Width;
        UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        FontFamily=new FontFamily("Segoe UI"); Foreground=new SolidColorBrush(Ink);
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
        Build();
        LoadPosition();
        SizeChanged+=(_,_)=>AdaptLayout();
        systemTimer.Tick+=(_,_)=>UpdateSystem(); providerTimer.Tick+=async (_,_)=>await UpdateProviders();
        Loaded+=async (_,_)=>{
            UpdateSystem(); systemTimer.Start(); providerTimer.Start(); await UpdateProviders();
            if(preview || selftest) {
                await System.Threading.Tasks.Task.Delay(5000); UpdateSystem();
                if(selftest) RunSelfTest();
                SavePreview(); Close();
            }
        };
        Closed+=(_,_)=>{closed=true; systemTimer.Stop();providerTimer.Stop(); lifetime.Cancel();StopCollector();if(!preview&&!selftest)SavePosition();};
        KeyDown+=async (_,e)=>{
            if(e.Key==Key.F5){await UpdateProviders();e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.P){SetPinned(!Topmost);e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.M){ToggleCompact();e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.OemComma){ShowConnection("cursor","Cursor");e.Handled=true;}
            else if(e.Key==Key.Escape)Close();
        };
    }
    static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    static TextBlock Text(string value,double size=12,Color? color=null)=>new(){Text=value,FontSize=size,Foreground=new SolidColorBrush(color??Ink),TextWrapping=TextWrapping.Wrap};
    static Meter Bar(string color)=>new(){Height=4,Foreground=Brush(color),Background=Brush("#26354A"),Margin=new Thickness(0,9,0,0)};
    static Button Button(string text,string tip,Action click)
    {
        var b=new Button {Content=text,ToolTip=tip,Foreground=new SolidColorBrush(Ink),Background=Brush("#162335"),BorderBrush=Brush("#344358"),BorderThickness=new Thickness(1),Padding=new Thickness(9,5,9,5),Cursor=Cursors.Hand,FontSize=12,Margin=new Thickness(4,0,0,0)};
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent}); border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetBinding(FrameworkElement.MarginProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        border.AppendChild(content); template.VisualTree=border; b.Template=template;
        System.Windows.Automation.AutomationProperties.SetName(b,tip);ToolTipService.SetInitialShowDelay(b,250);
        b.Click+=(_,_)=>click();b.MouseEnter+=(_,_)=>b.Background=Brush("#304359");b.MouseLeave+=(_,_)=>b.Background=Brush("#162335");return b;
    }
    static Border Panel(UIElement child,string color="#561C2A40",int radius=18)=>new(){Background=new LinearGradientBrush(Brush(color).Color,Color.FromArgb(35,31,37,60),70),BorderBrush=Brush("#385D738B"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(radius),Padding=new Thickness(16),Child=child};
    void SetPinned(bool value){Topmost=value;pin.Content=metricPin.Content=value?"◆":"◇";}
    Button RefreshButton(bool small)
    {
        var button=Button(small?"↻":"↻ Refresh","Refresh readings · F5",async()=>await UpdateProviders());refreshButtons.Add(button);return button;
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
            void Item(string label,string shortcut,Action action){var item=new MenuItem{Header=label,InputGestureText=shortcut,Padding=new Thickness(10,6,10,6)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Item(Topmost?"Unpin widget":"Keep on top","Ctrl+P",()=>SetPinned(!Topmost));
            Item("Fit to content","",FitHeight);Item("Toggle compact view","Ctrl+M",ToggleCompact);
            menu.Items.Add(new Separator());
            Item("Dashboard · 442 × 800","",()=>ApplyPreset(442,800));
            Item("Focus · 560 × 220","",()=>ApplyPreset(560,220));
            Item("Ribbon · 1200 × 120","",()=>ApplyPreset(1200,120));
            menu.Items.Add(new Separator());Item("Free RAM…","",ShowMemory);Item("Connections…","Ctrl+,",()=>ShowConnection("cursor","Cursor"));
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
        brand.Children.Add(new Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/prism-icon.png")),Width=small?20:28,Height=small?20:28,Margin=new Thickness(0,0,small?5:9,0)});
        var text=Text(small?"PRISM":"P R I S M",small?13:20);text.FontWeight=FontWeights.SemiBold;text.VerticalAlignment=VerticalAlignment.Center;brand.Children.Add(text);
        brand.MouseLeftButtonDown+=(_,_)=>DragMove();return brand;
    }
    void Build()
    {
        var outer=new Border{CornerRadius=new CornerRadius(25),Margin=new Thickness(10),Padding=new Thickness(21),BorderThickness=new Thickness(1)};frame=outer;
        outer.Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(237,15,33,43),0),new(Color.FromArgb(239,16,23,38),.45),new(Color.FromArgb(241,29,24,49),1)},new Point(0,0),new Point(1,1));
        outer.BorderBrush=new LinearGradientBrush(Brush("#8890B4C4").Color,Brush("#25485C80").Color,45);
        outer.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=18,ShadowDepth=4,Opacity=.35};
        fittedBody.Child=body;outer.Child=fittedBody;
        var surface=new Grid();surface.Children.Add(outer);Content=surface;
        var grip=new Thumb{Width=22,Height=22,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,15,15),Cursor=Cursors.SizeNWSE,ToolTip="Drag to resize · double-click to fit height"};
        var gripTemplate=new ControlTemplate(typeof(Thumb));var glyph=new FrameworkElementFactory(typeof(TextBlock));glyph.SetValue(TextBlock.TextProperty,"◢");glyph.SetValue(TextBlock.ForegroundProperty,Brush("#889AB5"));glyph.SetValue(TextBlock.FontSizeProperty,18d);gripTemplate.VisualTree=glyph;grip.Template=gripTemplate;
        grip.DragDelta+=(_,e)=>{Width=Math.Clamp(ActualWidth+e.HorizontalChange,MinWidth,MaxWidth);Height=Math.Clamp(ActualHeight+e.VerticalChange,MinHeight,MaxHeight);};
        grip.MouseDoubleClick+=(_,_)=>FitHeight();surface.Children.Add(grip);
        var header=Columns(-1,136);
        var brand=new StackPanel();brand.Children.Add(Brand(false));
        tagline=Text("SYSTEMS  /  INTELLIGENCE",9,Muted);brand.Children.Add(tagline); Add(header,brand,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        pin=Button("◇","Keep on top",()=>{Topmost=!Topmost;pin.Content=Topmost?"◆":"◇";metricPin.Content=pin.Content;});actions.Children.Add(pin);
        actions.Children.Add(Button("−","Compact / expanded",()=>ToggleCompact()));
        actions.Children.Add(MenuButton());Add(header,actions,1);
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==header)DragMove();};expandedBody.Children.Add(header);
        greeting=Columns(-1,100); greeting.Margin=new Thickness(0,25,0,19);
        var greetingText=new StackPanel();greetingText.Children.Add(Text("Workspace pulse",23));greetingText.Children.Add(Text("Live system health. AI capacity in view.",11,Muted));Add(greeting,greetingText,0);
        clock.HorizontalAlignment=HorizontalAlignment.Right;clock.FontWeight=FontWeights.Light;Add(greeting,clock,1);expandedBody.Children.Add(greeting);
        systems=new StackPanel();expandedBody.Children.Add(systems);
        var metrics=Columns(-1,12,-1);
        var cpu=new StackPanel();cpu.Children.Add(Text("PROCESSOR",10,Muted));var cpuReadout=Columns(-1,48);Add(cpuReadout,cpuValue,0);Add(cpuReadout,cpuGauge,1);cpu.Children.Add(cpuReadout);cpu.Children.Add(Text($"{Environment.ProcessorCount} logical cores",11,Muted));cpuGraph=new Sparkline("#7EEAD5"){Height=44,Margin=new Thickness(0,9,0,0)};cpu.Children.Add(cpuGraph);
        var ram=new StackPanel();ram.Children.Add(Text("MEMORY",10,Muted));var ramReadout=Columns(-1,48);Add(ramReadout,ramValue,0);Add(ramReadout,ramGauge,1);ram.Children.Add(ramReadout);ram.Children.Add(ramDetail);ramGraph=new Sparkline("#B3A1FF"){Height=44,Margin=new Thickness(0,9,0,0)};ram.Children.Add(ramGraph);
        cpuPanel=Panel(cpu);ramPanel=Panel(ram,"#50332850");Add(metrics,cpuPanel,0);Add(metrics,ramPanel,2);systems.Children.Add(metrics);
        extras=Columns(-1,18,-1);extras.Margin=new Thickness(0,14,0,18);
        var disk=new StackPanel();disk.Children.Add(Text("SYSTEM DRIVE",9,Muted));diskValue.Margin=new Thickness(0,5,0,0);disk.Children.Add(diskValue);disk.Children.Add(diskBar);
        var net=new StackPanel();net.Children.Add(Text("NETWORK · ↓ / ↑",9,Muted));netValue.Margin=new Thickness(0,5,0,0);net.Children.Add(netValue);networkNote=Text("Active physical adapters",9,Muted);net.Children.Add(networkNote);Add(extras,disk,0);Add(extras,net,2);systems.Children.Add(extras);
        utilities=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,12)};
        utilities.Children.Add(Button("Free RAM","Trim Prism or selected apps without closing them",ShowMemory));utilities.Children.Add(Button("Fit height","Fit the widget height to its contents",FitHeight));expandedBody.Children.Add(utilities);
        section=Columns(-1,100);section.Margin=new Thickness(0,3,0,10);var label=Text("AI CAPACITY",10,Muted);label.VerticalAlignment=VerticalAlignment.Center;Add(section,label,0);
        var refresh=RefreshButton(false);Add(section,refresh,1);expandedBody.Children.Add(section);
        foreach(var spec in new[]{("codex","Codex","⌘","#7EEAD5"),("cursor","Cursor","↗","#9DBDFF"),("opencode","OpenCode Go","▣","#BCA7FF"),("claude","Claude","✳","#EAB293")})
        {
            var card=new ProviderCard(spec.Item1,spec.Item2,spec.Item3,spec.Item4,()=>ShowConnection(spec.Item1,spec.Item2));
            cards[spec.Item1]=card;providerGrid.Children.Add(card.Element);
        }
        expandedBody.Children.Add(providerGrid);
        bottom=Columns(-1,92);bottom.Margin=new Thickness(0,10,0,0);footer.VerticalAlignment=VerticalAlignment.Center;Add(bottom,footer,0);
        Add(bottom,Button("Settings","Automatic account connections",()=>ShowConnection("cursor","Cursor")),1);expandedBody.Children.Add(bottom);
        hint=Text("System live · AI readings carry their own timestamp",9,Muted);hint.Margin=new Thickness(0,9,0,0);expandedBody.Children.Add(hint);
        body.Children.Add(expandedBody);
        BuildMetricBody();body.Children.Add(metricBody);
    }
    void BuildMetricBody()
    {
        metricHeader=Columns(-1,0);metricHeader.ColumnDefinitions[1].Width=GridLength.Auto;
        var brand=Brand(true);Add(metricHeader,brand,0);
        metricHeader.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==metricHeader)DragMove();};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(RefreshButton(true));
        metricPin=Button("◇","Toggle keep on top",()=>SetPinned(!Topmost));
        actions.Children.Add(Button("□","Restore detailed layout",RestoreDetails));
        actions.Children.Add(MenuButton());
        actions.Children.Add(Button("×","Close Prism",Close));Add(metricHeader,actions,1);
        metricHeader.Margin=new Thickness(0,0,0,6);metricBody.Children.Add(metricHeader);
        foreach(var spec in new[]{("cpu","CPU","▤","#7EEAD5"),("ram","Memory","▥","#B3A1FF"),("disk","Free disk","◴","#82BDFF"),("net","Network download / upload","↕","#9BCCD7"),("codex","Codex","⌘","#7EEAD5"),("cursor","Cursor","↗","#9DBDFF"),("opencode","OpenCode Go","▣","#BCA7FF"),("claude","Claude","✳","#EAB293")}){
            var tile=new MetricTile(spec.Item2,spec.Item3,spec.Item4);metricTiles[spec.Item1]=tile;metricGrid.Children.Add(tile.Element);
            if(cards.ContainsKey(spec.Item1)){
                tile.Element.Cursor=Cursors.Hand;tile.Element.MouseLeftButtonUp+=(_,_)=>ShowConnection(spec.Item1,spec.Item2);
                tile.Element.KeyDown+=(_,e)=>{if(e.Key==Key.Enter||e.Key==Key.Space)ShowConnection(spec.Item1,spec.Item2);};
            }
        }
        metricBody.Children.Add(metricGrid);
    }
    void SyncMetricValues()
    {
        metricTiles["cpu"].Set(cpuValue.Text,$"CPU · {cpuValue.Text} · {Environment.ProcessorCount} logical cores",cpuHistory.Count>0?cpuHistory[^1]:null,cpuHistory,health:cpuHistory.Count>0?"ready":"unknown");
        metricTiles["ram"].Set(ramValue.Text,$"Memory · {ramValue.Text} · {ramDetail.Text}",memoryReady?ramBar.Value:null,ramHistory,health:memoryReady?"ready":"unknown");
        metricTiles["disk"].Set(diskValue.Text.Replace(" free",""),"System drive · "+diskValue.Text,diskReady?diskBar.Value:null,health:diskReady?"ready":"unknown");
        metricTiles["net"].Set(netValue.Text.Replace(" KB/s","K").Replace(" MB/s","M"),"Network download / upload · "+netValue.Text,null,health:netValue.Text!="—"?"ready":"unknown");
        foreach(var (key,card) in cards)metricTiles[key].Set(card.CompactValue,card.Element.ToolTip?.ToString()??key,card.Remaining,health:card.Health);
    }
    void AdaptLayout()
    {
        if(adapting||cards.Count!=4)return;adapting=true;
        try{
            double width=ActualWidth>0?ActualWidth:Width,height=ActualHeight>0?ActualHeight:Height;
            MinHeight=width>=1002?120:width>=522?165:220;
            metricPin.Content=Topmost?"◆":"◇";
            metricsOnly=compact||height<640;
            expandedBody.Visibility=metricsOnly?Visibility.Collapsed:Visibility.Visible;
            metricBody.Visibility=metricsOnly?Visibility.Visible:Visibility.Collapsed;
            if(metricsOnly){
                density=3;frame.Padding=new Thickness(10);double availableWidth=width-42;
                body.Width=Math.Max(1,availableWidth);metricGrid.Columns=availableWidth>=960?8:availableWidth>=480?4:2;
                bool charts=height>=340&&availableWidth/metricGrid.Columns>=130;
                foreach(var tile in metricTiles.Values)tile.Arrange(availableWidth/metricGrid.Columns,charts);
                SyncMetricValues();body.Measure(new Size(availableWidth,double.PositiveInfinity));
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
                utilities.Margin=new Thickness(0,0,0,tight?3:12);
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
        try {using var doc=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(Data,"window.json")));var d=doc.RootElement;if(d.TryGetProperty("width",out var w))Width=Math.Clamp(w.GetDouble(),MinWidth,MaxWidth);MinHeight=Width>=1002?120:Width>=522?165:220;if(d.TryGetProperty("height",out var h))Height=Math.Clamp(h.GetDouble(),MinHeight,MaxHeight);if(d.TryGetProperty("compact",out var c)){compact=c.GetBoolean();}if(d.TryGetProperty("expandedHeight",out var expanded)&&expanded.TryGetDoubleSafe(out var eh))expandedHeight=Math.Clamp(eh,Math.Min(640,MaxHeight),MaxHeight);Left=Math.Clamp(d.GetProperty("left").GetDouble(),SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width);Top=Math.Clamp(d.GetProperty("top").GetDouble(),SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-Height);Topmost=d.GetProperty("topmost").GetBoolean();pin.Content=Topmost?"◆":"◇";}catch{}
    }
    void SavePosition(){try{File.WriteAllText(System.IO.Path.Combine(Data,"window.json"),JsonSerializer.Serialize(new{left=Left,top=Top,topmost=Topmost,width=Width,height=Height,expandedHeight=metricsOnly?expandedHeight:Height,compact}));}catch{}}
    void UpdateSystem()
    {
        clock.Text=DateTime.Now.ToString("HH:mm");
        try {
            if(Native.GetSystemTimes(out var idle,out var kernel,out var user)) {
                if(oldKernel!=0) {double all=(kernel-oldKernel)+(user-oldUser);double usage=all<=0?0:Math.Clamp(100*(1-(idle-oldIdle)/all),0,100);cpuValue.Text=$"{usage:0}%";cpuGauge.Set(usage);Plot(cpuHistory,cpuGraph,usage);}
                oldIdle=idle;oldKernel=kernel;oldUser=user;
            }
            var mem=new Native.MemoryStatus();mem.Length=(uint)Marshal.SizeOf<Native.MemoryStatus>();
            if(Native.GlobalMemoryStatusEx(ref mem)){var used=(mem.TotalPhys-mem.AvailPhys)/1073741824d;ramValue.Text=$"{mem.MemoryLoad}%";ramDetail.Text=$"{used:0.0} / {mem.TotalPhys/1073741824d:0.0} GB";ramBar.Value=mem.MemoryLoad;memoryReady=true;ramGauge.Set(mem.MemoryLoad);Plot(ramHistory,ramGraph,mem.MemoryLoad);}
            var drive=new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);diskValue.Text=$"{drive.AvailableFreeSpace/1073741824d:0.0} GB free";diskBar.Value=100*(double)drive.AvailableFreeSpace/drive.TotalSize;diskReady=true;
            long received=0,sent=0;foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211))) {var s=nic.GetIPv4Statistics();received+=s.BytesReceived;sent+=s.BytesSent;}
            var now=DateTime.UtcNow;var seconds=(now-networkAt).TotalSeconds;if(networkReady&&seconds>0)netValue.Text=$"{Rate(Math.Max(0,received-oldReceived)/seconds)} / {Rate(Math.Max(0,sent-oldSent)/seconds)}";networkReady=true;oldReceived=received;oldSent=sent;networkAt=now;
        } catch {footer.Text="A system sensor is unavailable";}
        AdaptLayout();
    }
    static string Rate(double bytes)=>bytes>1048576?$"{bytes/1048576:0.0} MB/s":$"{bytes/1024:0} KB/s";
    static void Plot(List<double> history,Sparkline chart,double value){history.Add(value);if(history.Count>36)history.RemoveAt(0);chart.SetSamples(history);}
    void StopCollector(){try{if(collector is {HasExited:false})collector.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
    async System.Threading.Tasks.Task UpdateProviders()
    {
        if(busy||closed)return;busy=true;foreach(var button in refreshButtons)button.IsEnabled=false;footer.Text="●  Refreshing AI readings";
        try {
            var start=new ProcessStartInfo{FileName="python",WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(System.IO.Path.Combine(Root,"providers.py"));
            collector=Process.Start(start)!; var stdout=collector.StandardOutput.ReadToEndAsync();var stderr=collector.StandardError.ReadToEndAsync();
            using var cts=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);cts.CancelAfter(TimeSpan.FromSeconds(45));await collector.WaitForExitAsync(cts.Token);
            if(closed)return;
            var output=await stdout;await stderr;if(collector.ExitCode!=0)throw new Exception("Collector unavailable. Check Python installation.");
            using var doc=JsonDocument.Parse(output);providers=doc.RootElement.Clone();
            foreach(var (key,card) in cards){try{if(providers.TryGetProperty(key,out var data))card.Update(data);else card.MarkOffline();}catch{card.MarkOffline();}}
            footer.Text=$"{cards.Values.Count(c=>c.Health=="ready")} of 4 sources current  ·  {DateTime.Now:HH:mm}";
        }catch(Exception){if(!closed){foreach(var card in cards.Values)card.MarkOffline();footer.Text="Could not refresh · retrying in 60s";}StopCollector();}
        finally{collector?.Dispose();collector=null;busy=false;if(!closed){foreach(var button in refreshButtons)button.IsEnabled=true;AdaptLayout();}}
    }
    void ShowConnection(string key,string name)
    {
        var dialog=new Window{Title="Prism · automatic connections",Width=470,Height=570,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Automatic connections",24));
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
            File.WriteAllText(System.IO.Path.Combine(Data,"connections.json"),JsonSerializer.Serialize(new{allowStoredCredentials=consent.IsChecked==true}));dialog.Close();await UpdateProviders();
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
            settings["statusLine"]=new System.Text.Json.Nodes.JsonObject{["type"]="command",["command"]="python \""+System.IO.Path.Combine(Root,"claude_feed.py").Replace('\\','/')+"\""};
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(config)!);File.WriteAllText(config,settings.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
            MessageBox.Show(owner,"Connected. Restart Claude Code, then complete a turn to populate the widget.","Claude connected");
        } catch(Exception e){MessageBox.Show(owner,e.Message,"Connection could not be saved");}
    }
    void SavePreview(string name="preview.png")
    {
        UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(System.IO.Path.Combine(Root,name));encoder.Save(file);
    }
    void RunSelfTest()
    {
        bool initial=Topmost;pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));bool pinOk=Topmost!=initial;pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        bool initialCompact=compact;double initialHeight=Height;compact=false;Height=Math.Min(MaxHeight,800);AdaptLayout();ToggleCompact();UpdateLayout();AdaptLayout();bool compactOk=metricsOnly;ToggleCompact();UpdateLayout();AdaptLayout();compactOk&=!metricsOnly;compact=initialCompact;Height=initialHeight;AdaptLayout();
        bool quotaValidation=RunQuotaChecks();
        bool sensors=cpuValue.Text!="—"&&ramValue.Text!="—"&&diskValue.Text!="—";
        double oldWidth=Width,oldHeight=Height,oldLeft=Left,oldTop=Top,oldExpandedHeight=expandedHeight;bool oldCompact=compact;compact=false;
        bool presets=true;
        foreach(var preset in new[]{(442d,800d),(560d,220d),(1200d,120d)}){
            ApplyPreset(preset.Item1,preset.Item2);UpdateLayout();AdaptLayout();UpdateLayout();
            presets&=Math.Abs(ActualWidth-Math.Min(preset.Item1,MaxWidth))<1&&Math.Abs(ActualHeight-Math.Clamp(preset.Item2,MinHeight,MaxHeight))<1;
            presets&=metricsOnly==(preset.Item2<640);
        }
        var layouts=new List<object>();bool fits=true;
        foreach(var size in new[]{(340d,220d),(400d,430d),(560d,165d),(442d,600d),(442d,858d),(840d,260d),(840d,800d),(1200d,120d)}){
            Width=size.Item1;Height=size.Item2;UpdateLayout();AdaptLayout();UpdateLayout();
            var bounds=body.TransformToAncestor(this).TransformBounds(new Rect(0,0,body.ActualWidth,body.ActualHeight));
            var all=metricsOnly?metricTiles.Values.Select(c=>(FrameworkElement)c.Element).Concat(new FrameworkElement[]{metricHeader}):cards.Values.Select(c=>(FrameworkElement)c.Element).Concat(new FrameworkElement[]{cpuPanel,ramPanel,extras,utilities,section,bottom});
            bool visible=all.All(el=>{var box=el.TransformToAncestor(this).TransformBounds(new Rect(0,0,el.ActualWidth,el.ActualHeight));return el.IsVisible&&box.Left>=10&&box.Top>=10&&box.Right<=ActualWidth-9&&box.Bottom<=ActualHeight-9;});
            bool fit=visible&&bounds.Bottom<=ActualHeight-10&&bounds.Right<=ActualWidth-10;fits&=fit;
            layouts.Add(new{width=ActualWidth,height=ActualHeight,density,metricsOnly,columns=metricsOnly?metricGrid.Columns:providerGrid.Columns,scale=Math.Round(bounds.Width/body.ActualWidth,3),allReadingsVisible=visible,fits=fit});
            SavePreview($"preview-{(int)size.Item1}x{(int)size.Item2}.png");
        }
        Width=oldWidth;Height=oldHeight;Left=oldLeft;Top=oldTop;expandedHeight=oldExpandedHeight;compact=oldCompact;UpdateLayout();AdaptLayout();UpdateLayout();
        File.WriteAllText(System.IO.Path.Combine(Root,"selftest.json"),JsonSerializer.Serialize(new{pin=pinOk,compact=compactOk,quotaValidation,presets,sensors,responsive=fits,layouts,providers=providers.ValueKind==JsonValueKind.Object,cpu=cpuValue.Text,memory=ramDetail.Text,drive=diskValue.Text}));
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
        public string CompactValue=>value.Text.Replace(" left","");
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
            Element=Panel(stack,"#401D2A3E",14);Element.Padding=new Thickness(13,11,13,11);Element.Margin=new Thickness(0,0,8,8);Element.Cursor=Cursors.Hand;Element.ToolTip="Click to manage "+name+" connection";
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
            row.ColumnDefinitions[2].Width=new GridLength(tile?0:tight?108:115);
            value.HorizontalAlignment=tile?HorizontalAlignment.Left:HorizontalAlignment.Right;
            value.FontSize=tile?18:tight?19:22;
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
        public void MarkOffline(){Remaining=null;bar.Value=0;Health="error";Element.Opacity=.7;status.Text="REFRESH FAILED";bar.Opacity=.3;detail.Text="Reading unavailable · click to connect";value.Text="—";Element.ToolTip=$"{name} · {status.Text}\n{detail.Text}";}
        public void Update(JsonElement data)
        {
            var now=DateTimeOffset.UtcNow;var reading=QuotaSnapshot.Read(data,now);
            Remaining=reading.Remaining;var state=reading.Status;status.Text=state;
            if(reading.Captured is DateTimeOffset at){double seconds=Math.Max(0,(now-at).TotalSeconds);status.Text+=" · "+(seconds<60?"just now":seconds<3600?$"{seconds/60:0}m ago":$"{seconds/3600:0}h ago");}
            bar.Value=Remaining??0;bar.Opacity=Remaining.HasValue?1:.15;
            if(Remaining is double remaining){
                value.Text=$"{remaining:0.#}% left";
                var parts=reading.Windows.Select(w=>$"{w.Label} {w.Remaining:0.#}%").ToList();
                string reset=reading.ResetSummary(now);if(reset.Length>0)parts.Add(reset);
                detail.Text=string.Join(" · ",parts);
            }else{value.Text="—";detail.Text=reading.Windows.Count==0&&data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("windows",out _)?"No current quota window · waiting for a fresh reading":reading.Detail;}
            Health=Remaining.HasValue?(state.Contains("Stale",StringComparison.OrdinalIgnoreCase)?"stale":"ready"):(state=="Connect"||state=="Waiting"?"unknown":"error");
            if(Health=="stale"){bar.Opacity=.4;detail.Text="Last reported · "+detail.Text;}
            var capacityColor=Remaining<=10?Brush("#F0A29A"):Remaining<=25?Brush("#E8C083"):accent;
            value.Foreground=bar.Foreground=capacityColor;
            Element.Opacity=Health=="ready"?1:Health=="stale"?.8:.72;
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
        readonly string name;
        double fontSize=20;
        bool showChart;
        public MetricTile(string name,string icon,string color)
        {
            this.name=name;var content=new StackPanel();var row=Columns(24,-1,8);
            var mark=Text(icon,18);mark.Foreground=Brush(color);mark.VerticalAlignment=VerticalAlignment.Center;Add(row,mark,0);
            value.Foreground=Brush(color);value.TextWrapping=TextWrapping.NoWrap;value.HorizontalAlignment=HorizontalAlignment.Right;Add(row,value,1);Add(row,stateDot,2);content.Children.Add(row);
            chart=new Sparkline(color){Height=28,Margin=new Thickness(0,5,0,0)};content.Children.Add(chart);
            meter=Bar(color);meter.Height=2;meter.Margin=new Thickness(0,4,0,0);content.Children.Add(meter);
            Element=Panel(content,"#401D2A3E",12);Element.Padding=new Thickness(7,5,7,5);Element.Margin=new Thickness(0,0,5,5);Element.Focusable=true;
            System.Windows.Automation.AutomationProperties.SetName(Element,name);
        }
        public void Arrange(double width,bool charts){fontSize=width<140?17:21;showChart=charts;}
        public void Set(string text,string tooltip,double? percent,List<double>? history=null,string health="ready")
        {
            value.Text=text;value.FontSize=text.Length>8?Math.Min(fontSize,13):fontSize;
            Element.ToolTip=tooltip;System.Windows.Automation.AutomationProperties.SetName(Element,name+" · "+text);
            meter.Value=percent??0;meter.Opacity=percent.HasValue?(health=="stale"?.35:1):.15;
            Element.Opacity=health=="ready"?1:health=="stale"?.8:.65;
            stateDot.Fill=Brush(health=="ready"?"#7EEAD5":health=="stale"?"#E8B96D":health=="error"?"#DEA1A5":"#7A8A9D");
            chart.Visibility=showChart&&history!=null?Visibility.Visible:Visibility.Collapsed;
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
