using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Prism;

public sealed partial class Widget
{
    readonly HashSet<string> collecting=new();
    readonly Dictionary<string,CancellationTokenSource> providerRequests=new();
    void CancelProvider(string id){if(providerRequests.TryGetValue(id,out var request))request.Cancel();}
    readonly Dictionary<string,DateTimeOffset> lastSuccess=new();
    readonly List<object> ecosystemLayouts=new();
    void VerifyCursorDisplay(DateTimeOffset now)
    {
        foreach(var scenario in new[]{(value:(double?)73,expired:false,age:0),(value:(double?)0,expired:false,age:0),(value:(double?)100,expired:false,age:0),(value:(double?)null,expired:false,age:0),(value:(double?)73,expired:true,age:0),(value:(double?)73,expired:false,age:601)}){
            var windows=new List<object>{new{id="apiPercentUsed",label="Other models",remaining=7d,reset=(long?)null},new{id="totalPercentUsed",label="Included",remaining=31d,reset=(long?)now.AddHours(2).ToUnixTimeSeconds()}};
            if(scenario.value is double current)windows.Add(new{id="autoPercentUsed",label="Cursor models",remaining=current,reset=(long?)now.AddHours(scenario.expired?-1:4).ToUnixTimeSeconds()});
            using var fixture=JsonDocument.Parse(JsonSerializer.Serialize(new{status="Live",at=now.AddSeconds(-scenario.age).ToUnixTimeSeconds(),windows}));
            var reading=QuotaSnapshot.Read(fixture.RootElement,now);double? expected=scenario.expired?null:scenario.value;
            if(reading.DashboardRemaining("cursor")!=expected||reading.DashboardRemaining("codex")!=reading.Remaining)throw new InvalidOperationException("Cursor primary category selection");
            if(expected.HasValue&&reading.ResetSummary(now,"cursor")!="Resets in 4h"||!expected.HasValue&&reading.ResetSummary(now,"cursor")!="")throw new InvalidOperationException("Cursor primary reset selection");
            providerData["cursor"]=fixture.RootElement.Clone();cards["cursor"].Update(fixture.RootElement);
            if(cards["cursor"].Remaining!=expected||!cards["cursor"].Element.ToolTip.ToString()!.Contains("Other models 7%"))throw new InvalidOperationException("Cursor card retains category details");
            foreach(var density in new[]{(level:0,width:400d),(level:1,width:160d)}){
                cards["cursor"].SetDensity(density.level,density.width);string value=expected is double percentage?$"{percentage:0.#}%":"—";
                if(cards["cursor"].CompactValue!=value||!System.Windows.Automation.AutomationProperties.GetName(cards["cursor"].Element).Contains(value))throw new InvalidOperationException("Cursor full/compact/accessibility value");
            }
            double? fresh=scenario.age==0?expected:null;var row=TaskbarRows().Single(r=>r.Label==ProviderName("cursor"));
            if(TaskbarPercent("cursor",now)!=fresh||row.Percent!=fresh||row.Value!=cards["cursor"].CompactValue)throw new InvalidOperationException("Cursor taskbar display/freshness");
            if(scenario.age>0&&cards["cursor"].Health!="stale")throw new InvalidOperationException("Cursor stale display state");
        }
    }
    void VerifyProviderViews()
    {
        var saved=preferences.Copy();var previousHardware=hardwareFixture;var savedData=providerData.ToDictionary(p=>p.Key,p=>p.Value);var now=DateTimeOffset.UtcNow;
        foreach(string state in new[]{"Connect","Unavailable"}){using var error=JsonDocument.Parse(JsonSerializer.Serialize(new{status=state,detail="Connection guidance fixture",windows=Array.Empty<object>()}));cards["codex"].Update(error.RootElement);if(cards["codex"].Remaining is not null||!cards["codex"].Element.ToolTip.ToString()!.Contains("Connection guidance fixture"))throw new InvalidOperationException("Empty quota preserves connection guidance");}
        preferences.DisabledProviders.Clear();preferences.VisibleMetrics=WidgetPreferences.MetricIds.ToList();preferences.ActiveProvidersOnly=false;VerifyCursorDisplay(now);
        var metrics=new[]{new SystemMetric("cpu","CPU",24,"%",24,now),new("ram","Memory",58,"%",58,now,Details:"9.3 / 16 GB"),new("disk","Free disk",240," GB",48,now),new("net","Network",1048576," B/s",null,now,Details:"524288")}.Concat(WidgetPreferences.ExtraMetricIds.Select(id=>new SystemMetric(id,MetricName(id),id=="frequency"?3.4:id=="vram"?3.1:35,id is "gpu" or "battery"?"%":id=="frequency"?" GHz":id=="vram"?" GB":" MB/s",id is "gpu" or "battery"?35:id=="vram"?38.75:null,now))).ToArray();
        hardwareFixture=new(metrics,Array.Empty<GpuAdapter>(),Array.Empty<DiskCapacity>(),now,0,true);cpuHistory.Clear();ramHistory.Clear();ReadSystemSnapshot();
        foreach(string language in new[]{"en","hi","es","fr"}){
            L.Set(language);
            foreach(var definition in ProviderCatalog.All){using var fixture=JsonDocument.Parse(JsonSerializer.Serialize(new{schema_version=1,provider_id=definition.Id,status="Live",source=definition.Source,detail="Synthetic example",at=now.ToUnixTimeSeconds(),windows=definition.Id=="cursor"?new[]{new{id="autoPercentUsed",label="Cursor models",remaining=73d,reset=(long?)null},new{id="apiPercentUsed",label="Other models",remaining=7d,reset=(long?)null},new{id="totalPercentUsed",label="Included",remaining=31d,reset=(long?)null}}:new[]{new{id="example",label="Allowance",remaining=42d,reset=(long?)null}}}));providerData[definition.Id]=fixture.RootElement.Clone();cards[definition.Id].Update(fixture.RootElement);}
            ApplyMetricPreferences();ShowProviders();ShowOpenRouter();
            foreach(var size in new[]{(340d,220d),(442d,858d),(560d,165d),(840d,800d),(1200d,120d),(420d,640d)}){
                compact=size.Item2<640;Width=size.Item1;Height=size.Item2;UpdateLayout();AdaptLayout();UpdateLayout();var check=CheckLayout();
                bool selection=preferences.VisibleMetrics.All(id=>metricsOnly?metricTiles[id].Element.IsVisible:cards.TryGetValue(id,out var card)?card.Element.IsVisible:!expandedHardwareTiles.TryGetValue(id,out var tile)||tile.Element.IsVisible);
                ecosystemLayouts.Add(new{language,width=ActualWidth,height=ActualHeight,check.fits,check.controls,check.scale,selection,expanded=!metricsOnly});
                if(language=="en")SavePreview($"preview-ecosystem-{(int)size.Item1}x{(int)size.Item2}.png",2);
            }
        }
        preferences=saved;hardwareFixture=previousHardware;providerData.Clear();foreach(var item in savedData)providerData[item.Key]=item.Value;L.Set("en");ApplyMetricPreferences();
    }
    void ShowProviders()
    {
        var dialog=FeatureWindow("Providers",690,720);var panel=new StackPanel{Margin=new Thickness(24)};
        dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        bool dismissed=false;dialog.Closed+=(_,_)=>dismissed=true;
        panel.Children.Add(Text("Connections and providers",24));panel.Children.Add(Text("Read-only sources. Authentication stays with your provider.",12,Muted));
        foreach(var definition in ProviderCatalog.All){
            string id=definition.Id;var now=DateTimeOffset.UtcNow;var reading=providerData.TryGetValue(id,out var data)?QuotaSnapshot.Read(data,now):null;
            var content=new StackPanel();content.Children.Add(Text(definition.Name,18));
            bool enabled=!preferences.DisabledProviders.Contains(id);
            content.Children.Add(Text(L.T(enabled?reading?.Status??"Waiting":"Disabled")+" · "+definition.Source,12));
            content.Children.Add(Text(definition.Authentication,11,Muted));
            string last=lastSuccess.TryGetValue(id,out var success)?success.LocalDateTime.ToString("g"):reading?.Captured?.LocalDateTime.ToString("g")??L.T("Unknown");
            content.Children.Add(Text(L.F("Last successful update: {0}",last)+" · "+L.F("Every {0} seconds",preferences.RefreshSeconds[id]),11,Muted));
            content.Children.Add(Text(string.Join(" · ",definition.Capabilities.Select(c=>L.T(c switch{"remaining"=>"Remaining allowance","reset"=>"Reset time","multiple_windows"=>"Multiple windows","history"=>"Usage history","local_cli"=>"Local CLI","api_credits"=>"API credits",_=>c}))),11,Muted));
            if(reading is not null)content.Children.Add(Text(reading.Detail,11,Muted));
            var actionStatus=Text("",11,Muted);content.Children.Add(actionStatus);
            var buttons=new WrapPanel{Margin=new Thickness(0,10,0,0)};
            buttons.Children.Add(Button(enabled?"Disable provider":"Enable provider","Enable or disable collection",()=>{
                var next=preferences.Copy();if(enabled)next.DisabledProviders.Add(id);else{next.DisabledProviders.Remove(id);if(!next.VisibleMetrics.Contains(id))next.VisibleMetrics.Add(id);}
                try{next.Save(SettingsPath);preferences=next;CancelProvider(id);providerData.Remove(id);latestReadings.Remove(ProviderName(id));schedule.Reset();ApplyMetricPreferences();AdaptLayout();dialog.Close();ShowProviders();}catch(IOException){actionStatus.Text=L.T("Could not save settings. Check folder access.");}catch(UnauthorizedAccessException){actionStatus.Text=L.T("Could not save settings. Check folder access.");}
            }));
            buttons.Children.Add(Button("Connection guide","Connection guide",()=>{dialog.Close();if(id=="openrouter")ShowOpenRouter();else ShowConnection(id,definition.Name);}));
            var refresh=Button("Refresh","Refresh",async()=>{await UpdateProvider(id);if(!dismissed&&!closed){dialog.Close();ShowProviders();}});refresh.IsEnabled=enabled&&!collecting.Contains(id);buttons.Children.Add(refresh);content.Children.Add(buttons);
            var card=Panel(content);card.Margin=new Thickness(0,12,0,0);panel.Children.Add(card);
        }
        panel.Children.Add(Text("No prompts, identities or machine information are sent to Prism servers. There are no Prism servers.",11,Muted));
        CaptureFeature(dialog,"providers");dialog.ShowDialog();
    }
    void ShowOpenRouter()
    {
        var dialog=FeatureWindow("OpenRouter",580,480);dialog.SizeToContent=SizeToContent.Height;var panel=new StackPanel{Margin=new Thickness(24)};dialog.Content=panel;
        panel.Children.Add(Text("OpenRouter",24));panel.Children.Add(Text("Prism can reuse an existing OPENROUTER_API_KEY environment variable for a read-only request to https://openrouter.ai/api/v1/key. The key goes only to OpenRouter, never to logs or quota caches. No model requests are made.",13));
        panel.Children.Add(Text("This shows the key's spending allowance, not your total account balance or tokens. Unlimited keys have unknown remaining capacity. Exact reset timestamps are unavailable.",12,Muted));
        var settings=LocalJson.Load<Dictionary<string,bool>>(Path.Combine(Data,"connections.json"))??new();
        var allow=Check(panel,"Allow existing OpenRouter environment sign-in",settings.GetValueOrDefault("allowOpenRouter"));
        var status=Text("",12,Muted);panel.Children.Add(status);
        panel.Children.Add(Button("Save","Save",()=>{try{settings["allowOpenRouter"]=allow.IsChecked==true;LocalJson.Save(Path.Combine(Data,"connections.json"),settings);CancelProvider("openrouter");providerData.Remove("openrouter");schedule.Reset();dialog.Close();}catch(IOException){status.Text=L.T("Could not save settings. Check folder access.");}catch(UnauthorizedAccessException){status.Text=L.T("Could not save settings. Check folder access.");}}));
        CaptureFeature(dialog,"openrouter");dialog.ShowDialog();
    }
    async Task UpdateProvider(string id)
    {
        if(closed||preferences.DisabledProviders.Contains(id)||!collecting.Add(id))return;
        using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);providerRequests[id]=request;
        busy=true;schedule.Mark(new[]{id},DateTimeOffset.UtcNow,preferences);
        try{
            var data=await ProviderRunner.Collect(Root,id,request.Token);request.Token.ThrowIfCancellationRequested();
            if(closed||preferences.DisabledProviders.Contains(id))return;
            providerData[id]=data;var now=DateTimeOffset.UtcNow;var reading=QuotaSnapshot.Read(data,now);
            if(UsageHistory.Fresh(reading,now)&&reading.Captured.HasValue)lastSuccess[id]=reading.Captured.Value;
            if(!preview&&!selftest&&preferences.HistoryEnabled&&history.Record(id,reading,now))try{history.Save(HistoryPath);}catch(IOException){}catch(UnauthorizedAccessException){}
        }catch(Exception){if(!closed){providerData.Remove(id);latestReadings.Remove(ProviderName(id));cards[id].MarkOffline();}}
        finally{
            providerRequests.Remove(id);collecting.Remove(id);busy=collecting.Count>0;
            if(!closed){using var merged=JsonDocument.Parse(JsonSerializer.Serialize(providerData));providers=merged.RootElement.Clone();RefreshForecasts(true);RefreshDisplayedProviders();ReconcileMetricVisibility();AdaptLayout();CheckResetReminders();CheckLowQuota();CheckCapacityAlerts();}
        }
    }
}
