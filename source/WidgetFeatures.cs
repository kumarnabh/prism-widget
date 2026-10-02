using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Prism;

public sealed partial class Widget
{
    WidgetPreferences preferences=new();
    readonly ProviderSchedule schedule=new();
    readonly Dictionary<string,JsonElement> providerData=new();
    UsageHistory history=new();LowQuotaAlerts lowAlerts=new();ProfileStore profiles=new();
    TrayIcon? tray;bool featureChecks,placing;
    double freeDiskGb;
    string SettingsPath=>System.IO.Path.Combine(Data,"preferences.json");
    void InitializeFeatures(bool startInTray)
    {
        Opacity=preferences.Opacity;ApplyMetricPreferences();
        if(!preview&&!selftest){history=UsageHistory.Load(System.IO.Path.Combine(Data,"usage-history.json"),DateTimeOffset.UtcNow);lowAlerts=LowQuotaAlerts.Load(System.IO.Path.Combine(Data,"low-alerts.json"));profiles=ProfileStore.Load(System.IO.Path.Combine(Data,"profiles.json"));}
        if(profiles.Items.Count==0){
            profiles.Items.Add(new("Work",442,858,false,true,1,WidgetPreferences.MetricIds.ToList(),WidgetPreferences.MetricIds.ToList(),new(true)));
            profiles.Items.Add(new("Gaming",560,165,true,true,.9,WidgetPreferences.MetricIds.ToList(),new(){"cpu","ram","disk","net"},new()));
            profiles.Items.Add(new("Presentation",400,220,true,false,1,WidgetPreferences.MetricIds.ToList(),new(){"cpu","ram"},new()));
        }
        SourceInitialized+=(_,_)=>{
            var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)!;
            string identity=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))[..16];
            uint show=TrayIcon.RegisterWindowMessage("Prism.Show."+identity);
            source.AddHook((IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>{
                if((uint)message==show){Dispatcher.BeginInvoke(ShowFromTray);handled=true;}
                if(!preview&&!selftest&&(message==0x232||message==0x2e0))Dispatcher.BeginInvoke(()=>ConstrainToDisplay(message==0x232&&preferences.SnapEdges));
                return IntPtr.Zero;
            });
        };
        Loaded+=(_,_)=>{if(!preview&&!selftest){ConfigureTray();if(startInTray&&tray is not null)HideToTray();}};
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized&&tray is not null)HideToTray();};
    }
    void ConfigureTray()
    {
        if(!preferences.TrayEnabled){if(!IsVisible)ShowFromTray();tray?.Dispose();tray=null;return;}
        if(tray is not null)return;
        try{tray=new TrayIcon(this,System.IO.Path.Combine(Root,"assets","prism.ico"),ShowFromTray,ShowTrayMenu);}catch{tray=null;footer.Text=L.T("System tray is unavailable. Prism will stay visible.");}
    }
    void ShowFromTray(){Show();WindowState=WindowState.Normal;Activate();}
    void HideToTray(){if(tray is null)return;SavePosition();DismissReminder();Hide();}
    void ShowTrayMenu()
    {
        var menu=new ContextMenu();
        void AddItem(string name,Action action){var item=new MenuItem{Header=L.T(name)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        AddItem("Show Prism",ShowFromTray);AddItem("Refresh",async()=>await UpdateProviders(true));
        AddItem("Settings",()=>{ShowFromTray();ShowPreferences();});AddItem("Usage history",()=>{ShowFromTray();ShowHistory();});
        menu.Items.Add(new Separator());AddItem("Exit Prism",Close);menu.Placement=PlacementMode.MousePoint;menu.IsOpen=true;
    }
    void ConstrainToDisplay(bool snap)
    {
        if(placing||WindowState!=WindowState.Normal)return;placing=true;
        try{
            var work=DesktopLayout.Current(this).Work;MaxWidth=work.Width;MaxHeight=Math.Max(120,work.Height);
            var next=DesktopLayout.Constrain(new Rect(Left,Top,ActualWidth,ActualHeight),work,snap);
            Width=next.Width;Height=next.Height;Left=next.Left;Top=next.Top;AdaptLayout();
        }finally{placing=false;}
    }
    void ApplyMetricPreferences()
    {
        preferences.Normalize();metricGrid.Children.Clear();providerGrid.Children.Clear();
        foreach(string id in preferences.MetricOrder){
            bool visible=preferences.VisibleMetrics.Contains(id);metricTiles[id].Element.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            if(visible)metricGrid.Children.Add(metricTiles[id].Element);
            if(cards.TryGetValue(id,out var card)){card.Element.Visibility=visible?Visibility.Visible:Visibility.Collapsed;if(visible)providerGrid.Children.Add(card.Element);}
        }
        void Pair(Grid grid,Dictionary<string,FrameworkElement> elements){
            grid.Children.Clear();foreach(var element in elements.Values){element.Visibility=Visibility.Collapsed;Grid.SetColumnSpan(element,1);}
            var visible=preferences.MetricOrder.Where(id=>elements.ContainsKey(id)&&preferences.VisibleMetrics.Contains(id)).ToArray();
            grid.Visibility=visible.Length==0?Visibility.Collapsed:Visibility.Visible;
            for(int i=0;i<visible.Length;i++){var element=elements[visible[i]];element.Visibility=Visibility.Visible;Add(grid,element,i*2);if(visible.Length==1)Grid.SetColumnSpan(element,3);}
        }
        Pair(systemMetrics,new(){{"cpu",cpuPanel},{"ram",ramPanel}});Pair(extras,new(){{"disk",diskStack},{"net",netStack}});
        systems.Visibility=systemMetrics.Visibility==Visibility.Collapsed&&extras.Visibility==Visibility.Collapsed?Visibility.Collapsed:Visibility.Visible;
        sectionLabel.Visibility=providerGrid.Children.Count==0?Visibility.Collapsed:Visibility.Visible;
    }
    void RefreshDisplayedProviders()
    {
        if(offline)return;var now=DateTimeOffset.UtcNow;latestReadings.Clear();
        foreach(var (id,card) in cards){
            if(providerData.TryGetValue(id,out var data)){
                var reading=QuotaSnapshot.Read(data,now);
                // A long user-selected schedule must not leave old readings marked current.
                if(reading.Captured is DateTimeOffset at&&now-at>TimeSpan.FromMinutes(10)&&reading.Windows.Count>0){
                    var node=System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!.AsObject();node["status"]="Stale";
                    using var stale=JsonDocument.Parse(node.ToJsonString());card.Update(stale.RootElement);reading=reading with{Status="Stale"};
                }else card.Update(data);
                latestReadings[ProviderName(id)]=reading;
            }else card.MarkOffline();
            int seconds=schedule.Remaining(id,now);string countdown=seconds==0?L.T("due now"):TimeSpan.FromSeconds(seconds).ToString(seconds>=3600?@"h\:mm\:ss":@"m\:ss");
            string info=card.Element.ToolTip?.ToString()??ProviderName(id);int suffix=info.IndexOf('\u2063');if(suffix>=0)info=info[..suffix];
            card.Element.ToolTip=info+"\u2063\n"+L.F("Next check: {0}",countdown);
        }
        if(!busy){string next=string.Join(" · ",WidgetPreferences.ProviderIds.Select(id=>ProviderName(id)+" "+TimeSpan.FromSeconds(schedule.Remaining(id,now)).ToString(@"h\:mm\:ss")));footer.Text=L.F("{0} of 4 sources current",cards.Values.Count(c=>c.Health=="ready"))+" · ↻ "+TimeSpan.FromSeconds(WidgetPreferences.ProviderIds.Min(id=>schedule.Remaining(id,now))).ToString(@"m\:ss");footer.ToolTip=L.F("Next check: {0}",next);foreach(var button in refreshButtons)button.ToolTip=footer.ToolTip;}
    }
    void CheckLowQuota()
    {
        if(preview||selftest||closed||reminderPopup.IsOpen||!preferences.LowQuotaEnabled||(!IsVisible&&tray is null))return;
        lowAlerts.Notified??=new();var before=new Dictionary<string,long>(lowAlerts.Notified);
        var messages=lowAlerts.Due(latestReadings,DateTimeOffset.UtcNow,preferences.LowQuotaThreshold);
        if(messages.Count==0){if(before.Count!=lowAlerts.Notified.Count)SaveLowAlerts();return;}
        if(tray is not null){if(!tray.Notify(L.T("Low quota"),string.Join("\n",messages))){lowAlerts.Notified=before;return;}}
        else{
            DismissReminder();var content=new StackPanel();content.Children.Add(Text("Low quota",14));foreach(var message in messages.Take(3))content.Children.Add(Text(message,12));
            var panel=Panel(content,"#F02A344A",14);panel.Width=310;reminderPopup.Child=panel;reminderPopup.IsOpen=true;reminderTimer.Start();
        }
        SaveLowAlerts();
    }
    void SaveLowAlerts(){try{lowAlerts.Save(System.IO.Path.Combine(Data,"low-alerts.json"));}catch(IOException){}catch(UnauthorizedAccessException){}}
    Window FeatureWindow(string title,double width=660,double height=660)=>new(){Title="Prism · "+L.T(title),Owner=this,Width=width,Height=height,MaxWidth=SystemParameters.WorkArea.Width-24,MaxHeight=SystemParameters.WorkArea.Height-40,MinWidth=380,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#121E30"),Foreground=new SolidColorBrush(Ink),FontFamily=FontFamily};
    static CheckBox Check(StackPanel panel,string label,bool value){var box=new CheckBox{Content=L.T(label),IsChecked=value,Foreground=new SolidColorBrush(Ink),Margin=new Thickness(0,7,0,7)};panel.Children.Add(box);return box;}
    static ComboBox Choose(StackPanel panel,string label,IEnumerable<object> values,object selected){panel.Children.Add(Text(label,12,Muted));var combo=new ComboBox{ItemsSource=values,SelectedItem=selected,Margin=new Thickness(0,6,0,14),Padding=new Thickness(7),Foreground=Brush("#142236"),Background=Brush("#EDF4FC")};panel.Children.Add(combo);return combo;}
    void ShowPreferences()
    {
        var dialog=FeatureWindow("Settings");var shell=new DockPanel{Margin=new Thickness(20)};dialog.Content=shell;
        var saveBar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(saveBar,Dock.Bottom);shell.Children.Add(saveBar);
        var tabs=new TabControl{Background=Brush("#121E30"),BorderBrush=Brush("#486079"),Foreground=Brush("#142236")};shell.Children.Add(tabs);
        StackPanel Tab(string name){var panel=new StackPanel{Margin=new Thickness(16)};tabs.Items.Add(new TabItem{Header=L.T(name),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;}
        var general=Tab("General");
        var trayCheck=Check(general,"Enable system tray",preferences.TrayEnabled);var startup=Check(general,"Start with Windows",StartupRegistration.Enabled);var snap=Check(general,"Snap to screen edges",preferences.SnapEdges);var hours=Check(general,"Use 24-hour time",preferences.Use24Hour);
        var languageNames=new[]{"English","हिन्दी","Español","Français"};var languageIds=new[]{"en","hi","es","fr"};var language=Choose(general,"Language",languageNames,languageNames[Array.IndexOf(languageIds,preferences.Language)]);
        general.Children.Add(Text("Opacity",12,Muted));var opacity=new Slider{Minimum=.65,Maximum=1,Value=preferences.Opacity,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,16)};general.Children.Add(opacity);
        general.Children.Add(Button("Connections…","Connections…",()=>{dialog.Close();ShowConnection("cursor","Cursor");}));general.Children.Add(Button("World clocks…","World clocks…",()=>{dialog.Close();ShowClocks();}));
        general.Children.Add(Text("Startup is optional and uses only your Windows account. Closing Prism exits; Hide to tray keeps monitoring.",11,Muted));
        var rates=Tab("Refresh rates");rates.Children.Add(Text("Provider caches and rate limits still apply. Hover a provider or the footer for its next check.",12,Muted));
        var intervals=new[]{60,300,600,900,1800,3600};var rateBoxes=new Dictionary<string,ComboBox>();
        foreach(string id in WidgetPreferences.ProviderIds){var box=Choose(rates,ProviderName(id),intervals.Cast<object>(),preferences.RefreshSeconds[id]);box.ItemStringFormat="{0} s";rateBoxes[id]=box;}
        var metrics=Tab("Metrics");metrics.Children.Add(Text("Choose visible metrics and their order. The compact grid uses the full order; the dashboard keeps paired system groups and orders AI cards.",12,Muted));
        var ordered=preferences.MetricOrder.ToList();var enabled=preferences.VisibleMetrics.ToHashSet();var list=new ListBox{Height=275,Margin=new Thickness(0,12,0,12),Background=Brush("#1C2D43"),Foreground=new SolidColorBrush(Ink)};metrics.Children.Add(list);
        void RenderMetrics(string? selected=null){list.Items.Clear();foreach(var id in ordered){var box=new CheckBox{Content=L.T(MetricName(id)),IsChecked=enabled.Contains(id),Foreground=new SolidColorBrush(Ink),Padding=new Thickness(6),Tag=id};box.Checked+=(_,_)=>enabled.Add(id);box.Unchecked+=(_,_)=>enabled.Remove(id);list.Items.Add(box);if(id==selected)list.SelectedItem=box;}}
        RenderMetrics();var move=new StackPanel{Orientation=Orientation.Horizontal};
        void Move(int delta){if(list.SelectedItem is not CheckBox item)return;string id=(string)item.Tag;int index=ordered.IndexOf(id),target=index+delta;if(target<0||target>=ordered.Count)return;ordered.RemoveAt(index);ordered.Insert(target,id);RenderMetrics(id);}
        move.Children.Add(Button("Move up","Move up",()=>Move(-1)));move.Children.Add(Button("Move down","Move down",()=>Move(1)));metrics.Children.Add(move);
        var alerts=Tab("Alerts");var low=Check(alerts,"Enable low-quota alerts",preferences.LowQuotaEnabled);var threshold=Choose(alerts,"Remaining threshold (%)",new[]{5,10,15,20,25,30,40,50}.Cast<object>(),preferences.LowQuotaThreshold);
        alerts.Children.Add(Text("Each low-quota window alerts once until it recovers or resets. Background alerts use Windows notifications; your notification settings may suppress them.",12,Muted));
        alerts.Children.Add(Button("Reset reminders…","Reset reminders…",()=>{dialog.Close();ShowResetReminders();}));
        var historyCheck=Check(alerts,"Save local quota history",preferences.HistoryEnabled);alerts.Children.Add(Text("History stores only provider, timestamp and remaining percentage for 30 days. Disabling recording preserves existing history until you clear it.",11,Muted));alerts.Children.Add(Button("Usage history","Usage history",()=>{dialog.Close();ShowHistory();}));
        BuildProfileSettings(Tab("Profiles"),dialog);BuildUpdateSettings(Tab("Updates"),dialog);
        var status=Text("",11,Muted);DockPanel.SetDock(status,Dock.Bottom);shell.Children.Insert(1,status);
        saveBar.Children.Add(Button("Cancel","Cancel",dialog.Close));
        saveBar.Children.Add(Button("Save","Save",()=>{
            if(enabled.Count==0){status.Text=L.T("At least one metric is required.");return;}
            var next=preferences.Copy();next.TrayEnabled=trayCheck.IsChecked==true||startup.IsChecked==true;next.SnapEdges=snap.IsChecked==true;next.Use24Hour=hours.IsChecked==true;next.Language=languageIds[Math.Max(0,language.SelectedIndex)];next.Opacity=opacity.Value;
            next.LowQuotaEnabled=low.IsChecked==true;next.LowQuotaThreshold=threshold.SelectedItem is int value?value:20;next.HistoryEnabled=historyCheck.IsChecked==true;next.MetricOrder=ordered.ToList();next.VisibleMetrics=ordered.Where(enabled.Contains).ToList();
            foreach(var (id,box) in rateBoxes)next.RefreshSeconds[id]=box.SelectedItem is int seconds?seconds:(id=="codex"?60:300);
            try{
                next.Save(SettingsPath);
                try{if((startup.IsChecked==true)!=StartupRegistration.Enabled)StartupRegistration.Set(startup.IsChecked==true);}catch{preferences.Save(SettingsPath);throw;}
                preferences=next;L.Set(next.Language);Opacity=next.Opacity;schedule.Reset();ConfigureTray();ApplyMetricPreferences();AdaptLayout();dialog.Close();
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException){status.Text=L.T("Could not save settings. Check folder access or an existing startup registration.");}
        }));
        if(selftest)dialog.Loaded+=(_,_)=>{
            dialog.UpdateLayout();var visual=(FrameworkElement)dialog.Content;
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*2),(int)Math.Ceiling(visual.ActualHeight*2),192,192,PixelFormats.Pbgra32);
            bitmap.Render(VectorSnapshot.Capture(visual));var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var output=File.Create(System.IO.Path.Combine(Root,"preview-settings-"+L.Language+".png")))png.Save(output);
            dialog.Dispatcher.BeginInvoke(dialog.Close);
        };
        dialog.ShowDialog();
    }
    static string MetricName(string id)=>id switch{"cpu"=>"CPU","ram"=>"Memory","disk"=>"Free disk","net"=>"Network",_=>ProviderName(id)};
    void BuildProfileSettings(StackPanel panel,Window owner)
    {
        var choices=Choose(panel,"Profiles",profiles.Items.Select(p=>(object)p.Name),profiles.Items.FirstOrDefault()?.Name??"");var name=Input(panel,"Profile name","");name.MaxLength=32;var status=Text("",11,Muted);panel.Children.Add(status);
        panel.Children.Add(Text("Profiles save size, density, pinning, opacity, visible metrics, order and clocks. They never include account credentials.",12,Muted));
        panel.Children.Add(Button("Save current layout","Save current layout",()=>{
            string label=name.Text.Trim();if(label.Length==0||label.Any(char.IsControl)){status.Text=L.T("Enter a profile name.");return;}
            int index=profiles.Items.FindIndex(p=>string.Equals(p.Name,label,StringComparison.OrdinalIgnoreCase));if(index<0&&profiles.Items.Count>=20){status.Text=L.T("Maximum 20 profiles.");return;}
            var value=new LayoutProfile(label,Width,Height,compact,Topmost,Opacity,preferences.MetricOrder.ToList(),preferences.VisibleMetrics.ToList(),clockPreferences);
            try{var copy=new ProfileStore{Items=profiles.Items.ToList()};if(index>=0)copy.Items[index]=value;else copy.Items.Add(value);copy.Save(System.IO.Path.Combine(Data,"profiles.json"));profiles=copy;choices.ItemsSource=profiles.Items.Select(p=>p.Name).ToList();choices.SelectedItem=label;status.Text=L.T("Saved.");}catch(IOException){status.Text=L.T("Could not save profile.");}catch(UnauthorizedAccessException){status.Text=L.T("Could not save profile.");}
        }));
        panel.Children.Add(Button("Load profile","Load profile",()=>{
            var profile=profiles.Items.FirstOrDefault(p=>p.Name==(choices.SelectedItem as string));if(profile is null)return;
            try{ApplyProfile(profile);owner.Close();}catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text=L.T("Could not save profile.");}
        }));
        panel.Children.Add(Button("Delete profile","Delete profile",()=>{
            if(choices.SelectedItem is not string selected||MessageBox.Show(owner,L.T("Delete profile")+" · "+selected+"?","Prism",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            try{var next=new ProfileStore{Items=profiles.Items.Where(p=>p.Name!=selected).ToList()};next.Save(System.IO.Path.Combine(Data,"profiles.json"));profiles=next;choices.ItemsSource=profiles.Items.Select(p=>p.Name).ToList();}catch(IOException){status.Text=L.T("Could not save profile.");}catch(UnauthorizedAccessException){status.Text=L.T("Could not save profile.");}
        }));
    }
    void ApplyProfile(LayoutProfile profile)
    {
        var next=preferences.Copy();next.MetricOrder=profile.Order.ToList();next.VisibleMetrics=profile.Visible.ToList();next.Opacity=profile.Opacity;next.Normalize();next.Save(SettingsPath);
        try{profile.Clocks.Save(System.IO.Path.Combine(Data,"clocks.json"));}catch{preferences.Save(SettingsPath);throw;}
        preferences=next;clockPreferences=profile.Clocks;Opacity=next.Opacity;ApplyMetricPreferences();SetPinned(profile.Pinned);ApplyPreset(profile.Width,profile.Height);compact=profile.Compact;AdaptLayout();SavePosition();
    }
    void BuildUpdateSettings(StackPanel panel,Window owner)
    {
        panel.Children.Add(Text("Prism "+typeof(Widget).Assembly.GetName().Version?.ToString(3),23));
        panel.Children.Add(Text("Checks contact GitHub only when requested. ZIPs are verified against the published SHA-256 checksum. Binaries remain unsigned; downloads never install or replace files automatically.",12,Muted));
        var status=Text("",12);status.Margin=new Thickness(0,12,0,12);panel.Children.Add(status);ReleaseUpdate? available=null;var token=new CancellationTokenSource();owner.Closed+=(_,_)=>token.Cancel();
        var download=Button("Download verified ZIP","Download verified ZIP",()=>{});download.IsEnabled=false;
        Button check=null!;check=Button("Check for updates","Check for updates",async()=>{
            status.Text=L.T("Checking…");check.IsEnabled=download.IsEnabled=false;
            try{using var client=new ReleaseUpdates();available=await client.Check(token.Token);if(!owner.IsVisible)return;var current=typeof(Widget).Assembly.GetName().Version!;bool newer=available.Version>new Version(current.Major,current.Minor,current.Build);status.Text=newer?L.F("Prism {0} is available ({1:0} MB).",available.Version,available.Size/1048576d):L.T("You are up to date.");download.IsEnabled=newer;}
            catch(OperationCanceledException){}catch{if(owner.IsVisible)status.Text=L.T("Update check failed. Please try again later.");}
            finally{if(owner.IsVisible)check.IsEnabled=true;}
        });panel.Children.Add(check);panel.Children.Add(download);
        download.Click+=async (_,_)=>{
            if(available is null)return;check.IsEnabled=download.IsEnabled=false;status.Text=L.T("Downloading and verifying…");
            try{using var client=new ReleaseUpdates();await client.Download(available,System.IO.Path.Combine(Data,"downloads"),token.Token);if(owner.IsVisible)status.Text=L.T("Verified download ready. Extract it into a new folder and run Setup.cmd.");}
            catch(OperationCanceledException){}catch{if(owner.IsVisible)status.Text=L.T("Download failed or could not be verified.");}
            finally{if(owner.IsVisible){check.IsEnabled=true;download.IsEnabled=true;}}
        };
        panel.Children.Add(Button("Open downloads folder","Open downloads folder",()=>{var path=System.IO.Path.Combine(Data,"downloads");Directory.CreateDirectory(path);OpenUrl(path);}));
    }
    void ShowHistory()
    {
        var dialog=FeatureWindow("Usage history",660,520);var stack=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        stack.Children.Add(Text("Remaining allowance (%)",23));
        var provider=Choose(stack,"Metrics",WidgetPreferences.ProviderIds.Select(id=>(object)ProviderName(id)),"Codex");var ranges=new[]{L.T("24 hours"),L.T("7 days"),L.T("30 days")};var range=Choose(stack,"Usage history",ranges,ranges[0]);
        var summary=Text("",12,Muted);var chart=new HistoryChart{Height=185,Margin=new Thickness(0,10,0,10)};stack.Children.Add(chart);stack.Children.Add(summary);
        void Update(){string id=WidgetPreferences.ProviderIds[Math.Max(0,provider.SelectedIndex)];int days=range.SelectedIndex switch{1=>7,2=>30,_=>1};var end=DateTimeOffset.UtcNow;var start=end.AddDays(-days);var points=history.Points.Where(p=>p.Provider==id&&p.At>=start.ToUnixTimeSeconds()).OrderBy(p=>p.At).ToArray();chart.Set(points,start,end);summary.Text=points.Length==0?L.T("No readings yet. History fills as fresh quota arrives."):L.F("{0} samples · latest {1:0.#}%",points.Length,points[^1].Remaining);}
        provider.SelectionChanged+=(_,_)=>Update();range.SelectionChanged+=(_,_)=>Update();Update();
        stack.Children.Add(Button("Clear history","Clear history",()=>{if(MessageBox.Show(dialog,L.T("Clear the local quota history? This cannot be undone."),"Prism",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{var cleared=new UsageHistory();cleared.Save(System.IO.Path.Combine(Data,"usage-history.json"));history=cleared;Update();}catch(IOException){summary.Text=L.T("Could not save settings.");}catch(UnauthorizedAccessException){summary.Text=L.T("Could not save settings.");}}));dialog.ShowDialog();
    }
}
