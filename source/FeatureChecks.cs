using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Prism;

// Runs without accounts, registry writes, Explorer or network access.
public static class FeatureChecks
{
    sealed class Responses(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(reply(request));
    }
    sealed class StalledStream:Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){await Task.Delay(Timeout.Infinite,token);return 0;}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    static void Require(bool value,string name){if(!value)throw new InvalidOperationException("Feature check failed: "+name);}
    public static async Task<bool> Verify(string directory)
    {
        string root=Path.Combine(directory,"features-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var now=DateTimeOffset.UtcNow;
        var prefs=new WidgetPreferences{MetricOrder=new(){"claude","bad","claude"},VisibleMetrics=new(),Language="invalid",Opacity=9,RefreshSeconds=new(){{"codex",1},{"cursor",99999}}};
        prefs.Normalize();Require(prefs.MetricOrder.Count==WidgetPreferences.MetricIds.Length&&prefs.MetricOrder[0]=="claude"&&prefs.VisibleMetrics.Count==0&&prefs.Language=="en"&&prefs.Opacity==1,"settings sanitization");
        string settings=Path.Combine(root,"preferences.json");prefs.Language="hi";prefs.Save(settings);Require(WidgetPreferences.Load(settings).Language=="hi","settings roundtrip");File.WriteAllText(settings,"{bad");Require(WidgetPreferences.Load(settings).MetricOrder.Count==WidgetPreferences.MetricIds.Length,"corrupt settings recovery");
        var scheduler=new ProviderSchedule();prefs=new();scheduler.Mark(scheduler.Due(now),now,prefs);Require(scheduler.Due(now.AddSeconds(60)).SequenceEqual(new[]{"codex"}),"independent schedules");Require(scheduler.Due(now.AddSeconds(300)).Length==4&&scheduler.Due(now,true).Length==4&&scheduler.Remaining("cursor",now)==300,"schedule countdown/force");
        QuotaSnapshot Reading(double remaining,int age=0,string status="Live",int reset=3600){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new{status,at=now.AddSeconds(-age).ToUnixTimeSeconds(),windows=new[]{new{label="Session",remaining,reset=now.AddSeconds(reset).ToUnixTimeSeconds()}}}));return QuotaSnapshot.Read(doc.RootElement,now);}
        var history=new UsageHistory();Require(history.Record("codex",Reading(42),now)&&!history.Record("codex",Reading(42),now),"history deduplication");Require(!history.Record("codex",Reading(20,601),now)&&!history.Record("codex",Reading(20,0,"Stale"),now),"history freshness");
        history.Points.Add(new("codex",now.AddDays(-31).ToUnixTimeSeconds(),50));string historyFile=Path.Combine(root,"history.json");history.Save(historyFile);Require(UsageHistory.Load(historyFile,now).Points.Count==1,"history retention");
        var low=new LowQuotaAlerts();KeyValuePair<string,QuotaSnapshot>[] Readings(double remaining)=>new[]{new KeyValuePair<string,QuotaSnapshot>("Codex",Reading(remaining))};
        Require(low.Due(Readings(10),now,20).Count==1&&low.Due(Readings(10),now,20).Count==0,"low alert deduplication");low.Due(Readings(50),now,20);Require(low.Due(Readings(10),now,20).Count==1,"low alert rearm");Require(new LowQuotaAlerts().Due(new[]{new KeyValuePair<string,QuotaSnapshot>("Codex",Reading(10,601))},now,20).Count==0,"stale alert suppression");
        var profiles=new ProfileStore{Items=new(){new("Work",442,858,false,true,1,WidgetPreferences.MetricIds.ToList(),new(){"cpu"},new(true,FirstLabel:null!,SecondLabel:null!))}};string profileFile=Path.Combine(root,"profiles.json");profiles.Save(profileFile);var restored=ProfileStore.Load(profileFile).Items.Single();Require(restored.Visible.SequenceEqual(new[]{"cpu"})&&!string.IsNullOrEmpty(restored.Clocks.FirstLabel)&&!string.IsNullOrEmpty(restored.Clocks.SecondLabel),"profile roundtrip and malformed labels");
        string lowFile=Path.Combine(root,"low.json");low.Due(Readings(50),now,20);low.Save(lowFile);Require(LowQuotaAlerts.Load(lowFile).Due(Readings(10),now,20).Count==1,"persisted alert recovery");File.Delete(lowFile);
        var work=new Rect(-1920,0,1920,1040);var snapped=DesktopLayout.Constrain(new Rect(-1910,8,400,800),work,true);Require(snapped.Left==-1920&&snapped.Top==0,"negative monitor snapping");Require(DesktopLayout.Constrain(new Rect(9000,9000,500,1400),work,false)==new Rect(-500,0,500,1040),"disconnected monitor recovery");
        foreach(string language in new[]{"en","hi","es","fr"}){L.Set(language);Require(L.Known("Settings")&&!string.IsNullOrWhiteSpace(L.T("Settings"))&&L.Time(now,false).Length>0,"localization "+language);}L.Set("en");
        byte[] payload=System.Text.Encoding.UTF8.GetBytes("verified test payload");string hash=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();string file="Prism-9.0.0-win-x64.zip",url="https://github.com/kumarnabh/prism-widget/releases/download/v9.0.0/"+file;
        var release=new ReleaseUpdate(new(9,0,0),file,new(url),hash,payload.Length,new("https://github.com/kumarnabh/prism-widget/releases/tag/v9.0.0"));
        using(var client=new ReleaseUpdates(new Responses(request=>new(HttpStatusCode.OK){Content=request.RequestUri!.Host=="api.github.com"?new StringContent(JsonSerializer.Serialize(new{tag_name="v9.0.0",draft=false,prerelease=false,assets=new[]{new{name=file,browser_download_url=url,size=payload.Length,digest="sha256:"+hash}}})):new ByteArrayContent(payload)}))){var found=await client.Check(default);Require(found.Sha256==hash,"release metadata");string target=await client.Download(found,root,default);Require(File.ReadAllBytes(target).SequenceEqual(payload),"verified download");File.Delete(target);}
        using(var client=new ReleaseUpdates(new Responses(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(payload)}))){bool rejected=false;try{await client.Download(release with{Sha256=new string('0',64)},root,default);}catch(IOException){rejected=true;}Require(rejected&&!File.Exists(Path.Combine(root,file))&&!Directory.EnumerateFiles(root,"*.tmp").Any(),"bad digest rejected and temporary file removed");}
        int requests=0;using(var client=new ReleaseUpdates(new Responses(_=>{requests++;var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new("https://example.invalid/payload");return response;}))){bool rejected=false;try{await client.Download(release,root,default);}catch(HttpRequestException){rejected=true;}Require(rejected&&requests==1,"redirect boundary");}
        using(var client=new ReleaseUpdates(new Responses(_=>new(HttpStatusCode.OK){Content=new StreamContent(new StalledStream())}),TimeSpan.FromMilliseconds(100))){bool timedOut=false;try{await client.Download(release,root,default);}catch(OperationCanceledException){timedOut=true;}Require(timedOut&&!Directory.EnumerateFiles(root,"*.tmp").Any(),"body timeout and partial cleanup");}
        foreach(string path in new[]{settings,historyFile,profileFile})File.Delete(path);Directory.Delete(root);
        return true;
    }
}
