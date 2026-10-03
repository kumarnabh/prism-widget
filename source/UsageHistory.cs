namespace Prism;

public sealed record UsagePoint(string Provider,long At,double Remaining);
public sealed record WindowSample(string Provider,string Scope,string Window,long At,double Remaining,long? Reset,bool Rolling=false);
public sealed class UsageHistory
{
    public int Version {get;set;}=2;
    public List<UsagePoint> Points {get;set;}=new();
    public List<WindowSample> Samples {get;set;}=new();
    public bool ReadOnly {get;private set;}
    static bool Valid(UsagePoint p,DateTimeOffset now)=>p is not null&&WidgetPreferences.ProviderIds.Contains(p.Provider)&&double.IsFinite(p.Remaining)&&p.Remaining>=0&&p.Remaining<=100&&p.At>=now.AddDays(-30).ToUnixTimeSeconds()&&p.At<=now.ToUnixTimeSeconds();
    public static UsageHistory Load(string path,DateTimeOffset now)
    {
        var history=LocalJson.Load<UsageHistory>(path,32_000_000)??new();
        if(history.Version>2){history.ReadOnly=true;history.Points=new();history.Samples=new();return history;}
        history.Samples=(history.Samples??new()).Where(p=>SampleValid(p,now)).OrderBy(p=>p.At).TakeLast(40_000).ToList();
        history.Points=(history.Points??new()).Where(p=>Valid(p,now)).OrderBy(p=>p.At).TakeLast(40_000).ToList();return history;
    }
    public bool Record(string provider,QuotaSnapshot reading,DateTimeOffset now)
    {
        if(ReadOnly||!Fresh(reading,now)||reading.Remaining is not double remaining||reading.Captured is not DateTimeOffset at)return false;
        Samples=(Samples??new()).Where(p=>SampleValid(p,now)).TakeLast(40_000).ToList();bool changed=false;
        if(reading.Scope.Length==64&&reading.Scope.All(Uri.IsHexDigit))foreach(var window in reading.Windows){
            var sample=new WindowSample(provider,reading.Scope,string.IsNullOrEmpty(window.Id)?window.Label:window.Id,at.ToUnixTimeSeconds(),window.Remaining,window.Reset?.ToUnixTimeSeconds(),window.Rolling);
            if(!SampleValid(sample,now))continue;
            int prior=Samples.FindLastIndex(p=>p.Provider==provider&&p.Scope==sample.Scope&&p.Window==sample.Window&&p.Reset==sample.Reset&&p.At/300==sample.At/300);
            if(prior<0){Samples.Add(sample);changed=true;}else if(Samples[prior].At<sample.At){Samples[prior]=sample;changed=true;}
        }
        Samples=Samples.TakeLast(40_000).ToList();
        var point=new UsagePoint(provider,at.ToUnixTimeSeconds(),remaining);if(!Valid(point,now))return changed;
        int index=Points.FindLastIndex(p=>p.Provider==provider&&p.At/300==point.At/300);
        if(index>=0){if(Points[index].At>=point.At)return changed;Points[index]=point;}else Points.Add(point);
        Points=Points.Where(p=>Valid(p,now)).OrderBy(p=>p.At).TakeLast(40_000).ToList();return true;
    }
    public static bool Fresh(QuotaSnapshot reading,DateTimeOffset now)=>reading.Status is "Live" or "Recent" or "CLI" or "Browser" or "Feed"
        &&reading.Captured is DateTimeOffset at&&at<=now&&now-at<=TimeSpan.FromMinutes(10);
    public static bool SampleValid(WindowSample p,DateTimeOffset now)=>p is not null&&WidgetPreferences.ProviderIds.Contains(p.Provider)&&p.Scope is {Length:64}&&p.Scope.All(Uri.IsHexDigit)&&p.Window is {Length:>0 and <=160}&&!p.Window.Any(char.IsControl)&&double.IsFinite(p.Remaining)&&p.Remaining>=0&&p.Remaining<=100&&p.At>now.AddDays(-30).ToUnixTimeSeconds()&&p.At<=now.ToUnixTimeSeconds()&&(p.Reset is null||p.Reset>p.At&&p.Reset<253402300799);
    public void Save(string path){if(ReadOnly)return;LocalJson.Save(path,this);}
}

public sealed class LowQuotaAlerts
{
    public Dictionary<string,long> Notified {get;set;}=new();
    public static LowQuotaAlerts Load(string path)=>LocalJson.Load<LowQuotaAlerts>(path,200_000)??new();
    public List<string> Due(IEnumerable<KeyValuePair<string,QuotaSnapshot>> readings,DateTimeOffset now,int threshold)
    {
        Notified??=new();Notified=Notified.Where(p=>p.Key.Length<512&&p.Value>=now.AddDays(-30).ToUnixTimeSeconds()).Take(256).ToDictionary(p=>p.Key,p=>p.Value);
        var result=new List<string>();
        foreach(var (provider,reading) in readings){
            if(!UsageHistory.Fresh(reading,now))continue;
            foreach(var window in reading.Windows){
                string legacy=System.Text.Json.JsonSerializer.Serialize(new[]{provider,window.Label,window.Reset?.ToUnixTimeSeconds().ToString()??"unknown"});
                string key=reading.Scope.Length==64?System.Text.Json.JsonSerializer.Serialize(new[]{provider,reading.Scope,string.IsNullOrEmpty(window.Id)?window.Label:window.Id,window.Reset?.ToUnixTimeSeconds().ToString()??"unknown"}):legacy;
                if(window.Remaining>threshold){Notified.Remove(key);Notified.Remove(legacy);continue;}
                if(!double.IsFinite(window.Remaining)||window.Remaining<0||window.Reset<=now||Notified.ContainsKey(key)||Notified.ContainsKey(legacy))continue;
                result.Add($"{provider} · {window.Label}: {window.Remaining:0.#}%");Notified[key]=now.ToUnixTimeSeconds();
            }
        }
        return result;
    }
    public void Save(string path)=>LocalJson.Save(path,this);
}
