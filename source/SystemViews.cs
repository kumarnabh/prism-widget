using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Prism;

public sealed partial class Widget
{
    readonly TextBlock emptyMetrics=Text("No metrics selected. Open Settings to choose metrics.",13,Muted);
    HardwareSnapshot Hardware=>hardwareFixture??hardware.Latest;
    readonly List<object> systemLayouts=new();
    bool taskbarMinimize;LiveTaskbarPreview? taskbarPreview;
    int MinimumMetricHeight(double width)
    {
        int columns=width>=1002?8:width>=522?4:2;
        int count=preferences.MetricOrder.Count(MetricVisible);
        int baseline=width>=1002?120:width>=522?165:220;
        return count>8?Math.Max(baseline,86+(int)Math.Ceiling(count/(double)columns)*34):baseline;
    }
    void ReadSystemSnapshot()
    {
        var now=DateTimeOffset.UtcNow;var sample=Hardware;
        SystemMetric? Get(string id)=>sample.Metrics.FirstOrDefault(m=>m.Id==id&&m.Current(now));
        var cpu=Get("cpu");cpuReady=cpu is not null;cpuValue.Text=cpu?.Display(now)??"—";cpuGauge.Set(cpu?.Value??0);
        if(cpu?.Value is double usage)Plot(cpuHistory,cpuGraph,usage);else cpuHistory.Clear();
        var ram=Get("ram");memoryReady=ram is not null;ramValue.Text=ram?.Display(now)??"—";ramDetail.Text=ram?.Details??L.T("Unavailable");ramBar.Value=ram?.Value??0;ramGauge.Set(ram?.Value??0);
        if(ram?.Value is double memory)Plot(ramHistory,ramGraph,memory);else ramHistory.Clear();
        var disk=Get("disk");diskReady=disk is not null;freeDiskGb=disk?.Value??0;diskValue.Text=disk is not null?L.F("{0:0.0} GB free",disk.Value!.Value):"—";diskBar.Value=disk?.Percent??0;
        var net=Get("net");netValue.Text=net?.Value is double download&&double.TryParse(net.Details,NumberStyles.Float,CultureInfo.InvariantCulture,out double upload)?Rate(download)+" / "+Rate(upload):"—";
    }
    void SyncHardwareTiles()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(string id in WidgetPreferences.ExtraMetricIds){var value=Hardware.Metrics.FirstOrDefault(m=>m.Id==id);
            string detail=value?.Details??"";string state=value?.State(now)??"Unavailable";
            metricTiles[id].Set(value?.Display(now)??"—",L.T(MetricName(id))+" · "+L.T(state)+(detail.Length>0?" · "+detail:"")+(value is not null?"\n"+L.F("Captured: {0}",value.At.LocalDateTime.ToString("g")):""),value?.Current(now)==true?value.Percent:null,health:state=="Current"?"ready":state=="Stale"?"stale":"unknown");
        }
    }
    void ShowSystemCapacity()
    {
        var dialog=FeatureWindow("System capacity",700,700);var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("System capacity",24));stack.Children.Add(Text("Windows-reported values. Unsupported sensors stay unavailable.",12,Muted));
        var readings=new StackPanel{Margin=new Thickness(0,14,0,14)};stack.Children.Add(readings);
        void Update(){readings.Children.Clear();var sample=Hardware;var now=DateTimeOffset.UtcNow;
            foreach(var metric in sample.Metrics.Where(m=>m.Id is not "gpu" and not "vram" and not "net")){
                var block=new StackPanel();block.Children.Add(Text(L.T(metric.Label)+"  "+metric.Display(now),17));
                string state=L.T(metric.State(now));if(metric.Details.Length>0)state+=" · "+LocalizeHardwareDetails(metric.Details);block.Children.Add(Text(state,11,Muted));var panel=Panel(block);panel.Margin=new Thickness(0,0,0,8);readings.Children.Add(panel);
            }
            if(sample.Adapters.Length==0)readings.Children.Add(Text("GPU metrics unavailable",12,Muted));
            foreach(var disk in sample.Disks){bool fresh=now>=disk.At&&now-disk.At<=TimeSpan.FromSeconds(60);readings.Children.Add(Text(disk.Name+"  "+(fresh?L.F("{0:0.0} GB free",disk.Free/1073741824d)+$" / {disk.Total/1073741824d:0.#} GB":L.T("Stale")),13));}
        }Update();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};timer.Tick+=(_,_)=>Update();dialog.Loaded+=(_,_)=>timer.Start();dialog.Closed+=(_,_)=>timer.Stop();
        stack.Children.Add(Button("Top resource consumers","Top resource consumers",()=>{dialog.Close();ShowConsumers();}));
        stack.Children.Add(Text("Temperature, package power and battery health are unavailable without dependable hardware support.",11,Muted));
        CaptureFeature(dialog,"system");dialog.ShowDialog();
    }
    static string LocalizeHardwareDetails(string detail){foreach(string state in new[]{"Charging","Discharging","Plugged in","Unknown"})if(detail.StartsWith(state,StringComparison.Ordinal))return L.T(state)+detail[state.Length..];return detail;}
    void ShowConsumers()
    {
        var dialog=FeatureWindow("Top resource consumers",640,520);dialog.SizeToContent=SizeToContent.Height;var panel=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("Top resource consumers",22));panel.Children.Add(Text("Process names and current usage stay local and are never saved.",12,Muted));
        var rows=new StackPanel{Margin=new Thickness(0,16,0,16)};panel.Children.Add(rows);rows.Children.Add(Text("Reading…",14));var stop=new CancellationTokenSource();dialog.Closed+=(_,_)=>stop.Cancel();
        Button refresh=null!;async Task Update(){refresh.IsEnabled=false;try{
            var values=selftest?new[]{new ProcessCapacity(10,"Example editor",1,0,512*1048576,14),new ProcessCapacity(11,"Example browser",1,0,240*1048576,4)}:await ResourceConsumers.Read(stop.Token);
            if(stop.IsCancellationRequested)return;rows.Children.Clear();
            foreach(bool cpu in new[]{true,false}){rows.Children.Add(Text(cpu?"CPU":"Memory",16));foreach(var row in (cpu?values.OrderByDescending(p=>p.CpuPercent):values.OrderByDescending(p=>p.Memory)).Take(5))rows.Children.Add(Text(row.Name+"  · "+(cpu?$"{row.CpuPercent:0.#}%":$"{row.Memory/1048576d:0.#} MB"),13));}
        }catch(OperationCanceledException){}catch{if(!stop.IsCancellationRequested){rows.Children.Clear();rows.Children.Add(Text("Unavailable",13));}}finally{if(!stop.IsCancellationRequested)refresh.IsEnabled=true;}}
        refresh=Button("Refresh","Refresh",()=>_ = Update());panel.Children.Add(refresh);
        panel.Children.Add(Button("Open Task Manager","Open Task Manager",()=>{if(!selftest)Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"Taskmgr.exe")){UseShellExecute=true});}));
        dialog.Loaded+=async (_,_)=>await Update();CaptureFeature(dialog,"consumers");dialog.ShowDialog();
    }
    void InitializeTaskbar()
    {
        if(preview||selftest)return;
        try{taskbarPreview=new(this,TaskbarRows);}catch{taskbarPreview=null;}
        var restore=new ThumbButtonInfo{Description=L.T("Show Prism"),ImageSource=Icon};restore.Click+=(_,_)=>ShowFromTray();
        TaskbarItemInfo=new(){Description="Prism · "+L.T("System capacity"),ThumbButtonInfos=new(){restore}};
    }
    void MinimizeToTaskbar(){taskbarMinimize=true;ShowInTaskbar=true;WindowState=WindowState.Minimized;taskbarPreview?.Invalidate();}
    void UpdateTaskbarProgress()
    {
        if(TaskbarItemInfo is null)return;string id=preferences.TaskbarMetric;double? percent=TaskbarPercent(id,DateTimeOffset.UtcNow);
        TaskbarItemInfo.ProgressState=percent.HasValue?TaskbarItemProgressState.Normal:TaskbarItemProgressState.None;
        TaskbarItemInfo.ProgressValue=percent.HasValue?Math.Clamp(percent.Value/100,0,1):0;
        TaskbarItemInfo.Description=percent.HasValue?"Prism · "+L.T(MetricName(id))+$" {percent:0.#}%":"Prism · "+L.T("System capacity");
    }
    double? TaskbarPercent(string id,DateTimeOffset now)
    {
        double? percent=null;
        if(MetricVisible(id)){
            if(WidgetPreferences.ProviderIds.Contains(id)&&providerData.TryGetValue(id,out var data)){var reading=QuotaSnapshot.Read(data,now);if(UsageHistory.Fresh(reading,now))percent=reading.Remaining;}
            else{var metric=Hardware.Metrics.FirstOrDefault(m=>m.Id==id);if(metric?.Current(now)==true)percent=metric.Percent;}
        }
        return percent;
    }
    void DockRibbon()
    {
        ShowFromTray();compact=true;var work=DesktopLayout.Current(this).Work;Width=Math.Min(1200,work.Width);Height=MinimumMetricHeight(Width);Left=work.Left+(work.Width-Width)/2;Top=work.Bottom-Height;AdaptLayout();SavePosition();
    }
    public record BarMetric(string Label,string Value,double? Percent,string State,bool Capacity=false);
    BarMetric[] TaskbarRows()
    {
        var now=DateTimeOffset.UtcNow;return preferences.MetricOrder.Where(MetricVisible).Select(id=>{
            if(WidgetPreferences.ProviderIds.Contains(id)){
                var reading=providerData.TryGetValue(id,out var data)?QuotaSnapshot.Read(data,now):null;bool fresh=reading is not null&&UsageHistory.Fresh(reading,now);
                return new BarMetric(ProviderName(id),reading?.Remaining is double remaining?$"{remaining:0.#}%":"—",fresh?reading?.Remaining:null,reading?.Status??"Unavailable",true);
            }
            var metric=Hardware.Metrics.FirstOrDefault(m=>m.Id==id);return new BarMetric(L.T(MetricName(id)),metric?.Display(now)??"—",metric?.Current(now)==true?metric.Percent:null,metric?.State(now)??"Unavailable");
        }).ToArray();
    }
    void VerifySystemViews()
    {
        var saved=preferences.Copy();var savedFixture=hardwareFixture;var now=DateTimeOffset.UtcNow;
        var metrics=WidgetPreferences.ExtraMetricIds.Select(id=>new SystemMetric(id,MetricName(id),id=="frequency"?3.4:id=="vram"?3.1:35,id is "gpu" or "battery"?"%":id=="frequency"?" GHz":id=="vram"?" GB":" MB/s",35,now)).Concat(new[]{new SystemMetric("gpu:fixture","Example GPU",35,"%",35,now),new SystemMetric("vram:fixture","Dedicated VRAM",3.1," GB",38.75,now,Details:"8 GB total · 4.9 GB free")}).ToArray();
        hardwareFixture=new(metrics,new[]{new GpuAdapter("fixture","Example GPU",8UL*1073741824)},new[]{new DiskCapacity("C:\\",50L*1073741824,500L*1073741824,now),new DiskCapacity("D:\\",100L*1073741824,1000L*1073741824,now)},now,0);
        foreach(string language in new[]{"en","hi","es","fr"}){
            L.Set(language);ShowSystemCapacity();ShowConsumers();
            foreach(var visible in new[]{WidgetPreferences.MetricIds,Array.Empty<string>(),new[]{"gpu","vram"}})foreach(var size in new[]{(340d,220d),(442d,858d),(560d,165d),(840d,800d),(1200d,120d)}){
                preferences.VisibleMetrics=visible.ToList();ApplyMetricPreferences();ApplyPreset(size.Item1,size.Item2);UpdateLayout();AdaptLayout();UpdateLayout();var check=CheckLayout();if(metricTiles["vram"].Label!=L.T("Dedicated VRAM"))throw new InvalidOperationException("Metric label follows current language");systemLayouts.Add(new{language,metrics=visible.Length,width=ActualWidth,height=ActualHeight,check.fits,check.controls,check.scale});
                if(language=="en"&&visible.Length==WidgetPreferences.MetricIds.Length)SavePreview($"preview-system-grid-{(int)size.Item1}x{(int)size.Item2}.png",2);
            }
        }
        preferences.VisibleMetrics=new(){"battery"};hardwareFixture=hardwareFixture with{BatteryPresent=false};if(MetricVisible("battery"))throw new InvalidOperationException("Desktop omits battery metric");
        hardwareFixture=hardwareFixture with{BatteryPresent=null};if(!MetricVisible("battery"))throw new InvalidOperationException("Unknown battery remains available as unknown");
        var savedData=providerData.ToDictionary(p=>p.Key,p=>p.Value);providerData.Clear();preferences.ActiveProvidersOnly=true;preferences.VisibleMetrics=new(){"codex","cursor","claude"};ApplyMetricPreferences();AdaptLayout();
        if(metricGrid.Children.Count!=0||emptyMetrics.Visibility!=Visibility.Visible)throw new InvalidOperationException("Active-only empty state");
        using(var fixture=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{status="Live",at=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),windows=new[]{new{label="Session",remaining=55d}}})))foreach(var id in preferences.VisibleMetrics)providerData[id]=fixture.RootElement.Clone();
        ReconcileMetricVisibility();if(metricGrid.Children.Count!=3||emptyMetrics.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Active-only connection layout");
        if(TaskbarPercent("codex",DateTimeOffset.UtcNow)!=55||TaskbarPercent("ram",DateTimeOffset.UtcNow)!=null)throw new InvalidOperationException("Taskbar respects capacity and visibility");
        using(var stale=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{status="Live",at=DateTimeOffset.UtcNow.AddMinutes(-11).ToUnixTimeSeconds(),windows=new[]{new{label="Session",remaining=55d}}})))providerData["codex"]=stale.RootElement.Clone();
        if(TaskbarPercent("codex",DateTimeOffset.UtcNow)!=null)throw new InvalidOperationException("Taskbar rejects stale capacity");
        providerData.Clear();ReconcileMetricVisibility();if(metricGrid.Children.Count!=0||emptyMetrics.Visibility!=Visibility.Visible)throw new InvalidOperationException("Active-only disconnection layout");foreach(var item in savedData)providerData[item.Key]=item.Value;
        L.Set("en");hardwareFixture=savedFixture;preferences=saved;ApplyMetricPreferences();
        var bars=LiveTaskbarPreview.Render(new[]{new BarMetric("CPU","24%",24,"Current"),new("GPU","37%",37,"Current"),new("Memory","58%",58,"Current"),new("Codex","42%",42,"Live",true),new("Cursor","68%",null,"Stale",true)},400,260);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bars));using var file=System.IO.File.Create(System.IO.Path.Combine(Root,"preview-taskbar.png"));encoder.Save(file);
    }
}
