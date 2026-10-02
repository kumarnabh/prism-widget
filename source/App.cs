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
        var app = new App();
        app.DispatcherUnhandledException += (_,e) => { try { File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"error.log"),e.Exception+Environment.NewLine); } catch {} e.Handled=true; };
        app.Run(new Widget(args.Contains("--preview"), args.Contains("--selftest")));
    }
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
    readonly UniformGrid providerGrid=new(){Columns=1};
    readonly Viewbox fittedBody=new(){Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center};
    Border frame=null!,cpuPanel=null!,ramPanel=null!;
    Grid greeting=null!,extras=null!,section=null!,bottom=null!;
    StackPanel utilities=null!;
    TextBlock tagline=null!,hint=null!,networkNote=null!;
    Canvas cpuGraph=null!,ramGraph=null!;
    bool adapting;
    int density;
    readonly TextBlock cpuValue=Text("—",38), ramValue=Text("—",38), ramDetail=Text("Reading memory",11,Muted), diskValue=Text("—",14), netValue=Text("—",14);
    readonly TextBlock clock=Text("",21), footer=Text("Connecting to your workspace",10,Muted);
    readonly Polyline cpuLine=new(){Stroke=Brush("#7EEAD5"),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round};
    readonly Polyline ramLine=new(){Stroke=Brush("#B3A1FF"),StrokeThickness=2,StrokeLineJoin=PenLineJoin.Round};
    readonly ProgressBar ramBar=Bar("#B3A1FF"), diskBar=Bar("#82BDFF");
    readonly Dictionary<string,ProviderCard> cards=new();
    readonly List<double> cpuHistory=new(), ramHistory=new();
    readonly DispatcherTimer systemTimer=new(){Interval=TimeSpan.FromSeconds(2)}, providerTimer=new(){Interval=TimeSpan.FromSeconds(60)};
    ulong oldIdle,oldKernel,oldUser;
    long oldReceived,oldSent; DateTime networkAt=DateTime.UtcNow;
    bool busy,closed,compact;
    Process? collector;
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
        Closed+=(_,_)=>{closed=true; systemTimer.Stop();providerTimer.Stop(); if(collector is {HasExited:false}) collector.Kill(true); if(!preview&&!selftest) SavePosition();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape) Close();};
    }
    static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    static TextBlock Text(string value,double size=12,Color? color=null)=>new(){Text=value,FontSize=size,Foreground=new SolidColorBrush(color??Ink),TextWrapping=TextWrapping.Wrap};
    static ProgressBar Bar(string color)=>new(){Minimum=0,Maximum=100,Height=4,Foreground=Brush(color),Background=Brush("#26354A"),BorderThickness=new Thickness(0),Margin=new Thickness(0,9,0,0)};
    static Button Button(string text,string tip,Action click)
    {
        var b=new Button {Content=text,ToolTip=tip,Foreground=new SolidColorBrush(Ink),Background=Brush("#162335"),BorderBrush=Brush("#344358"),BorderThickness=new Thickness(1),Padding=new Thickness(9,5,9,5),Cursor=Cursors.Hand,FontSize=12,Margin=new Thickness(4,0,0,0)};
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent}); border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty,new Thickness(9,5,9,5)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        border.AppendChild(content); template.VisualTree=border; b.Template=template;
        b.Click+=(_,_)=>click(); b.MouseEnter+=(_,_)=>b.Background=Brush("#304359"); b.MouseLeave+=(_,_)=>b.Background=Brush("#162335"); return b;
    }
    static Border Panel(UIElement child,string color="#561C2A40",int radius=18)=>new(){Background=Brush(color),BorderBrush=Brush("#304C6078"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(radius),Padding=new Thickness(16),Child=child};
    static Grid Columns(params double[] widths)
    { var g=new Grid();foreach(var w in widths)g.ColumnDefinitions.Add(new(){Width=w<0?new GridLength(-w,GridUnitType.Star):new GridLength(w)});return g; }
    static void Add(Grid grid,UIElement el,int col){Grid.SetColumn(el,col);grid.Children.Add(el);}
    StackPanel Brand(bool small)
    {
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        brand.Children.Add(new Image{Source=Icon,Width=small?20:28,Height=small?20:28,Margin=new Thickness(0,0,small?5:9,0)});
        var text=Text(small?"PRISM":"P R I S M",small?13:20);text.FontWeight=FontWeights.SemiBold;text.VerticalAlignment=VerticalAlignment.Center;brand.Children.Add(text);
        brand.MouseLeftButtonDown+=(_,_)=>DragMove();return brand;
    }
    void Build()
    {
        var outer=new Border{CornerRadius=new CornerRadius(25),Margin=new Thickness(10),Padding=new Thickness(21),BorderThickness=new Thickness(1)};frame=outer;
        outer.Background=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(213,22,41,54),0),new(Color.FromArgb(218,20,27,48),.45),new(Color.FromArgb(227,24,22,46),1)},new Point(0,0),new Point(1,1));
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
        tagline=Text("YOUR WORKSPACE, IN VIEW",9,Muted);brand.Children.Add(tagline); Add(header,brand,0);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        pin=Button("◇","Keep on top",()=>{Topmost=!Topmost;pin.Content=Topmost?"◆":"◇";metricPin.Content=pin.Content;});actions.Children.Add(pin);
        actions.Children.Add(Button("−","Compact / expanded",()=>ToggleCompact()));
        actions.Children.Add(Button("×","Close Prism",Close));Add(header,actions,1);
        header.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource==header)DragMove();};expandedBody.Children.Add(header);
        greeting=Columns(-1,100); greeting.Margin=new Thickness(0,25,0,19);
        var greetingText=new StackPanel();greetingText.Children.Add(Text("Workspace pulse",23));greetingText.Children.Add(Text("A little clarity for your busy machine.",11,Muted));Add(greeting,greetingText,0);
        clock.HorizontalAlignment=HorizontalAlignment.Right;clock.FontWeight=FontWeights.Light;Add(greeting,clock,1);expandedBody.Children.Add(greeting);
        systems=new StackPanel();expandedBody.Children.Add(systems);
        var metrics=Columns(-1,12,-1);
        var cpu=new StackPanel();cpu.Children.Add(Text("PROCESSOR",10,Muted));cpu.Children.Add(cpuValue);cpu.Children.Add(Text($"{Environment.ProcessorCount} logical cores",11,Muted));cpuGraph=new Canvas{Height=35,Margin=new Thickness(0,9,0,0),Children={cpuLine}};cpu.Children.Add(cpuGraph);
        var ram=new StackPanel();ram.Children.Add(Text("MEMORY",10,Muted));ram.Children.Add(ramValue);ram.Children.Add(ramDetail);ramGraph=new Canvas{Height=35,Margin=new Thickness(0,9,0,0),Children={ramLine}};ram.Children.Add(ramGraph);
        cpuPanel=Panel(cpu);ramPanel=Panel(ram,"#50332850");Add(metrics,cpuPanel,0);Add(metrics,ramPanel,2);systems.Children.Add(metrics);
        extras=Columns(-1,18,-1);extras.Margin=new Thickness(0,14,0,18);
        var disk=new StackPanel();disk.Children.Add(Text("SYSTEM DRIVE",9,Muted));diskValue.Margin=new Thickness(0,5,0,0);disk.Children.Add(diskValue);disk.Children.Add(diskBar);
        var net=new StackPanel();net.Children.Add(Text("NETWORK · ↓ / ↑",9,Muted));netValue.Margin=new Thickness(0,5,0,0);net.Children.Add(netValue);networkNote=Text("Active physical adapters",9,Muted);net.Children.Add(networkNote);Add(extras,disk,0);Add(extras,net,2);systems.Children.Add(extras);
        utilities=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,12)};
        utilities.Children.Add(Button("Free RAM","Trim Prism or selected apps without closing them",ShowMemory));utilities.Children.Add(Button("Fit height","Fit the widget height to its contents",FitHeight));expandedBody.Children.Add(utilities);
        section=Columns(-1,100);section.Margin=new Thickness(0,3,0,10);var label=Text("AI AVAILABILITY",10,Muted);label.VerticalAlignment=VerticalAlignment.Center;Add(section,label,0);
        var refresh=Button("↻ Refresh","Refresh AI readings",async()=>await UpdateProviders());Add(section,refresh,1);expandedBody.Children.Add(section);
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
        actions.Children.Add(Button("RAM","Free RAM from selected apps",ShowMemory));
        actions.Children.Add(Button("↻","Refresh AI readings",async()=>await UpdateProviders()));
        actions.Children.Add(Button("⚙","Settings",()=>ShowConnection("cursor","Cursor")));
        metricPin=Button("◇","Toggle keep on top",()=>{Topmost=!Topmost;pin.Content=Topmost?"◆":"◇";metricPin.Content=pin.Content;});actions.Children.Add(metricPin);
        actions.Children.Add(Button("□","Restore detailed layout",()=>{compact=false;Height=Math.Min(MaxHeight,Math.Max(Height,700));AdaptLayout();}));
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
        metricTiles["cpu"].Set(cpuValue.Text,$"CPU · {cpuValue.Text} · {Environment.ProcessorCount} logical cores",cpuHistory.LastOrDefault(),cpuHistory);
        metricTiles["ram"].Set(ramValue.Text,$"Memory · {ramValue.Text} · {ramDetail.Text}",ramBar.Value,ramHistory);
        metricTiles["disk"].Set(diskValue.Text.Replace(" free",""),"System drive · "+diskValue.Text,100-diskBar.Value);
        metricTiles["net"].Set(netValue.Text.Replace(" KB/s","K").Replace(" MB/s","M"),"Network download / upload · "+netValue.Text,null);
        foreach(var (key,card) in cards)metricTiles[key].Set(card.CompactValue,card.Element.ToolTip?.ToString()??key,card.Remaining);
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
                cpuGraph.Height=ramGraph.Height=dense?10:tight?20:35;
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
            Redraw(cpuHistory,cpuLine);Redraw(ramHistory,ramLine);
        }finally{adapting=false;}
    }
    void FitHeight(){AdaptLayout();Height=Math.Clamp(body.DesiredSize.Height+22+frame.Padding.Top*2,MinHeight,MaxHeight);}
    void ToggleCompact(){compact=!compact;AdaptLayout();}
    void LoadPosition()
    {
        try {using var doc=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(Data,"window.json")));var d=doc.RootElement;if(d.TryGetProperty("width",out var w))Width=Math.Clamp(w.GetDouble(),MinWidth,MaxWidth);MinHeight=Width>=1002?120:Width>=522?165:220;if(d.TryGetProperty("height",out var h))Height=Math.Clamp(h.GetDouble(),MinHeight,MaxHeight);if(d.TryGetProperty("compact",out var c)){compact=c.GetBoolean();}Left=Math.Clamp(d.GetProperty("left").GetDouble(),SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-Width);Top=Math.Clamp(d.GetProperty("top").GetDouble(),SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-100);Topmost=d.GetProperty("topmost").GetBoolean();pin.Content=Topmost?"◆":"◇";}catch{}
    }
    void SavePosition(){try{File.WriteAllText(System.IO.Path.Combine(Data,"window.json"),JsonSerializer.Serialize(new{left=Left,top=Top,topmost=Topmost,width=Width,height=Height,compact}));}catch{}}
    void UpdateSystem()
    {
        clock.Text=DateTime.Now.ToString("HH:mm");
        try {
            if(Native.GetSystemTimes(out var idle,out var kernel,out var user)) {
                if(oldKernel!=0) {double all=(kernel-oldKernel)+(user-oldUser);double usage=all<=0?0:Math.Clamp(100*(1-(idle-oldIdle)/all),0,100);cpuValue.Text=$"{usage:0}%";Plot(cpuHistory,cpuLine,usage);}
                oldIdle=idle;oldKernel=kernel;oldUser=user;
            }
            var mem=new Native.MemoryStatus();mem.Length=(uint)Marshal.SizeOf<Native.MemoryStatus>();
            if(Native.GlobalMemoryStatusEx(ref mem)){var used=(mem.TotalPhys-mem.AvailPhys)/1073741824d;ramValue.Text=$"{mem.MemoryLoad}%";ramDetail.Text=$"{used:0.0} / {mem.TotalPhys/1073741824d:0.0} GB";ramBar.Value=mem.MemoryLoad;Plot(ramHistory,ramLine,mem.MemoryLoad);}
            var drive=new DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);diskValue.Text=$"{drive.AvailableFreeSpace/1073741824d:0.0} GB free";diskBar.Value=100*(1-(double)drive.AvailableFreeSpace/drive.TotalSize);
            long received=0,sent=0;foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ethernet||n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211))) {var s=nic.GetIPv4Statistics();received+=s.BytesReceived;sent+=s.BytesSent;}
            var now=DateTime.UtcNow;var seconds=(now-networkAt).TotalSeconds;if(oldReceived>0&&seconds>0)netValue.Text=$"{Rate(Math.Max(0,received-oldReceived)/seconds)} / {Rate(Math.Max(0,sent-oldSent)/seconds)}";oldReceived=received;oldSent=sent;networkAt=now;
        } catch {footer.Text="A system sensor is unavailable";}
        AdaptLayout();
    }
    static string Rate(double bytes)=>bytes>1048576?$"{bytes/1048576:0.0} MB/s":$"{bytes/1024:0} KB/s";
    static void Plot(List<double> history,Polyline line,double value){history.Add(value);if(history.Count>36)history.RemoveAt(0);Redraw(history,line);}
    static void Redraw(List<double> history,Polyline line){var canvas=line.Parent as Canvas;double width=canvas?.ActualWidth??146,height=Math.Max(1,(canvas?.Height??35)-3);line.Points=new PointCollection(history.Select((v,i)=>new Point(i*Math.Max(0,width)/35,height*(1-v/100))));}
    async System.Threading.Tasks.Task UpdateProviders()
    {
        if(busy||closed)return;busy=true;footer.Text="●  Refreshing AI readings";
        try {
            var start=new ProcessStartInfo{FileName="python",WorkingDirectory=Root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(System.IO.Path.Combine(Root,"providers.py"));
            collector=Process.Start(start)!; var stdout=collector.StandardOutput.ReadToEndAsync();var stderr=collector.StandardError.ReadToEndAsync();
            using var cts=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45));await collector.WaitForExitAsync(cts.Token);
            var output=await stdout;await stderr;if(collector.ExitCode!=0)throw new Exception("Collector unavailable. Check Python installation.");
            using var doc=JsonDocument.Parse(output);providers=doc.RootElement.Clone();
            foreach(var (key,card) in cards)if(providers.TryGetProperty(key,out var data))card.Update(data);
            footer.Text=$"●  System live  ·  {DateTime.Now:HH:mm:ss}";
        }catch(Exception){foreach(var card in cards.Values)card.MarkOffline();footer.Text="AI refresh failed · retry in 60s";if(collector is {HasExited:false})collector.Kill(true);}
        finally{collector?.Dispose();collector=null;busy=false;AdaptLayout();}
    }
    void ShowConnection(string key,string name)
    {
        var dialog=new Window{Title="Prism · automatic connections",Width=470,Height=570,MaxHeight=SystemParameters.WorkArea.Height-40,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=this,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),ResizeMode=ResizeMode.NoResize,FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Automatic connections",24));
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
    static void OpenUrl(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
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
        bool initialCompact=compact;ToggleCompact();bool compactOk=compact!=initialCompact;ToggleCompact();compactOk&=compact==initialCompact;
        bool sensors=cpuValue.Text!="—"&&ramValue.Text!="—"&&diskValue.Text!="—";
        double oldWidth=Width,oldHeight=Height;bool oldCompact=compact;compact=false;
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
        Width=oldWidth;Height=oldHeight;compact=oldCompact;UpdateLayout();AdaptLayout();UpdateLayout();
        File.WriteAllText(System.IO.Path.Combine(Root,"selftest.json"),JsonSerializer.Serialize(new{pin=pinOk,compact=compactOk,sensors,responsive=fits,layouts,providers=providers.ValueKind==JsonValueKind.Object,cpu=cpuValue.Text,memory=ramDetail.Text,drive=diskValue.Text}));
    }
    sealed class ProviderCard
    {
        public Border Element{get;}
        public string CompactValue=>value.Text.Replace(" left","");
        public double? Remaining=>value.Text=="—"||bar.Opacity<=.2?null:bar.Value;
        readonly TextBlock value=Text("—",22),detail=Text("Connecting…",10,Muted),status=Text("WAITING",9,Muted);
        readonly ProgressBar bar;readonly string name;
        readonly StackPanel labels=new();
        readonly Grid row=Columns(37,-1,115),readout=Columns(-1,-1);
        readonly TextBlock title;
        readonly Border mark;
        bool tileLayout;
        public ProviderCard(string key,string name,string icon,string color,Action click)
        {
            this.name=name;var stack=new StackPanel();
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
        public void MarkOffline(){status.Text="REFRESH FAILED";bar.Opacity=.3;detail.Text="Reading unavailable · click to connect";value.Text="—";Element.ToolTip=$"{name} · {status.Text}\n{detail.Text}";}
        public void Update(JsonElement data)
        {
            var state=data.GetProperty("status").GetString()??"Unavailable";status.Text=state.ToUpperInvariant();bar.Value=0;bar.Opacity=1;
            var age="";if(data.TryGetProperty("at",out var at)){var sec=Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeSeconds()-at.GetDouble());age=sec<60?"just now":sec<3600?$"{sec/60:0}m ago":$"{sec/3600:0}h ago";status.Text+=" · "+age.ToUpperInvariant();}
            var source=data.TryGetProperty("source",out var src)?src.GetString():"";Element.ToolTip=$"{name} · {source}\nClick to manage connection";
            if(data.TryGetProperty("windows",out var windows)&&windows.GetArrayLength()>0){
                var all=windows.EnumerateArray().ToList();var limited=all.MinBy(w=>w.GetProperty("remaining").GetDouble());
                double remain=limited.GetProperty("remaining").GetDouble();value.Text=$"{remain:0.#}% left";bar.Value=remain;
                var parts=all.Select(w=>$"{w.GetProperty("label").GetString()} {w.GetProperty("remaining").GetDouble():0.#}%").ToList();
                if(all.Count==1&&limited.TryGetProperty("reset",out var reset)&&reset.ValueKind==JsonValueKind.Number){var when=DateTimeOffset.FromUnixTimeSeconds((long)reset.GetDouble()).LocalDateTime;parts.Add($"resets {when:ddd HH:mm}");}
                detail.Text=string.Join(" · ",parts);
                if(state.Contains("Manual",StringComparison.OrdinalIgnoreCase)&&data.TryGetProperty("detail",out var manual))detail.Text=manual.GetString();
                if(state.Contains("Stale",StringComparison.OrdinalIgnoreCase)){bar.Opacity=.4;detail.Text="Last reported · "+detail.Text;}
            } else if(data.TryGetProperty("tokensUsed",out var tokens)){
                double n=tokens.GetDouble();value.Text=n>=1e6?$"{n/1e6:0.0}M":n>=1000?$"{n/1000:0.0}k":$"{n:0}";
                detail.Text="Go tokens used / 7 days · remaining quota needs setup";bar.Opacity=.2;
            }else{value.Text="—";detail.Text=data.TryGetProperty("detail",out var desc)?desc.GetString():"Click to connect";bar.Opacity=.2;}
            Element.ToolTip=$"{name} · {source}\n{status.Text}\n{detail.Text}\nClick to manage connection";
        }
    }
    sealed class MetricTile
    {
        public Border Element{get;}
        readonly TextBlock value=Text("—",20);
        readonly Canvas chart=new(){Height=22,Margin=new Thickness(0,4,0,0),ClipToBounds=true};
        readonly Polyline line;
        readonly ProgressBar meter;
        readonly string name;
        double fontSize=20;
        bool showChart;
        public MetricTile(string name,string icon,string color)
        {
            this.name=name;var content=new StackPanel();var row=Columns(24,-1);
            var mark=Text(icon,18);mark.Foreground=Brush(color);mark.VerticalAlignment=VerticalAlignment.Center;Add(row,mark,0);
            value.Foreground=Brush(color);value.TextWrapping=TextWrapping.NoWrap;value.HorizontalAlignment=HorizontalAlignment.Right;Add(row,value,1);content.Children.Add(row);
            line=new Polyline{Stroke=Brush(color),StrokeThickness=1.5};chart.Children.Add(line);content.Children.Add(chart);
            meter=Bar(color);meter.Height=2;meter.Margin=new Thickness(0,4,0,0);content.Children.Add(meter);
            Element=Panel(content,"#401D2A3E",12);Element.Padding=new Thickness(7,5,7,5);Element.Margin=new Thickness(0,0,5,5);Element.Focusable=true;
            System.Windows.Automation.AutomationProperties.SetName(Element,name);
        }
        public void Arrange(double width,bool charts){fontSize=width<140?17:21;showChart=charts;}
        public void Set(string text,string tooltip,double? percent,List<double>? history=null)
        {
            value.Text=text;value.FontSize=text.Length>8?Math.Min(fontSize,13):fontSize;
            Element.ToolTip=tooltip;System.Windows.Automation.AutomationProperties.SetName(Element,name+" · "+text);
            meter.Value=percent??0;meter.Opacity=percent.HasValue?1:.15;
            chart.Visibility=showChart&&history!=null?Visibility.Visible:Visibility.Collapsed;
            if(history!=null){double width=Math.Max(0,Element.ActualWidth-16);line.Points=new PointCollection(history.Select((v,i)=>new Point(i*width/35,20*(1-v/100))));}
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
