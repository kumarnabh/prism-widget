using System.IO;
using System.Text.Json;

namespace Prism;

public sealed class WidgetPreferences
{
    public static readonly string[] BaseMetricIds={"cpu","ram","disk","net","codex","cursor","opencode","claude"};
    public static readonly string[] ExtraMetricIds={"gpu","vram","frequency","battery","read","write"};
    public static readonly string[] MetricIds=BaseMetricIds.Concat(ExtraMetricIds).ToArray();
    public static readonly string[] ProviderIds={"codex","cursor","opencode","claude"};
    public bool TrayEnabled {get;set;}=true;
    public bool SnapEdges {get;set;}=true;
    public bool HistoryEnabled {get;set;}=true;
    public bool LowQuotaEnabled {get;set;}=true;
    public bool ForecastEnabled {get;set;}=true;
    public bool CapacityAlerts {get;set;}=false;
    public bool ActiveProvidersOnly {get;set;}=false;
    public bool MinimizeToTray {get;set;}=true;
    public string TaskbarMetric {get;set;}="ram";
    public int LowQuotaThreshold {get;set;}=20;
    public bool Use24Hour {get;set;}=true;
    public string Language {get;set;}="en";
    public double Opacity {get;set;}=1;
    public List<string> MetricOrder {get;set;}=MetricIds.ToList();
    public List<string> VisibleMetrics {get;set;}=BaseMetricIds.ToList();
    public Dictionary<string,int> RefreshSeconds {get;set;}=new(){{"codex",60},{"cursor",300},{"opencode",300},{"claude",300}};
    public void Normalize()
    {
        if(!new[]{"en","hi","es","fr"}.Contains(Language))Language="en";
        if(!double.IsFinite(Opacity))Opacity=1;Opacity=Math.Clamp(Opacity,.65,1);
        LowQuotaThreshold=Math.Clamp(LowQuotaThreshold,1,50);
        if(!new[]{"none","cpu","ram","gpu","battery"}.Concat(ProviderIds).Contains(TaskbarMetric))TaskbarMetric="none";
        MetricOrder=(MetricOrder??new()).Where(MetricIds.Contains).Distinct().Concat(MetricIds).Distinct().ToList();
        VisibleMetrics=(VisibleMetrics??new()).Where(MetricIds.Contains).Distinct().ToList();
        RefreshSeconds??=new();
        RefreshSeconds=ProviderIds.ToDictionary(id=>id,id=>Math.Clamp(RefreshSeconds.GetValueOrDefault(id,id=="codex"?60:300),60,3600));
    }
    public WidgetPreferences Copy()=>JsonSerializer.Deserialize<WidgetPreferences>(JsonSerializer.Serialize(this))!;
    public static WidgetPreferences Load(string path){var p=LocalJson.Load<WidgetPreferences>(path)??new();p.Normalize();return p;}
    public void Save(string path){Normalize();LocalJson.Save(path,this);}
}

public static class LocalJson
{
    public static T? Load<T>(string path,int maxBytes=8_000_000)
    {
        try{if(!File.Exists(path)||new FileInfo(path).Length>maxBytes)return default;return JsonSerializer.Deserialize<T>(File.ReadAllText(path));}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){return default;}
    }
    public static void Save<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(value));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public sealed class ProviderSchedule
{
    readonly Dictionary<string,DateTimeOffset> next=new();
    public string[] Due(DateTimeOffset now,bool force=false)=>WidgetPreferences.ProviderIds.Where(id=>force||!next.TryGetValue(id,out var at)||at<=now).ToArray();
    public void Mark(IEnumerable<string> ids,DateTimeOffset now,WidgetPreferences settings){foreach(var id in ids)next[id]=now.AddSeconds(settings.RefreshSeconds[id]);}
    public int Remaining(string id,DateTimeOffset now)=>next.TryGetValue(id,out var at)?Math.Max(0,(int)Math.Ceiling((at-now).TotalSeconds)):0;
    public void Reset()=>next.Clear();
}

public sealed record LayoutProfile(string Name,double Width,double Height,bool Compact,bool Pinned,double Opacity,List<string> Order,List<string> Visible,ClockPreferences Clocks);
public sealed class ProfileStore
{
    public List<LayoutProfile> Items {get;set;}=new();
    public static ProfileStore Load(string path)
    {
        var result=LocalJson.Load<ProfileStore>(path,200_000)??new();
        result.Items=(result.Items??new()).Where(p=>p is not null&&!string.IsNullOrWhiteSpace(p.Name)&&p.Name.Length<=32&&!p.Name.Any(char.IsControl)&&double.IsFinite(p.Width)&&double.IsFinite(p.Height)&&double.IsFinite(p.Opacity)&&p.Order is not null&&p.Visible is not null&&p.Clocks is not null&&WorldClocks.Valid(p.Clocks.FirstZone)&&WorldClocks.Valid(p.Clocks.SecondZone)).DistinctBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).Take(20).Select(p=>p with{Clocks=p.Clocks with{FirstLabel=WorldClocks.Label(p.Clocks.FirstLabel,p.Clocks.FirstZone),SecondLabel=WorldClocks.Label(p.Clocks.SecondLabel,p.Clocks.SecondZone)}}).ToList();
        return result;
    }
    public void Save(string path)=>LocalJson.Save(path,this);
}
