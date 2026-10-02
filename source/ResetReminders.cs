using System.IO;
using System.Text.Json;

namespace Prism;

public sealed record ResetNotice(string Provider,string Window,double Remaining,DateTimeOffset Reset)
{
    public string Key=>JsonSerializer.Serialize(new[]{Provider,Window,Reset.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)});
    public string Message(DateTimeOffset now)=>$"{Provider} · {Window}: resets in {Math.Max(1,Math.Ceiling((Reset-now).TotalMinutes)):0}m · {Remaining:0.#}% left";
}

public sealed class ResetReminders
{
    public bool Enabled {get;set;}=true;
    public int LeadMinutes {get;set;}=30;
    public Dictionary<string,long> Notified {get;set;}=new();
    public static readonly int[] LeadOptions={5,15,30,60,120};
    public static ResetReminders Load(string path)
    {
        try{
            var result=JsonSerializer.Deserialize<ResetReminders>(File.ReadAllText(path))??new();
            if(!LeadOptions.Contains(result.LeadMinutes))result.LeadMinutes=30;
            result.Notified??=new();result.Prune(DateTimeOffset.UtcNow);return result;
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    public static List<ResetNotice> Due(IEnumerable<KeyValuePair<string,QuotaSnapshot>> readings,DateTimeOffset now,int minutes)
    {
        var result=new List<ResetNotice>();
        foreach(var (provider,reading) in readings){
            if(reading.Captured is not DateTimeOffset at||at>now||now-at>TimeSpan.FromMinutes(10))continue;
            if(reading.Status is not ("Live" or "Recent" or "CLI" or "Browser" or "Feed"))continue;
            foreach(var window in reading.Windows){
                if(!double.IsFinite(window.Remaining)||window.Remaining>100||window.Remaining<=10||window.Reset is not DateTimeOffset reset||reset<=now||reset-now>TimeSpan.FromMinutes(minutes))continue;
                result.Add(new(provider,window.Label,window.Remaining,reset));
            }
        }
        return result.OrderBy(n=>n.Reset).ThenBy(n=>n.Provider).ToList();
    }
    public List<ResetNotice> Unseen(IEnumerable<ResetNotice> notices)=>Enabled?notices.Where(n=>!Notified.ContainsKey(n.Key)).DistinctBy(n=>n.Key).ToList():new();
    public void Mark(IEnumerable<ResetNotice> notices,DateTimeOffset now){foreach(var notice in notices)Notified[notice.Key]=notice.Reset.ToUnixTimeSeconds();Prune(now);}
    void Prune(DateTimeOffset now)=>Notified=Notified.Where(p=>p.Key.Length<=512&&p.Value>now.ToUnixTimeSeconds()&&p.Value<now.AddYears(1).ToUnixTimeSeconds()).Take(512).ToDictionary(p=>p.Key,p=>p.Value);
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static bool Verify(string testPath)
    {
        var now=new DateTimeOffset(2026,10,2,12,0,0,TimeSpan.Zero);
        List<ResetNotice> Check(double remaining,double resetMinutes,string status="Live",int ageMinutes=0,bool knownReset=true){
            var snapshot=new QuotaSnapshot(status,"test","",now.AddMinutes(-ageMinutes),new[]{new QuotaWindow("5-hour",remaining,knownReset?now.AddMinutes(resetMinutes):null)});
            return Due(new Dictionary<string,QuotaSnapshot>{{"Codex",snapshot}},now,30);
        }
        bool bounds=Check(double.NaN,5).Count==0&&Check(101,5).Count==0&&Check(10,5).Count==0&&Check(10.1,5).Count==1&&Check(50,30).Count==1&&Check(50,30.1).Count==0&&Check(50,0).Count==0;
        bool freshness=Check(50,5,"Stale").Count==0&&Check(50,5,ageMinutes:11).Count==0&&Check(50,5,ageMinutes:-1).Count==0&&Check(50,5,knownReset:false).Count==0;
        var notice=new ResetNotice("Codex","5-hour",50,DateTimeOffset.UtcNow.AddMinutes(20));var state=new ResetReminders();
        bool once=state.Unseen(new[]{notice}).Count==1;state.Mark(new[]{notice},DateTimeOffset.UtcNow);state.Save(testPath);
        var restored=Load(testPath);once&=restored.Unseen(new[]{notice}).Count==0&&restored.Unseen(new[]{notice with{Reset=notice.Reset.AddHours(5)}}).Count==1;
        restored.Enabled=false;bool disabled=restored.Unseen(new[]{notice with{Provider="Cursor"}}).Count==0;
        File.WriteAllText(testPath,"{invalid");bool recovery=Load(testPath).LeadMinutes==30;
        return bounds&&freshness&&once&&disabled&&recovery;
    }
}
