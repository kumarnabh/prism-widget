using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Prism;

public sealed partial class Widget
{
    GitHubHistory githubHistory=new();
    bool githubConnected;
    string GitHubHistoryPath=>Path.Combine(Data,"github-history.json");
    string GitHubConnectionPath=>Path.Combine(Data,"github-connection.json");
    GitHubReading GitHubCurrent=>providerData.TryGetValue("github",out var data)?GitHubReading.Read(data,DateTimeOffset.UtcNow):GitHubReading.Empty(preferences.DisabledProviders.Contains("github")?"Disabled":"Unknown");
    void BuildGitHubTiles()
    {
        foreach(var definition in GitHubCatalog.All){
            var tile=new MetricTile(definition.Name,"⑂","#A9D2FF");metricTiles[definition.Id]=tile;
            var expanded=new MetricTile(definition.Name,"⑂","#A9D2FF");expandedHardwareTiles[definition.Id]=expanded;
            foreach(var item in new[]{tile,expanded}){item.Element.Cursor=Cursors.Hand;item.Element.MouseLeftButtonUp+=(_,_)=>ShowGitHubDetails();item.Element.KeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){ShowGitHubDetails();e.Handled=true;}};}
        }
    }
    GitHubValue GitHubMetric(string id,GitHubReading reading)
    {
        var metric=reading.Metric(id);if(id is not ("github-forecast" or "github-exhaustion"))return metric;
        if(!preferences.ForecastEnabled)return metric with{Value=null,State="Unknown",Source="Local estimates are disabled in Settings."};
        double? target=preferences.GitHubTargets.TryGetValue("github-spend",out double configured)?configured:null;
        var estimate=githubHistory.Forecast(reading,DateTimeOffset.UtcNow,target);
        double? value=id=="github-forecast"?estimate?.MonthSpend:estimate?.TargetHours;
        return metric with{Value=value,State=value.HasValue?"Live":"Unknown",Source=value.HasValue?estimate!.Description:id=="github-exhaustion"&&target is null?"Local estimate unavailable: configure a Local target for Month billable spend.":"Local estimate unavailable: insufficient, stale, irregular, flat or corrected observations, or target already reached."};
    }
    string GitHubTooltip(GitHubValue metric,GitHubReading reading)=>GitHubCatalog.Get(metric.Id).Name+" · "+GitHubCatalog.Display(metric.Value,metric.Unit)+" · "+metric.State+"\n"+GitHubCatalog.Get(metric.Id).Description+"\n"+metric.Source+"\nUTC billing month: "+(reading.Period.Length>0?reading.Period:"Unknown")+"\nLast successful fetch: "+(reading.At?.LocalDateTime.ToString("g")??"Unknown")+"\n"+reading.Detail;
    void SyncGitHubTiles()
    {
        var reading=GitHubCurrent;
        foreach(var definition in GitHubCatalog.All){
            if(!metricTiles.TryGetValue(definition.Id,out var tile))continue;var metric=GitHubMetric(definition.Id,reading);
            string health=metric.State=="Live"?"ready":metric.State=="Stale"?"stale":metric.State is "Error" or "Permission"?"error":"unknown";
            tile.Set(GitHubCatalog.Compact(metric.Value,metric.Unit),GitHubTooltip(metric,reading),null,health:health);
            expandedHardwareTiles[definition.Id].Set(GitHubCatalog.Compact(metric.Value,metric.Unit),GitHubTooltip(metric,reading),null,health:health);
        }
    }
    void RecordGitHub(JsonElement data,DateTimeOffset now)
    {
        var reading=GitHubReading.Read(data,now);
        if(reading.Fresh(now))lastSuccess["github"]=reading.At!.Value;
        if(!preview&&!selftest&&preferences.HistoryEnabled&&githubHistory.Record(reading,now))try{githubHistory.Save(GitHubHistoryPath);}catch(IOException){}catch(UnauthorizedAccessException){}
        CheckGitHubAlerts();
    }
    void CheckGitHubAlerts()
    {
        if(preview||selftest||closed||!githubConnected||preferences.DisabledProviders.Contains("github")||reminderPopup.IsOpen)return;
        var reading=GitHubCurrent;var now=DateTimeOffset.UtcNow;if(!reading.Fresh(now))return;
        notifications.Forecasts??=new();
        var notice=GitHubAlerts.Next(reading,preferences,notifications.Forecasts,now);
        if(notice is not null&&DeliverNotice("GitHub local target",new[]{notice.Message})){foreach(string key in notice.Keys)notifications.Forecasts[key]=now.ToUnixTimeSeconds();SaveNotifications();}
    }
    void AddGitHubConnectionCard(StackPanel panel)
    {
        var reading=GitHubCurrent;var content=new StackPanel();content.Children.Add(Text("GitHub",18));
        content.Children.Add(Text(reading.Status+" · optional read-only billing and selected repositories",12));content.Children.Add(Text(reading.Detail,11,Muted));
        content.Children.Add(Text("Every "+preferences.RefreshSeconds["github"]+" seconds · last successful fetch: "+(reading.At?.LocalDateTime.ToString("g")??"Unknown"),11,Muted));
        var actions=new WrapPanel();actions.Children.Add(Button("Connect / disconnect…","GitHub sign-in and collection consent",ShowGitHubConnection));actions.Children.Add(Button("Metrics…","Choose GitHub metrics",ShowGitHubMetrics));actions.Children.Add(Button("Details…","GitHub billing and repository details",()=>ShowGitHubDetails()));
        content.Children.Add(actions);var card=Panel(content);card.Margin=new Thickness(0,12,0,0);panel.Children.Add(card);
    }
    void ResetGitHubAccount()
    {
        CancelProvider("github");providerData.Remove("github");lastSuccess.Remove("github");
        string cache=Path.Combine(Data,"github-cache.json");if(File.Exists(cache))File.Delete(cache);
        schedule.Reset();SyncGitHubTiles();ApplyMetricPreferences();AdaptLayout();
    }
    void ShowGitHubConnection()
    {
        var dialog=FeatureWindow("GitHub connection",640,720);var panel=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var config=preview||selftest?new GitHubConnection():LocalJson.Load<GitHubConnection>(GitHubConnectionPath,20000)??new();
        panel.Children.Add(Text("GitHub",24));panel.Children.Add(Text("Allow read-only requests to api.github.com. Reuse the active github.com sign-in from GitHub CLI, or optionally save a fine-grained token in Windows Credential Manager. Prism never changes GitHub resources or billing.",13));
        var consent=Check(panel,"Enable GitHub collection",config.Enabled&&!preferences.DisabledProviders.Contains("github"));
        var mode=Choose(panel,"Sign-in source",new[]{"GitHub CLI (recommended)","Windows Credential Manager"},config.Mode=="credential"?"Windows Credential Manager":"GitHub CLI (recommended)");
        panel.Children.Add(Text("CLI setup: gh auth login --hostname github.com. Prism reuses existing permissions and never requests broader scopes. Environment-token overrides are ignored. Use gh auth switch --hostname github.com to select an account, then Refresh.",12,Muted));
        panel.Children.Add(Text("Personal billing: fine-grained Plan (read). Organization billing: Administration (read), selected organization and appropriate owner/billing access. Selected repositories: Metadata (read) and Actions (read). A normal CLI token may lack billing access; repository statistics can still work. Grant only what you need.",12,Muted));
        panel.Children.Add(Text("Optional fine-grained token (blank keeps the saved credential)",12,Muted));var secret=new PasswordBox{MaxLength=4096,Margin=new Thickness(0,6,0,14),Padding=new Thickness(8),Foreground=Theme.Solid(Ink),Background=Theme.Solid(Color.FromRgb(20,31,53)),BorderBrush=Theme.Solid(Color.FromRgb(51,71,107))};System.Windows.Automation.AutomationProperties.SetName(secret,"Optional fine-grained GitHub token");panel.Children.Add(secret);
        var org=Input(panel,"Organization (blank = personal billing)",config.Organization);org.MaxLength=39;
        var repos=Input(panel,"Selected repositories (owner/repo, one per line; maximum 20)",string.Join("\n",config.Repositories));repos.AcceptsReturn=true;repos.Height=90;repos.MaxLength=3000;
        panel.Children.Add(Text("Repository statistics are separate from billing. Billing reports cover metered usage and can lag. There is no guessed allowance, plan quota or spending limit. Showing a metric does not control collection or alerts.",12,Muted));
        var status=Text("",12,Muted);panel.Children.Add(status);
        panel.Children.Add(Primary(Button("Save connection","Save explicit GitHub collection consent",async()=>{
            try{
                var nextConfig=new GitHubConnection{Enabled=consent.IsChecked==true,Mode=mode.SelectedIndex==1?"credential":"cli",Organization=org.Text.Trim(),Repositories=repos.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).ToList()};nextConfig.Validate();
                ResetGitHubAccount();
                using(var credential=secret.SecurePassword)if(nextConfig.Mode=="credential"&&credential.Length>0)GitHubCredential.Save(Root,credential);
                secret.Clear();var next=preferences.Copy();if(nextConfig.Enabled)next.DisabledProviders.Remove("github");else if(!next.DisabledProviders.Contains("github"))next.DisabledProviders.Add("github");
                LocalJson.Save(GitHubConnectionPath,nextConfig);githubConnected=nextConfig.Enabled;next.Save(SettingsPath);preferences=next;ResetGitHubAccount();dialog.Close();if(nextConfig.Enabled)await UpdateProvider("github");
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){secret.Clear();status.Text="Could not save connection. Check the account/repository format and folder access.";}
        })));
        panel.Children.Add(Button("Disconnect and remove saved token","Revoke Prism consent and remove this installation's saved token",()=>{
            try{CancelProvider("github");LocalJson.Save(GitHubConnectionPath,new GitHubConnection());githubConnected=false;ResetGitHubAccount();GitHubCredential.Remove(Root);var next=preferences.Copy();if(!next.DisabledProviders.Contains("github"))next.DisabledProviders.Add("github");next.Save(SettingsPath);preferences=next;secret.Clear();ResetGitHubAccount();dialog.Close();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text="Could not fully disconnect. Check folder access and Windows Credential Manager.";}
        }));
        dialog.Closed+=(_,_)=>secret.Clear();CaptureFeature(dialog,"github-connection");dialog.ShowDialog();
    }
    void ShowGitHubMetrics()
    {
        var dialog=FeatureWindow("Settings · Metrics · GitHub",740,760);var shell=new DockPanel{Margin=new Thickness(20)};dialog.Content=shell;
        var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);shell.Children.Add(buttons);
        var tabs=new TabControl();shell.Children.Add(tabs);var panel=new StackPanel{Margin=new Thickness(12)};tabs.Items.Add(new TabItem{Header="Metrics",Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        panel.Children.Add(Text("GitHub metrics",23));panel.Children.Add(Text("Choose each metric independently. Details only keeps it out of the dashboard, ribbon and tray. Collection and alerts continue when hidden. Changes are included in layout profiles.",12,Muted));
        var selected=preferences.VisibleMetrics.Where(GitHubCatalog.Ids.Contains).ToHashSet();var details=preferences.GitHubDetailsOnly.ToHashSet();var order=preferences.MetricOrder.Where(GitHubCatalog.Ids.Contains).ToList();
        var list=new ListBox{Height=390,Margin=new Thickness(0,12,0,12)};panel.Children.Add(list);
        void Render(string? focus=null){list.Items.Clear();var reading=GitHubCurrent;foreach(string id in order){var definition=GitHubCatalog.Get(id);var stack=new StackPanel();var checks=new WrapPanel();var visible=new CheckBox{Content=definition.Name+" · "+definition.Unit,IsChecked=selected.Contains(id),Margin=new Thickness(0,4,14,4)};visible.Checked+=(_,_)=>selected.Add(id);visible.Unchecked+=(_,_)=>selected.Remove(id);checks.Children.Add(visible);var only=new CheckBox{Content="Details only",IsChecked=details.Contains(id),Margin=new Thickness(0,4,0,4)};only.Checked+=(_,_)=>details.Add(id);only.Unchecked+=(_,_)=>details.Remove(id);checks.Children.Add(only);stack.Children.Add(checks);stack.Children.Add(Text(definition.Description+" · "+GitHubMetric(id,reading).State,11,Muted));var item=new ListBoxItem{Content=stack,Tag=id,Padding=new Thickness(6),HorizontalContentAlignment=HorizontalAlignment.Stretch};list.Items.Add(item);if(id==focus)list.SelectedItem=item;}}
        Render();var moves=new WrapPanel();panel.Children.Add(moves);
        void Move(int delta){if(list.SelectedItem is not ListBoxItem item)return;string id=(string)item.Tag;int index=order.IndexOf(id),target=index+delta;if(target<0||target>=order.Count)return;order.RemoveAt(index);order.Insert(target,id);Render(id);}
        moves.Children.Add(Button("Move up","Move selected metric up",()=>Move(-1)));moves.Children.Add(Button("Move down","Move selected metric down",()=>Move(1)));moves.Children.Add(Button("Select all","Select all GitHub metrics",()=>{selected.UnionWith(order);Render();}));moves.Children.Add(Button("Select none","Hide every GitHub metric",()=>{selected.Clear();Render();}));
        var targetsPanel=new StackPanel{Margin=new Thickness(12)};tabs.Items.Add(new TabItem{Header="Local targets & alerts",Content=new ScrollViewer{Content=targetsPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        targetsPanel.Children.Add(Text("Local targets & thresholds",23));targetsPanel.Children.Add(Text("Targets are local preferences, not GitHub-enforced limits or included allowances. Opt-in alerts fire once at 75%, 90% and 100% of each Local target per account/month. Hidden metrics can alert. Forecasts do not trigger these measured-usage alerts. No included-allowance alerts are available without an authoritative denominator.",12,Muted));
        var percentChecks=new Dictionary<int,CheckBox>();foreach(int percent in new[]{75,90,100})percentChecks[percent]=Check(targetsPanel,"Notify at "+percent+"% of Local target",preferences.GitHubAlertPercents.Contains(percent));
        var targets=new Dictionary<string,TextBox>();var alerts=new Dictionary<string,CheckBox>();foreach(var definition in GitHubCatalog.All.Where(m=>m.Category is not ("forecast" or "allowance"))){targets[definition.Id]=Input(targetsPanel,definition.Name+" · Local target ("+definition.Unit+"; blank = none)",preferences.GitHubTargets.TryGetValue(definition.Id,out double value)?value.ToString(CultureInfo.CurrentCulture):"");alerts[definition.Id]=Check(targetsPanel,"Alert for "+definition.Name,preferences.GitHubAlertMetrics.Contains(definition.Id));}
        var status=Text("",11,Muted);DockPanel.SetDock(status,Dock.Bottom);shell.Children.Insert(1,status);
        buttons.Children.Add(Primary(Button("Save","Save GitHub metric choices",()=>{
            try{var next=preferences.Copy();var targetValues=new Dictionary<string,double>();foreach(var (id,input) in targets){if(string.IsNullOrWhiteSpace(input.Text))continue;if(!double.TryParse(input.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out double number)||!double.IsFinite(number)||number<=0||number>1e12){status.Text="Enter a positive target in the displayed unit or leave it blank.";return;}targetValues[id]=number;}
                next.GitHubTargets=targetValues;next.GitHubAlertMetrics=alerts.Where(p=>p.Value.IsChecked==true&&targetValues.ContainsKey(p.Key)).Select(p=>p.Key).ToList();next.GitHubAlertPercents=percentChecks.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToList();
                next.VisibleMetrics=next.VisibleMetrics.Where(id=>!GitHubCatalog.Ids.Contains(id)).Concat(order.Where(selected.Contains)).ToList();next.MetricOrder=next.MetricOrder.Where(id=>!GitHubCatalog.Ids.Contains(id)).Concat(order).ToList();next.GitHubDetailsOnly=details.ToList();next.Save(SettingsPath);preferences=next;SyncGitHubTiles();ApplyMetricPreferences();AdaptLayout();dialog.Close();
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text="Could not save settings. Check folder access.";}
        })));buttons.Children.Add(Button("Cancel","Cancel",dialog.Close));buttons.Children.Add(Button("Connection…","Configure GitHub",()=>{dialog.Close();ShowGitHubConnection();}));buttons.Children.Add(Button("Details & history…","GitHub details",()=>{dialog.Close();ShowGitHubDetails();}));
        CaptureFeature(dialog,"github-metrics");dialog.ShowDialog();
    }
    void ShowGitHubDetails(bool showHistory=false)
    {
        var dialog=FeatureWindow("GitHub details & history",730,760);var panel=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Text("GitHub · usage & capacity",23));var summary=Text("",12,Muted);panel.Children.Add(summary);
        var controls=new WrapPanel();controls.Children.Add(Button("Metrics…","Select GitHub metrics",()=>{dialog.Close();ShowGitHubMetrics();}));controls.Children.Add(Button("Connection…","GitHub connection",()=>{dialog.Close();ShowGitHubConnection();}));controls.Children.Add(Button("Refresh","Refresh with cache and rate-limit backoff",async()=>await UpdateProvider("github")));panel.Children.Add(controls);
        var all=Check(panel,"Show all collected metrics",showHistory);var rows=new StackPanel();panel.Children.Add(rows);
        var choice=Choose(panel,"History metric",GitHubCatalog.All.Select(m=>(object)m.Name),GitHubCatalog.Get("github-spend").Name);var range=Choose(panel,"History range",new[]{"24 hours","7 days","30 days"},"24 hours");
        var chart=new GitHubHistoryChart{Height=190,Margin=new Thickness(0,12,0,12)};panel.Children.Add(chart);var historyRows=new StackPanel();panel.Children.Add(historyRows);
        void Update(){var reading=GitHubCurrent;summary.Text=reading.Status+" · UTC month "+(reading.Period.Length>0?reading.Period:"Unknown")+" · last successful fetch "+(reading.At?.LocalDateTime.ToString("g")??"Unknown")+"\n"+reading.Detail;rows.Children.Clear();
            foreach(string id in preferences.MetricOrder.Where(id=>GitHubCatalog.Ids.Contains(id)&&(all.IsChecked==true||preferences.VisibleMetrics.Contains(id)))){var metric=GitHubMetric(id,reading);var content=new StackPanel();content.Children.Add(Text(GitHubCatalog.Get(id).Name+" · "+GitHubCatalog.Display(metric.Value,metric.Unit)+" · "+metric.State,15));content.Children.Add(Text(GitHubCatalog.Get(id).Description,11,Muted));content.Children.Add(Text(metric.Source,11,Muted));var card=Panel(content);card.ToolTip=GitHubTooltip(metric,reading);card.Margin=new Thickness(0,8,0,0);rows.Children.Add(card);}
            if(reading.Repositories.Length>0){rows.Children.Add(Text("Repository-attributed billing · opaque local labels",15));foreach(var repo in reading.Repositories.Take(100))rows.Children.Add(Text("Repository "+repo.Scope[..8]+" · "+GitHubCatalog.Display(repo.Usd,"USD"),12,Muted));if(reading.Repositories.Length>100)rows.Children.Add(Text("Showing first 100 repository summaries.",11,Muted));}
            string metricId=GitHubCatalog.All[Math.Max(0,choice.SelectedIndex)].Id;var definition=GitHubCatalog.Get(metricId);int days=range.SelectedIndex switch{1=>7,2=>30,_=>1};var now=DateTimeOffset.UtcNow;
            var points=githubHistory.Samples.Where(s=>s.Scope==reading.Scope&&s.Metric==metricId&&s.At>=now.AddDays(-days).ToUnixTimeSeconds()).OrderBy(s=>s.At).ToArray();chart.Set(points,definition,now.AddDays(-days),now);historyRows.Children.Clear();historyRows.Children.Add(Text(points.Length+" observations · current account / selection only · 30-day local retention",11,Muted));foreach(var point in points.TakeLast(12).Reverse())historyRows.Children.Add(Text(DateTimeOffset.FromUnixTimeSeconds(point.At).LocalDateTime.ToString("g")+" · "+GitHubCatalog.Display(point.Value,point.Unit)+" · UTC month "+point.Period,11,Muted));
        }
        choice.SelectionChanged+=(_,_)=>Update();range.SelectionChanged+=(_,_)=>Update();all.Checked+=(_,_)=>Update();all.Unchecked+=(_,_)=>Update();
        panel.Children.Add(Button("Clear GitHub history","Delete this installation's local GitHub history",()=>{if(MessageBox.Show(dialog,"Clear local GitHub history? This cannot be undone.","Prism",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{var empty=new GitHubHistory();empty.Save(GitHubHistoryPath);githubHistory=empty;Update();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){summary.Text="Could not clear history.";}}));
        Update();UpdateWhileOpen(dialog,Update);CaptureFeature(dialog,"github-details");dialog.ShowDialog();
    }
}
