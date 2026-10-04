using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Prism;

public sealed class NotificationState
{
    public long LastDelivery {get;set;}
    public Dictionary<string,long> Forecasts {get;set;}=new();
    public bool Available(DateTimeOffset now)=>LastDelivery<=now.ToUnixTimeSeconds()-120||LastDelivery>now.ToUnixTimeSeconds()+300;
    public void Mark(DateTimeOffset now){LastDelivery=now.ToUnixTimeSeconds();Forecasts=(Forecasts??new()).Where(p=>p.Value>now.AddDays(-30).ToUnixTimeSeconds()).TakeLast(256).ToDictionary(p=>p.Key,p=>p.Value);}
}

public sealed partial class Widget
{
    string HistoryPath=>System.IO.Path.Combine(Data,"usage-history-v2.json");
    NotificationState notifications=new();
    readonly Dictionary<string,long> forecastCandidates=new();
    Window? trayView;
    DateTimeOffset forecastAt=DateTimeOffset.MinValue;
    DateTimeOffset alertCheckAt=DateTimeOffset.MinValue;
    readonly Dictionary<string,CapacityEstimate> forecasts=new();
    static string ForecastKey(string provider,QuotaWindow window)=>provider+"\u001f"+(string.IsNullOrEmpty(window.Id)?window.Label:window.Id);
    void RefreshForecasts(bool force=false)
    {
        var now=DateTimeOffset.UtcNow;if(!force&&now-forecastAt<TimeSpan.FromSeconds(30))return;
        forecastAt=now;forecasts.Clear();if(!preferences.ForecastEnabled)return;
        foreach(var (id,data) in providerData){var reading=QuotaSnapshot.Read(data,now);foreach(var window in reading.Windows)forecasts[ForecastKey(id,window)]=CapacityForecast.Calculate(history.Samples,id,reading,window,now);}
    }
    bool DeliverNotice(string title,IReadOnlyList<string> messages)
    {
        var now=DateTimeOffset.UtcNow;if(!notifications.Available(now)||messages.Count==0)return false;
        bool sent;
        if(!IsVisible||WindowState==WindowState.Minimized)sent=tray?.Notify(L.T(title),string.Join("\n",messages))==true;
        else{DismissReminder();var stack=new StackPanel();stack.Children.Add(Text(title,14));foreach(var message in messages.Take(3))stack.Children.Add(Text(message,12));var panel=Panel(stack,"#F02A344A",14);panel.Width=310;panel.ToolTip=string.Join("\n",messages);reminderPopup.Child=panel;reminderPopup.IsOpen=true;reminderTimer.Start();sent=true;}
        if(sent){notifications.Mark(now);SaveNotifications();}return sent;
    }
    void SaveNotifications(){try{LocalJson.Save(System.IO.Path.Combine(Data,"notification-state.json"),notifications);}catch(IOException){}catch(UnauthorizedAccessException){}}
    void CheckCapacityAlerts()
    {
        if(preview||selftest||closed||!preferences.ForecastEnabled||!preferences.CapacityAlerts||reminderPopup.IsOpen)return;
        var now=DateTimeOffset.UtcNow;if(now-alertCheckAt<TimeSpan.FromSeconds(30))return;alertCheckAt=now;
        notifications.Forecasts??=new();
        foreach(var (id,data) in providerData){var reading=QuotaSnapshot.Read(data,now);if(!UsageHistory.Fresh(reading,now)||reading.Captured is not DateTimeOffset capture)continue;
            foreach(var window in reading.Windows){
                string key=JsonSerializer.Serialize(new[]{id,reading.Scope,window.Id,window.Reset?.ToUnixTimeSeconds().ToString()});
                var forecast=CapacityForecast.Calculate(history.Samples,id,reading,window,now);
                if(notifications.Forecasts.ContainsKey(key))continue;
                if(!CapacityForecast.Confirm(forecastCandidates,key,reading,window,forecast,now))continue;
                if(DeliverNotice("Capacity estimate",new[]{L.F("{0} may exhaust before reset at the recent pace.",ProviderName(id)+" · "+window.Label)})){notifications.Forecasts[key]=now.ToUnixTimeSeconds();SaveNotifications();}return;
            }
        }
    }
    void ShowCapacity()
    {
        RefreshForecasts(true);var window=FeatureWindow("Capacity & resets",680,680);window.SizeToContent=SizeToContent.Height;var stack=new StackPanel{Margin=new Thickness(24)};window.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Next resets",23));stack.Children.Add(Text("Provider readings and local estimates. Estimates are not guarantees.",12,Muted));
        var list=new StackPanel();stack.Children.Add(list);
        void Update(){list.Children.Clear();RefreshForecasts(true);
        var now=DateTimeOffset.UtcNow;
        var rows=providerData.SelectMany(pair=>{var reading=QuotaSnapshot.Read(pair.Value,now);return reading.Windows.Select(w=>(id:pair.Key,reading,window:w));}).OrderBy(r=>r.window.Reset??DateTimeOffset.MaxValue).ToArray();
        if(rows.Length==0)list.Children.Add(Text("No current quota windows",14));
        foreach(var row in rows){
            var block=new StackPanel();block.Children.Add(Text(ProviderName(row.id)+" · "+row.window.Label,16));
            string reset=row.window.Reset is DateTimeOffset at?at.LocalDateTime.ToString(preferences.Use24Hour?"ddd HH:mm":"ddd h:mm tt")+" · "+Until(at-now):L.T("Unknown reset");
            block.Children.Add(Text(reset+" · "+$"{row.window.Remaining:0.#}%"+" · "+L.T(row.reading.Status)+(row.window.Remaining==row.reading.Remaining?" · "+L.T("Limiting window"):""),12));
            if(row.reading.Captured is DateTimeOffset captured)block.Children.Add(Text(L.F("Captured: {0}",captured.LocalDateTime.ToString("g")),11,Muted));
            if(preferences.ForecastEnabled&&forecasts.TryGetValue(ForecastKey(row.id,row.window),out var forecast))block.Children.Add(Text(forecast.Describe(),12,Muted));
            var panel=Panel(block);panel.Margin=new Thickness(0,10,0,0);list.Children.Add(panel);
        }
        }Update();UpdateWhileOpen(window,Update);
        CaptureFeature(window,"capacity");window.ShowDialog();
    }
    void ShowTrayCapacity()
    {
        if(trayView is not null){trayView.Activate();return;}
        var window=new Window{Title="Prism · "+L.T("Capacity"),Width=340,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-24,WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Background=Brush("#17273D"),Foreground=new SolidColorBrush(Ink),FontFamily=FontFamily};
        var stack=new StackPanel{Margin=new Thickness(18)};window.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};stack.Children.Add(Text("PRISM",20));
        var system=Text("",16);stack.Children.Add(system);
        var list=new StackPanel();stack.Children.Add(list);
        void Update(){system.Text="CPU "+cpuValue.Text+"  ·  "+L.T("Memory")+" "+ramValue.Text;list.Children.Clear();var now=DateTimeOffset.UtcNow;foreach(var id in WidgetPreferences.ProviderIds.Where(id=>!preferences.DisabledProviders.Contains(id))){
            var reading=providerData.TryGetValue(id,out var data)?QuotaSnapshot.Read(data,now):null;
            string value=reading?.Remaining is double remaining?$"{remaining:0.#}%":"—";
            string state=reading?.Status??"Unavailable";if(reading?.Remaining is not null&&UsageHistory.Fresh(reading,now))state=reading.Remaining<=10?"Critical capacity":reading.Remaining<=25?"Low capacity":state;
            var row=Text(ProviderName(id)+"  "+value+" · "+L.T(state),13);row.Margin=new Thickness(0,8,0,0);list.Children.Add(row);
        }
        var next=providerData.SelectMany(p=>{var reading=QuotaSnapshot.Read(p.Value,now);return reading.Windows.Where(w=>w.Reset>now&&UsageHistory.Fresh(reading,now)).Select(w=>(id:p.Key,window:w));}).OrderBy(x=>x.window.Reset).FirstOrDefault();
        list.Children.Add(Text(next.window is null?L.T("Unknown reset"):L.T("Next resets")+" · "+ProviderName(next.id)+" · "+Until(next.window.Reset!.Value-now),12,Muted));
        }Update();UpdateWhileOpen(window,Update);
        stack.Children.Add(Button("Capacity & resets","Capacity & resets",()=>{window.Close();ShowFromTray();ShowCapacity();}));stack.Children.Add(Button("Show Prism","Show Prism",()=>{window.Close();ShowFromTray();}));
        bool closing=false;trayView=window;window.Closing+=(_,_)=>closing=true;window.Closed+=(_,_)=>trayView=null;window.Deactivated+=(_,_)=>{if(!closing&&trayView==window)window.Close();};window.Loaded+=(_,_)=>{var area=DesktopLayout.Current(this).Work;window.Left=area.Right-window.ActualWidth-12;window.Top=area.Bottom-window.ActualHeight-12;};CaptureFeature(window,"tray");if(selftest)window.ShowDialog();else{window.Show();if(!closing)window.Activate();}
    }
    static string Until(TimeSpan span)=>span.TotalDays>=1?L.F("{0:0.#} days",span.TotalDays):span.TotalHours>=1?L.F("{0:0.#} hours",span.TotalHours):L.F("{0:0} minutes",Math.Max(0,span.TotalMinutes));
    static void UpdateWhileOpen(Window window,Action update)
    {
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};timer.Tick+=(_,_)=>update();window.Loaded+=(_,_)=>timer.Start();window.Closed+=(_,_)=>timer.Stop();
    }
    void CaptureFeature(Window dialog,string name)
    {
        if(!selftest)return;
        dialog.Loaded+=(_,_)=>{dialog.UpdateLayout();var visual=(FrameworkElement)dialog.Content;
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*2),(int)Math.Ceiling(visual.ActualHeight*2),192,192,PixelFormats.Pbgra32);
            var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(dialog.Background,null,new Rect(0,0,visual.ActualWidth,visual.ActualHeight));bitmap.Render(background);
            bitmap.Render(VectorSnapshot.Capture(visual));var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var file=File.Create(System.IO.Path.Combine(Root,"preview-"+name+"-"+L.Language+".png")))png.Save(file);dialog.Dispatcher.BeginInvoke(dialog.Close);
        };
    }
    void VerifyCapacityViews()
    {
        var saved=providerData.ToDictionary(p=>p.Key,p=>p.Value);var savedHistory=history;history=new();var now=DateTimeOffset.UtcNow;string scope=new('a',64);long reset=now.AddHours(3).ToUnixTimeSeconds();
        using var fixture=JsonDocument.Parse(JsonSerializer.Serialize(new{status="Live",at=now.ToUnixTimeSeconds(),scope,windows=new[]{new{id="session",label="Session",remaining=68d,reset},new{id="weekly",label="Weekly",remaining=87d,reset=now.AddDays(3).ToUnixTimeSeconds()}}}));
        providerData["codex"]=fixture.RootElement.Clone();history.Samples=Enumerable.Range(0,13).Select(i=>new WindowSample("codex",scope,"session",now.AddMinutes((i-12)*5).ToUnixTimeSeconds(),80-i,reset)).ToList();
        foreach(string language in new[]{"en","hi","es","fr"}){L.Set(language);ShowCapacity();ShowHistory();ShowTrayCapacity();}
        providerData.Clear();foreach(var item in saved)providerData[item.Key]=item.Value;history=savedHistory;L.Set("en");RefreshForecasts(true);
    }
}
