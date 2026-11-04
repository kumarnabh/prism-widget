using System.IO;
using System.Text.Json;

namespace Prism;

public static class GitHubChecks
{
    public static JsonElement Fixture(DateTimeOffset now,double spend=8)
    {
        var metrics=GitHubCatalog.All.Select(m=>new{id=m.Id,unit=m.Unit,value=m.Category is "allowance" or "forecast"?(double?)null:m.Id=="github-spend"?spend:m.Unit=="bytes"?256*1048576d:12d,state=m.Category is "allowance" or "forecast"?"Unknown":"Live",source=m.Id is "github-runs" or "github-successes" or "github-failures" or "github-artifacts" or "github-cache"?"repository":"billing"});
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{schema_version=1,provider_id="github",status="Live",at=now.ToUnixTimeSeconds(),scope=new string('a',64),period=now.UtcDateTime.ToString("yyyy-MM"),metrics,repositories=new[]{new{scope=new string('b',64),usd=spend,name="example/project"}}}));return doc.RootElement.Clone();
    }
    public static bool Verify(string root)
    {
        void Require(bool value,string label){if(!value)throw new InvalidOperationException("GitHub check failed: "+label);}
        var now=new DateTimeOffset(2026,11,4,12,0,0,TimeSpan.Zero);var reading=GitHubReading.Read(Fixture(now,0),now);
        Require(QuotaDisplay.Missing("Permission")==L.T("No access")&&QuotaDisplay.Missing("Connect")==L.T("Connect")&&QuotaDisplay.Missing("Unavailable")==L.T("Retry"),"unavailable cards explain their state without a blank dash");
        Require(GitHubConnection.ModeForSave("cli",32)=="credential"&&GitHubConnection.ModeForSave("cli",0)=="cli"&&GitHubConnection.ModeForSave("credential",0)=="credential","entered credential cannot be silently discarded by CLI selection");
        Require(reading.Fresh(now)&&reading.Metric("github-spend").Value==0&&reading.Metric("github-actions-allowance").Value is null,"typed zero and unknown allowance");
        Require(reading.Repositories.Single().Name=="example/project","validated private repository drill-down labels");
        Require(GitHubReading.Read(Fixture(now),now.AddSeconds(3601)).Status=="Stale","freshness independent of schedule");
        Require(!reading.Fresh(now.AddMonths(1)),"billing month does not leak across reset");
        string serialized=Fixture(now).GetRawText();
        foreach(string invalid in new[]{serialized.Replace("\"unit\":\"USD\"","\"unit\":\"GB\""),serialized.Replace("\"value\":8","\"value\":-1"),serialized.Replace(new string('a',64),"private-account")})using(var doc=JsonDocument.Parse(invalid))Require(GitHubReading.Read(doc.RootElement,now).Status=="Unavailable","corrupt wire rejected");
        var history=new GitHubHistory();
        for(int i=0;i<6;i++){var capture=now.AddMinutes(-150+i*30);history.Record(GitHubReading.Read(Fixture(capture,1+i),capture),capture);}
        var current=GitHubReading.Read(Fixture(now,6),now);var estimate=history.Forecast(current,now,12);
        Require(estimate is not null&&Math.Abs(estimate.TargetHours!.Value-3)<.001&&estimate.MonthSpend>6,"local trend and target labels");
        Require(history.Forecast(current with{Scope=new string('c',64)},now,12) is null,"account isolation");
        Require(history.Forecast(current with{Status="Stale"},now,12) is null,"stale suppresses forecast");
        var shortHistory=new GitHubHistory{Samples=history.Samples.Take(5).ToList()};Require(shortHistory.Forecast(current,now,12) is null,"insufficient observations");
        var irregular=new GitHubHistory{Samples=history.Samples.Select(s=>s.At==now.AddMinutes(-30).ToUnixTimeSeconds()?s with{At=now.AddMinutes(-5).ToUnixTimeSeconds()}:s).ToList()};Require(irregular.Forecast(current,now,12) is null,"irregular sampling suppresses forecast");
        var corrected=new GitHubHistory{Samples=history.Samples.Select(s=>s.Metric=="github-spend"&&s.At==now.ToUnixTimeSeconds()?s with{Value=1}:s).ToList()};Require(corrected.Forecast(current,now,12) is null,"billing correction suppresses forecast");
        int count=history.Samples.Count;Require(!history.Record(current,now)&&history.Samples.Count==count,"cached capture never becomes new history");
        history.Samples.Add(new(new string('a',64),"2026-10","github-spend","USD",now.AddDays(-31).ToUnixTimeSeconds(),2));history.Prune(now);Require(history.Samples.Count==count,"30 day retention");
        string folder=Path.Combine(root,"github-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var preferences=new WidgetPreferences{VisibleMetrics=new(){"github-cache","github-spend"},MetricOrder=GitHubCatalog.Ids.Reverse().ToList(),GitHubDetailsOnly=new(){"github-spend"},GlassTheme="clear",GitHubTargets=new(){{"github-spend",10},{"github-cache",1000}},GitHubAlertMetrics=new(){"github-spend"}};preferences.Normalize();string path=Path.Combine(folder,"preferences.json");preferences.Save(path);var loaded=WidgetPreferences.Load(path);
        Require(loaded.GitHubDetailsOnly.SequenceEqual(preferences.GitHubDetailsOnly)&&loaded.VisibleMetrics.SequenceEqual(preferences.VisibleMetrics)&&loaded.MetricOrder.SequenceEqual(preferences.MetricOrder)&&loaded.GlassTheme=="clear"&&loaded.GitHubTargets["github-cache"]==1000,"visibility order targets theme and details persistence");
        var profiles=new ProfileStore{Items=new(){new("GitHub",442,858,false,false,1,loaded.MetricOrder,loaded.VisibleMetrics,new(),loaded.GitHubDetailsOnly,loaded.GlassTheme)}};string profilePath=Path.Combine(folder,"profiles.json");profiles.Save(profilePath);Require(ProfileStore.Load(profilePath).Items.Single().GitHubDetailsOnly!.SequenceEqual(loaded.GitHubDetailsOnly)&&ProfileStore.Load(profilePath).Items.Single().GlassTheme=="clear","profile persistence");
        var defaultPreferences=new WidgetPreferences();Require(defaultPreferences.DisabledProviders.Contains("github")&&!defaultPreferences.VisibleMetrics.Any(GitHubCatalog.Ids.Contains)&&defaultPreferences.RefreshSeconds["github"]==1800,"opt in defaults and schedule");
        var alertPreferences=new WidgetPreferences{VisibleMetrics=new(),GitHubTargets=new(){{"github-spend",8}},GitHubAlertMetrics=new(){"github-spend"}};alertPreferences.DisabledProviders.Remove("github");var sent=new Dictionary<string,long>();
        var notice=GitHubAlerts.Next(GitHubReading.Read(Fixture(now,8),now),alertPreferences,sent,now);Require(notice is not null&&notice.Keys.Length==3&&notice.Message.Contains("Local target"),"hidden metrics alert with local target label");foreach(string key in notice!.Keys)sent[key]=now.ToUnixTimeSeconds();
        Require(GitHubAlerts.Next(GitHubReading.Read(Fixture(now,8),now),alertPreferences,sent,now) is null,"persistent dedup keys");Require(GitHubAlerts.Next(GitHubReading.Read(Fixture(now,8),now) with{Status="Stale"},alertPreferences,new Dictionary<string,long>(),now) is null,"stale values cannot alert");
        Require(GitHubAlerts.Next(GitHubReading.Read(Fixture(now,8),now) with{Scope=new string('d',64)},alertPreferences,sent,now) is not null,"new account isolated alert keys");
        alertPreferences.GitHubAlertPercents.Clear();Require(GitHubAlerts.Next(GitHubReading.Read(Fixture(now,8),now),alertPreferences,new Dictionary<string,long>(),now) is null,"select-none alert thresholds");
        history.Save(Path.Combine(folder,"history.json"));Require(GitHubHistory.Load(Path.Combine(folder,"history.json"),now).Samples.Count==count,"history roundtrip");
        var measured=GitHubCatalog.All.Where(m=>m.Category is not ("forecast" or "allowance")).ToArray();
        var bounded=new GitHubHistory{Samples=Enumerable.Range(0,40000).Select(i=>{var at=now.AddMinutes(-5*(i/measured.Length));var metric=measured[i%measured.Length];return new GitHubSample(new string('a',64),at.ToString("yyyy-MM"),metric.Id,metric.Unit,at.ToUnixTimeSeconds(),1e15);}).ToList()};
        string boundPath=Path.Combine(folder,"bounded-history.json");bounded.Save(boundPath);Require(GitHubHistory.Load(boundPath,now).Samples.Count==40000,"maximum retained history survives restart");
        string futurePath=Path.Combine(folder,"future-history.json");File.WriteAllText(futurePath,"{\"Version\":2,\"Samples\":[]}");var future=GitHubHistory.Load(futurePath,now);Require(!future.Record(current,now),"newer schema is read only");future.Save(futurePath);Require(File.ReadAllText(futurePath)=="{\"Version\":2,\"Samples\":[]}","newer schema bytes preserved");
        return true;
    }
}

public sealed partial class Widget
{
    bool githubChecks;
    readonly List<object> githubLayouts=new();
    void VerifyGitHubViews()
    {
        var saved=preferences.Copy();var originalFont=FontFamily;bool savedConsent=githubConnected;var savedData=providerData.ToDictionary(p=>p.Key,p=>p.Value);string theme=Theme.Active;var now=DateTimeOffset.UtcNow;
        using(var denied=JsonDocument.Parse("{\"provider_id\":\"github\",\"status\":\"Permission\",\"message\":\"permission\"}")){
            providerData["github"]=denied.RootElement.Clone();SyncGitHubTiles();
            if(metricTiles["github-actions-minutes"].DisplayValue!=L.T("No access")||metricTiles["github-actions-storage"].DisplayValue!=L.T("No access"))throw new InvalidOperationException("GitHub billing denial is visible in compact cards");
        }
        providerData["github"]=GitHubChecks.Fixture(now);preferences.DisabledProviders.Remove("github");preferences.ActiveProvidersOnly=true;
        foreach(var (id,card) in cards)if(providerData.TryGetValue(id,out var sample))card.Update(sample);
        preferences.VisibleMetrics=new(){"github-spend","github-cache"};preferences.GitHubDetailsOnly=new(){"github-cache"};ApplyMetricPreferences();
        githubConnected=false;ApplyMetricPreferences();if(metricGrid.Children.Count!=0)throw new InvalidOperationException("Unconfigured GitHub stays hidden even with migrated select-all preferences");githubConnected=true;ApplyMetricPreferences();
        if(metricGrid.Children.Count!=1||!MetricVisible("github-spend")||MetricVisible("github-cache"))throw new InvalidOperationException("GitHub detail-only/active-AI visibility");
        if(TaskbarRows().Count(r=>GitHubCatalog.All.Any(d=>d.Name==r.Label))!=1)throw new InvalidOperationException("Tray/taskbar GitHub choices follow per-metric visibility");
        preferences.VisibleMetrics.Clear();ApplyMetricPreferences();if(metricGrid.Children.Count!=0||expandedHardware.Children.Count!=0)throw new InvalidOperationException("Empty GitHub has no cards or reserved space");
        preferences.GitHubDetailsOnly.Clear();preferences.ActiveProvidersOnly=false;
        foreach(string selectedTheme in Theme.Ids){Theme.Select(selectedTheme);preferences.GlassTheme=selectedTheme;
            foreach(var visible in new[]{new[]{"github-spend","github-actions-minutes","github-actions-storage","github-artifacts"},WidgetPreferences.BaseMetricIds.Concat(GitHubCatalog.Ids).ToArray()}){
                preferences.VisibleMetrics=visible.ToList();ApplyMetricPreferences();
                foreach(string font in new[]{originalFont.Source,"Segoe UI"}){
                FontFamily=new System.Windows.Media.FontFamily(font);
                foreach(var size in new[]{(340d,220d),(442d,858d),(560d,165d),(840d,800d),(1200d,120d),(420d,640d)}){
                    compact=size.Item2<640;Width=size.Item1;Height=MinimumMetricHeight(size.Item1)>size.Item2?MinimumMetricHeight(size.Item1):size.Item2;UpdateLayout();AdaptLayout();SyncGitHubTiles();UpdateLayout();Theme.Apply(this);var check=CheckLayout();
                    bool selection=visible.All(MetricVisible)&&metricGrid.Children.Count==visible.Length;
                    githubLayouts.Add(new{theme=selectedTheme,font,metrics=visible.Length,width=ActualWidth,height=ActualHeight,check.fits,check.controls,check.scale,selection,metricsOnly,desiredHeight=body.DesiredSize.Height,minimumHeight=MinHeight,maximumHeight=MaxHeight});
                    if(font==originalFont.Source&&visible.Length==4&&(size.Item1==442||size.Item1==1200||size.Item1==340))SavePreview($"preview-github-{selectedTheme}-{(int)size.Item1}.png",2);
                }
                }
            }
            FontFamily=originalFont;
            // Include the real WPF header, charts and AI cards in every theme render.
            preferences.VisibleMetrics=WidgetPreferences.BaseMetricIds.Concat(new[]{"github-spend","github-cache"}).ToList();ApplyMetricPreferences();compact=false;Width=442;Height=950;UpdateLayout();AdaptLayout();SyncGitHubTiles();UpdateLayout();Theme.Apply(this);SavePreview("preview-theme-"+selectedTheme+".png",2);
        }
        ShowGitHubMetrics();ShowGitHubConnection();ShowGitHubDetails();ShowTrayCapacity();
        preferences=saved;githubConnected=savedConsent;providerData.Clear();foreach(var row in savedData)providerData[row.Key]=row.Value;Theme.Select(theme);ApplyMetricPreferences();
    }
}
