using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Prism;

public sealed record GitHubMetricDefinition(string Id,string Name,string Unit,string Category,string Description);
public static class GitHubCatalog
{
    public static readonly GitHubMetricDefinition[] All=Load();
    public static readonly string[] Ids=All.Select(m=>m.Id).ToArray();
    static GitHubMetricDefinition[] Load(){using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Prism.GitHubMetrics.json")!;return JsonSerializer.Deserialize<GitHubMetricDefinition[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;}
    public static GitHubMetricDefinition Get(string id)=>All.Single(m=>m.Id==id);
    public static string Compact(double? value,string unit)
    {
        if(value is not double n)return "—";if(unit=="bytes")return Display(n,unit);
        string amount=n>=1e9?$"{n/1e9:0.#}G":n>=1e6?$"{n/1e6:0.#}M":n>=1e3?$"{n/1e3:0.#}k":n.ToString(unit=="USD"?"0.00":"0.#",CultureInfo.CurrentCulture);
        return unit=="USD"?amount+" USD":amount+" "+(unit switch{"minutes"=>"min","core-hours"=>"core·h","GB-hours"=>"GB·h","hours"=>"h",_=>unit});
    }
    public static string Display(double? value,string unit)=>value is not double n?"—":unit switch{
        "USD"=>n.ToString("0.00",CultureInfo.CurrentCulture)+" USD",
        "bytes"=>n>=1_073_741_824?$"{n/1_073_741_824:0.##} GiB":$"{n/1_048_576:0.##} MiB",
        "%"=>$"{n:0.#}%",_=>$"{n:0.##} {unit}"};
}
public sealed record GitHubValue(string Id,string Unit,double? Value,string State,string Source);
public sealed record GitHubRepository(string Scope,double Usd,string Name="");
public sealed record GitHubReading(string Status,string Scope,string Period,DateTimeOffset? At,string Detail,GitHubValue[] Metrics,GitHubRepository[] Repositories)
{
    public bool Fresh(DateTimeOffset now)=>Status=="Live"&&Scope.Length==64&&At is DateTimeOffset at&&at<=now&&now-at<=TimeSpan.FromHours(1)&&Period==now.UtcDateTime.ToString("yyyy-MM");
    public GitHubValue Metric(string id)=>Metrics.FirstOrDefault(m=>m.Id==id)??new(id,GitHubCatalog.Get(id).Unit,null,Status is "Connect" or "Disabled" or "Error" or "Permission"?Status:"Unknown","billing");
    public static GitHubReading Empty(string status="Unknown")=>new(status,"","",null,"Read-only GitHub usage. Configure in Connections.",Array.Empty<GitHubValue>(),Array.Empty<GitHubRepository>());
    static readonly HashSet<string> States=new(){"Live","Stale","Unknown","Unavailable","Error","Permission","Connect","Disabled"};
    public static GitHubReading Read(JsonElement data,DateTimeOffset now)
    {
        try{
            string status=data.GetProperty("status").GetString()??"Unavailable";if(!States.Contains(status))return Empty("Unavailable");
            string detail="Read-only GitHub usage. Billing reports may be delayed.";
            if(data.TryGetProperty("message",out var code))detail=code.GetString() switch{"auth"=>"Sign in with GitHub CLI or replace the saved credential.","consent"=>"Choose a sign-in in Connections.","permission"=>"Billing requires Plan (read), or organization Administration (read). Repository Actions requires Actions (read).","rate"=>"GitHub rate limited collection. Waiting for backoff.","changed"=>"Sign-in changed. Refresh to read the current account.","network"=>"GitHub could not be reached. Cached readings retain their capture time.","format"=>"GitHub returned an unsupported or incomplete response.",_=>detail};
            if(!data.TryGetProperty("at",out var stamp))return Empty(status) with{Detail=detail};
            if(!stamp.TryGetDouble(out var seconds)||!double.IsFinite(seconds)||seconds<=0||seconds>now.ToUnixTimeMilliseconds()/1000d)return Empty("Unavailable");
            var at=DateTimeOffset.FromUnixTimeSeconds((long)seconds);string scope=data.GetProperty("scope").GetString()??"",period=data.GetProperty("period").GetString()??"";
            if(!Regex.IsMatch(scope,"^[a-f0-9]{64}$")||!Regex.IsMatch(period,@"^\d{4}-(0[1-9]|1[0-2])$"))return Empty("Unavailable");
            if(status=="Live"&&(now-at>TimeSpan.FromHours(1)||period!=now.UtcDateTime.ToString("yyyy-MM")))status="Stale";
            var rows=data.GetProperty("metrics");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>GitHubCatalog.All.Length)return Empty("Unavailable");
            var metrics=new List<GitHubValue>();var seen=new HashSet<string>();
            foreach(var row in rows.EnumerateArray()){
                string id=row.GetProperty("id").GetString()??"",unit=row.GetProperty("unit").GetString()??"",state=row.GetProperty("state").GetString()??"";
                if(!GitHubCatalog.Ids.Contains(id)||!seen.Add(id)||GitHubCatalog.Get(id).Unit!=unit||!States.Contains(state))return Empty("Unavailable");
                double? value=null;var number=row.GetProperty("value");
                if(number.ValueKind!=JsonValueKind.Null){if(!number.TryGetDouble(out double n)||!double.IsFinite(n)||n<0||n>1e15||unit=="%"&&n>100)return Empty("Unavailable");if(state is "Live" or "Stale"&&status is "Live" or "Stale")value=n;}
                string source=row.GetProperty("source").GetString()=="repository"?"Selected repository REST APIs":"GitHub billing usage report (USD; may be delayed)";
                metrics.Add(new(id,unit,value,status=="Stale"&&state=="Live"?"Stale":state,source));
            }
            var repos=new List<GitHubRepository>();
            if(data.TryGetProperty("repositories",out var repositoryRows)){
                if(repositoryRows.ValueKind!=JsonValueKind.Array||repositoryRows.GetArrayLength()>1000)return Empty("Unavailable");
                foreach(var row in repositoryRows.EnumerateArray()){
                    string key=row.GetProperty("scope").GetString()??"";double usd=row.GetProperty("usd").GetDouble();
                    string name=row.TryGetProperty("name",out var label)?label.GetString()??"":"";
                    if(!Regex.IsMatch(key,"^[a-f0-9]{64}$")||!double.IsFinite(usd)||usd<0||usd>1e15||name.Length>0&&(!Regex.IsMatch(name,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$")||name.EndsWith("/.")||name.EndsWith("/..")))return Empty("Unavailable");
                    if(Regex.IsMatch(name,@"gh[pousr]_[a-z0-9]{20,}|github_pat_[a-z0-9_]{20,}",RegexOptions.IgnoreCase))name="";repos.Add(new(key,usd,name));
                }
            }
            return new(status,scope,period,at,detail,metrics.ToArray(),repos.ToArray());
        }catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException){return Empty("Unavailable");}
    }
}
public sealed record GitHubSample(string Scope,string Period,string Metric,string Unit,long At,double Value);
public sealed record GitHubEstimate(double MonthSpend,double? TargetHours,string Description);
public sealed record GitHubAlert(string Message,string[] Keys);
public static class GitHubAlerts
{
    public static GitHubAlert? Next(GitHubReading reading,WidgetPreferences preferences,IReadOnlyDictionary<string,long> sent,DateTimeOffset now)
    {
        if(!reading.Fresh(now)||preferences.DisabledProviders.Contains("github"))return null;
        foreach(string id in preferences.GitHubAlertMetrics){
            if(!preferences.GitHubTargets.TryGetValue(id,out double target)||reading.Metric(id) is not {Value:double value,State:"Live"})continue;
            string prefix="github/"+reading.Scope+"/"+reading.Period+"/"+id+"/"+target.ToString("R",CultureInfo.InvariantCulture)+"/";
            foreach(int percent in preferences.GitHubAlertPercents.OrderDescending()){
                if(value<target*percent/100||sent.ContainsKey(prefix+percent))continue;
                return new(GitHubCatalog.Get(id).Name+" reached "+percent+"% of Local target ("+GitHubCatalog.Display(target,GitHubCatalog.Get(id).Unit)+"). This is not a GitHub-enforced limit.",preferences.GitHubAlertPercents.Where(p=>p<=percent).Select(p=>prefix+p).ToArray());
            }
        }
        return null;
    }
}
public sealed class GitHubHistory
{
    public int Version {get;set;}=1;
    public List<GitHubSample> Samples {get;set;}=new();
    public static GitHubHistory Load(string path,DateTimeOffset now){var result=LocalJson.Load<GitHubHistory>(path,12_000_000)??new();result.Prune(now);return result;}
    public void Prune(DateTimeOffset now){Samples=(Samples??new()).Where(s=>s is not null&&Regex.IsMatch(s.Scope??"","^[a-f0-9]{64}$")&&Regex.IsMatch(s.Period??"",@"^\d{4}-(0[1-9]|1[0-2])$")&&GitHubCatalog.Ids.Contains(s.Metric)&&GitHubCatalog.Get(s.Metric).Unit==s.Unit&&double.IsFinite(s.Value)&&s.Value>=0&&s.Value<=1e15&&s.At>now.AddDays(-30).ToUnixTimeSeconds()&&s.At<=now.ToUnixTimeSeconds()).DistinctBy(s=>(s.Scope,s.Period,s.Metric,s.At/300)).OrderBy(s=>s.At).TakeLast(40000).ToList();}
    public bool Record(GitHubReading reading,DateTimeOffset now)
    {
        if(Version!=1||!reading.Fresh(now))return false;bool changed=false;
        foreach(var metric in reading.Metrics.Where(m=>m.Value.HasValue&&m.State=="Live"&&GitHubCatalog.Get(m.Id).Category!="forecast")){
            long at=reading.At!.Value.ToUnixTimeSeconds();int index=Samples.FindLastIndex(s=>s.Scope==reading.Scope&&s.Period==reading.Period&&s.Metric==metric.Id&&s.At/300==at/300);
            var sample=new GitHubSample(reading.Scope,reading.Period,metric.Id,metric.Unit,at,metric.Value!.Value);
            if(index>=0){if(Samples[index].At>=at)continue;Samples[index]=sample;}else Samples.Add(sample);changed=true;
        }
        Prune(now);return changed;
    }
    public GitHubEstimate? Forecast(GitHubReading reading,DateTimeOffset now,double? localTarget)
    {
        if(!reading.Fresh(now)||Version!=1)return null;
        var points=Samples.Where(s=>s.Scope==reading.Scope&&s.Period==reading.Period&&s.Metric=="github-spend"&&s.At>=now.AddHours(-24).ToUnixTimeSeconds()).OrderBy(s=>s.At).ToArray();
        if(points.Length<6||points[^1].At!=reading.At!.Value.ToUnixTimeSeconds()||points[^1].At-points[0].At<1800)return null;
        var gaps=points.Zip(points.Skip(1),(a,b)=>(double)(b.At-a.At)).ToArray();double median=gaps.Order().ElementAt(gaps.Length/2);
        if(median<300||gaps.Any(g=>g>Math.Min(7200,median*1.75)||g<=0)||points.Zip(points.Skip(1),(a,b)=>b.Value<a.Value).Any(v=>v))return null;
        double[] x=points.Select(p=>(p.At-points[0].At)/3600d).ToArray(),y=points.Select(p=>p.Value).ToArray();double mx=x.Average(),my=y.Average();
        double variance=x.Sum(v=>(v-mx)*(v-mx)),slope=x.Zip(y,(a,b)=>(a-mx)*(b-my)).Sum()/variance;
        double total=y.Sum(v=>(v-my)*(v-my));if(slope<=0||total<=0)return null;
        double residual=x.Zip(y,(a,b)=>Math.Pow(b-(my+slope*(a-mx)),2)).Sum();if(1-residual/total<.9)return null;
        var end=new DateTimeOffset(now.Year,now.Month,1,0,0,0,TimeSpan.Zero).AddMonths(1);double forecast=y[^1]+slope*(end-reading.At.Value).TotalHours;
        double? hours=localTarget is double target&&target>y[^1]?(target-y[^1])/slope:null;
        if(!double.IsFinite(forecast)||forecast>1e15||hours is double h&&(!double.IsFinite(h)||h>1e15))return null;
        return new(forecast,hours,"Local estimate · low confidence · linear trend from regular observations; delayed billing can change this estimate.");
    }
    public void Save(string path){if(Version==1)LocalJson.Save(path,this);}
}
public sealed class GitHubConnection
{
    public static string ModeForSave(string selected,int enteredLength)=>enteredLength>0?"credential":selected;
    public bool Enabled {get;set;}
    public string Mode {get;set;}="cli";
    public string Organization {get;set;}="";
    public List<string> Repositories {get;set;}=new();
    public void Validate(){if(Mode is not ("cli" or "credential")||!Regex.IsMatch(Organization,@"^([A-Za-z0-9][A-Za-z0-9-]{0,38})?$"))throw new InvalidOperationException("Invalid GitHub account selection");Repositories=(Repositories??new()).Select(r=>r.Trim()).Where(r=>r.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(Repositories.Count>20||Repositories.Any(r=>!Regex.IsMatch(r,@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$")||r.EndsWith("/.")||r.EndsWith("/..")))throw new InvalidOperationException("Use at most 20 owner/repository names");}
}
