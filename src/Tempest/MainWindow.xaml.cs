using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tempest.Core;

namespace Tempest;
public partial class MainWindow : Window
{
    readonly Engine engine;
    readonly List<string> log = [];
    readonly bool demo;
    Status? status;
    string page = "overview";
    bool busy;
    bool elevated;
    CancellationTokenSource? operation;
    Func<CancellationToken,Task>? pending;
    string pendingName = "";
    bool pendingCancelable = true;
    TextBox? logBox;
    ComboBox? recoveryList;
    ComboBox? buildsList;
    List<SpotifyBuild> builds = [];
    List<Checkpoint> checkpoints = [];
    readonly string sessionLog;
    public MainWindow(bool demoMode)
    {
        InitializeComponent(); demo = demoMode;
        SourceInitialized += (_,_) => WindowAppearance.Apply(this);
        var paths = Installation.Current();
        if(demo)
        {
            var root = Path.Combine(Path.GetTempPath(),"Tempest-Demo",Guid.NewGuid().ToString("N"));
            paths = new(Path.Combine(root,"Spotify"),Path.Combine(root,"Cli"),Path.Combine(root,"Config"),Path.Combine(root,"Data"));
        }
        engine = new(paths,demo);
        if(demo) engine.SeedDemo();
        using(var identity=WindowsIdentity.GetCurrent()) elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        Directory.CreateDirectory(Path.Combine(paths.Data,"Logs"));
        sessionLog=Path.Combine(paths.Data,"Logs",DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
        engine.Log += message=>Dispatcher.Invoke(()=>AppendLog(message));
        engine.Stage += message=>Dispatcher.Invoke(()=> { OperationDetail.Text=Clean(message); if(message.Contains("Restoring")) CancelButton.IsEnabled=false; });
        FooterStatus.Text=demo ? "Demo mode" : "Ready";
        if(demo) Title += " [DEMO]";
        Loaded += async (_,_)=> { await Refresh(); ShowPage("overview"); };
        Closing += (_,e)=>
        {
            if(busy) { e.Cancel=true; NoticeText.Text="An operation is running. Cancel it and wait for recovery to finish before closing."; }
        };
        AppendLog(demo?"Demo session started. Real Spotify files and settings are never changed.":"Tempest started. No changes made to Spotify.");
    }
    string Clean(string text)
    {
        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(profile.Length>0) text=text.Replace(profile,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);
        var name=Environment.UserName;
        if(name.Length>0) text=System.Text.RegularExpressions.Regex.Replace(text,@"(?i)(?<![\w%])"+System.Text.RegularExpressions.Regex.Escape(name)+@"(?![\w%])","[user]");
        return text;
    }
    void AppendLog(string text)
    {
        var entry=$"{DateTime.Now:HH:mm:ss}  {Clean(text)}";
        log.Add(entry);
        try { File.AppendAllText(sessionLog,entry+Environment.NewLine); } catch(IOException) { }
        if(logBox!=null) { logBox.Text=string.Join(Environment.NewLine,log); logBox.ScrollToEnd(); }
    }
    async Task Refresh()
    {
        RefreshButton.IsEnabled=false;
        try
        {
            status=await engine.Inspect(CancellationToken.None);
            checkpoints=await Task.Run(()=>engine.Recovery.List());
            NoticeText.Text=demo ? "Demo mode — try the tools without changing Spotify." : elevated ? "Reopen Tempest without administrator privileges to use the tools." : !status.Spotify ? "Install desktop Spotify to get started." : !status.Cli ? "Spotify is ready. Install Spicetify to customise it." : "Your installation is ready.";
        }
        catch(Exception e) { NoticeText.Text="Could not refresh: "+Clean(e.Message); AppendLog(e.Message); }
        finally { RefreshButton.IsEnabled=!busy; }
    }
    static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    TextBlock Text(string text,double size=12,string color="#9EB4C8",FontWeight? weight=null) => new(){Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,FontWeight=weight??FontWeights.Normal,LineHeight=size*1.5};
    Button Button(string text,Action action,bool primary=false)
    {
        var button=new Button {Content=text,Style=(Style)FindResource(primary?"Primary":"ActionButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,15,0,0)};
        button.Click+=(_,_)=>action(); return button;
    }
    void Heading(string title,string subtitle)
    {
        PageContent.Children.Add(Text(title,15,"#E9F3FC",FontWeights.SemiBold));
        var sub=Text(subtitle); sub.Margin=new(0,6,0,17); PageContent.Children.Add(sub);
    }
    Border Card(string title,string description,string button,Action action,bool primary=false)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,0,22,0)};
        stack.Children.Add(Text(title,14,"#E9F3FC",FontWeights.SemiBold));
        var detail=Text(description);detail.Margin=new(0,4,0,0);stack.Children.Add(detail);grid.Children.Add(stack);
        var command=Button(button,action,primary);command.Margin=new(0);command.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(command,1);grid.Children.Add(command);
        return new Border{Style=(Style)FindResource("Card"),Child=grid,Margin=new(0,0,0,6)};
    }
    void Pair(Border first, Border second)
    {
        PageContent.Children.Add(first);PageContent.Children.Add(second);
    }
    void Review(string name,string description,Func<CancellationToken,Task> action,bool checkpoint=true,bool cancelable=true)
    {
        if(busy) { NoticeText.Text="Wait for the current operation to finish."; return; }
        if(elevated && !demo) { NoticeText.Text="Close Tempest and reopen it without Run as administrator."; return; }
        pending=action;pendingName=name;pendingCancelable=cancelable;
        ReviewTitle.Text=name;ReviewBody.Text=description;
        ReviewFootnote.Text=demo?"Demo mode: this only affects disposable test files.":checkpoint?"Spotify will close. A recovery checkpoint is saved before files change.":"This action changes only the settings described above.";
        ReviewOverlay.Visibility=Visibility.Visible;ConfirmButton.Focus();
    }
    async void Confirm_Click(object sender,RoutedEventArgs e)
    {
        var action=pending;if(action==null || busy)return;
        pending=null;ReviewOverlay.Visibility=Visibility.Collapsed;
        busy=true;RefreshButton.IsEnabled=false;operation=new();
        OperationPanel.Visibility=Visibility.Visible;OperationTitle.Text=pendingName;Progress.Visibility=Visibility.Visible;Progress.IsIndeterminate=true;
        CancelButton.Visibility=pendingCancelable?Visibility.Visible:Visibility.Collapsed;CancelButton.IsEnabled=true;
        AppendLog("START: "+pendingName);
        try { await Task.Run(()=>action(operation.Token)); OperationTitle.Text="Completed"; OperationDetail.Text="Your changes are ready. Open Spotify when you’re ready."; AppendLog("SUCCESS: "+pendingName); }
        catch(OperationCanceledException) { OperationTitle.Text="Cancelled"; OperationDetail.Text="Operation stopped. Any saved checkpoint was restored.";AppendLog("CANCELLED: "+pendingName); }
        catch(Exception ex) { OperationTitle.Text="Needs attention";OperationDetail.Text=Clean(ex.Message);AppendLog("ERROR: "+ex.Message); }
        finally
        {
            busy=false;Progress.Visibility=Visibility.Collapsed;CancelButton.Visibility=Visibility.Collapsed;operation.Dispose();operation=null;
            await Refresh();ShowPage(page);
        }
    }
    void DismissReview_Click(object sender,RoutedEventArgs e) { pending=null;ReviewOverlay.Visibility=Visibility.Collapsed; }
    void Cancel_Click(object sender,RoutedEventArgs e) { operation?.Cancel();CancelButton.IsEnabled=false;OperationDetail.Text="Cancelling safely. Please wait for recovery to finish."; }
    void Navigate_Click(object sender,RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    void ShowPage(string key)
    {
        page=key;PageContent.Children.Clear();logBox=null;recoveryList=null;buildsList=null;
        foreach(var button in Navigation.Children.OfType<Button>()) {var selected=(string)button.Tag==key;button.Background=Brush(selected?"#30485C":"#00111F2C");button.Foreground=Brush(selected?"#EDF7FE":"#BED0DF");button.BorderBrush=Brush(selected?"#91D5F3":"#00000000");}
        PageTitle.Text=key switch{"install"=>"Install & update","repair"=>"Repair","recovery"=>"Recovery","activity"=>"Activity","appearance"=>"Settings",_=>"Overview"};
        PageEyebrow.Text=key switch{"repair"=>"BACK IN HARMONY","recovery"=>"A WAY BACK","activity"=>"BEHIND THE SCENES","appearance"=>"MAKE IT YOURS",_=>"YOUR WORKSPACE"};
        switch(key)
        {
            case "install": InstallPage();break;
            case "repair": RepairPage();break;
            case "recovery": RecoveryPage();break;
            case "activity": ActivityPage();break;
            case "appearance": AppearancePage();break;
            default: OverviewPage();break;
        }
    }
    void OverviewPage()
    {
        var statusGrid=new Grid();for(var i=0;i<3;i++)statusGrid.ColumnDefinitions.Add(new());
        var values=new[]{("Spotify",status?.SpotifyVersion??"Checking…"),("Spicetify",status?.CliVersion??"Checking…"),("Marketplace",status?.Marketplace==true?"Installed":"Not installed")};
        for(var i=0;i<3;i++)
        {
            var stack=new StackPanel();stack.Children.Add(Text(values[i].Item1,12,"#9FB5C8"));
            var value=Text(values[i].Item2,17,"#E9F3FC",FontWeights.SemiBold);value.Margin=new(0,8,0,0);stack.Children.Add(value);
            var card=new Border{Style=(Style)FindResource("Card"),Child=stack,Padding=new(18),Margin=new(i==0?0:4,0,i==2?0:4,26)};Grid.SetColumn(card,i);statusGrid.Children.Add(card);
        }
        PageContent.Children.Add(statusGrid);
        var label=Text("Manage your installation",14,"#E9F3FC",FontWeights.SemiBold);label.Margin=new(0,0,0,12);PageContent.Children.Add(label);
        PageContent.Children.Add(Card("Spicetify & Marketplace","Install the latest versions. Your existing setup is saved first.","Install / update",InstallReview,true));
        PageContent.Children.Add(Card("Reapply customisations","Restore your theme after a Spotify update.","Reapply",()=>RepairReview("apply")));
        PageContent.Children.Add(Card("Recovery","Return to a saved setup or the original Spotify.","View recovery",()=>ShowPage("recovery")));
        var line=Text($"Theme: {status?.Theme??"—"}   ·   {status?.Applied??"Checking"}",12,"#9FB5C8");line.Margin=new(0,18,0,0);PageContent.Children.Add(line);
    }
    void InstallReview() => Review("Install / update Spicetify","• Download Spicetify and Marketplace from official GitHub releases.\n• Verify release checksums.\n• Save your existing files, then apply Spicetify.\n• Keep any existing local theme.\n\nSpotify must already be installed and signed in.",engine.Install);
    void InstallPage()
    {
        Heading("Install and keep up to date","Select a tool to review its changes before continuing.");
        PageContent.Children.Add(Card("Spicetify + Marketplace","Get the latest releases, with checksum verification and a recovery checkpoint.","Install / update",InstallReview,true));
        Pair(Card("Desktop Spotify","Download Spotify’s signed installer. The Store edition is left untouched.","Install Spotify",()=>Review("Install desktop Spotify","Download and verify Spotify’s installer, then launch it. Complete any Spotify setup windows that appear. This external installer cannot be cancelled from Tempest once launched. Existing Spotify files may be updated by Spotify’s installer.",engine.InstallSpotify,false,false)),Card("Check releases","See the latest Spicetify and Marketplace versions without changing files.","Check for updates",()=>_ = CheckUpdates()));
        PageContent.Children.Add(Card("Auto-apply shortcut","Create a separate “Spotify - Tempest” desktop shortcut that reapplies Spicetify when opened.","Create shortcut",()=>Review("Create auto-apply shortcut","Add a new desktop shortcut that runs spicetify auto. Your existing shortcuts and startup settings stay as they are.",engine.CreateShortcut,false)));
    }
    async Task CheckUpdates()
    {
        if(busy)return;
        NoticeText.Text="Checking official release metadata…";
        try
        {
            if(demo) { NoticeText.Text="Demo: Spicetify 2.46.2 · Marketplace 1.0.9 (sample versions)";return; }
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var cli=await engine.Latest("cli",timeout.Token);var mp=await engine.Latest("marketplace",timeout.Token);
            NoticeText.Text=$"LATEST RELEASES   Spicetify {cli.Tag}  ·  Marketplace {mp.Tag}";
        }
        catch(Exception e){NoticeText.Text="Update check unavailable: "+Clean(e.Message);}
    }
    void RepairReview(string kind)
    {
        var (name,description)=kind switch
        {
            "reset"=>("Disable customisations","Save a checkpoint, then disable themes, extensions and custom apps in Spicetify’s configuration. Their files are kept. Marketplace customisations stored inside Spotify may need a separate Marketplace reset after recovery."),
            "location"=>("Repair Spotify location","Point Spicetify at the standard desktop Spotify installation and its preferences file, then reapply. Custom Spotify paths are not managed in this version."),
            "devtools"=>("Enable developer tools","Enable Spotify’s developer tools. After opening Spotify, use Ctrl+Shift+I to inspect errors. This does not automatically reset Marketplace."),
            _=>("Reapply Spicetify","Save a checkpoint, refresh Spotify’s backup and apply your existing theme, extensions and Marketplace. Spotify will remain closed when finished.")
        };
        Review(name,description,ct=>engine.Repair(kind,ct));
    }
    void RepairPage()
    {
        Heading("Fix a problem","Start with reapply. File repairs save a recovery checkpoint first.");
        Pair(Card("Theme disappeared","Usually happens after Spotify updates.","Reapply",()=>RepairReview("apply"),true),Card("Wrong Spotify location","Reconnect the desktop client and preferences.","Repair location",()=>RepairReview("location")));
        Pair(Card("Broken customisation","Disable extensions, themes and custom apps.","Disable customisations",()=>RepairReview("reset")),Card("Command not found","Add Spicetify to your user PATH.","Repair PATH",()=>Review("Repair PATH","Add the Spicetify folder to your user PATH if missing. Open a new terminal afterwards.",engine.FixPath,false)));
        Pair(Card("Inspect an issue","Enable Spotify’s developer tools.","Enable tools",()=>RepairReview("devtools")),Card("Update control","Temporarily pause updates after a compatibility break.","Update options",UpdateReview));
    }
    void UpdateReview()
    {
        if(busy)return;
        PageContent.Children.Clear();Heading("Spotify update controls","Blocking updates also delays fixes from Spotify. Re-enable them after compatibility is restored.");
        Pair(Card("Pause updates","Uses Spicetify’s update-block command. Older CLI versions may not support it.","Block updates",()=>Review("Block Spotify updates","Ask Spicetify to block Spotify updates. This is separate from file checkpoints. You can undo it here using Allow updates.",ct=>engine.Updates(true,ct),false)),Card("Resume updates","Remove the update block using Spicetify.","Allow updates",()=>Review("Allow Spotify updates","Ask Spicetify to unblock Spotify updates. Legacy blocks created by the old batch installer may require separate cleanup.",ct=>engine.Updates(false,ct),false)));
    }
    void RecoveryPage()
    {
        Heading("Restore your setup","Use a checkpoint, restore original Spotify, or choose an older version.");
        var stack=new StackPanel();stack.Children.Add(Text("Saved checkpoints",15,"#E9F3FC",FontWeights.SemiBold));
        stack.Children.Add(Text("Stored locally. They can include your configuration and personal Spotify files; they are never exported or uploaded.",11));
        recoveryList=new ComboBox{ItemsSource=checkpoints,SelectedIndex=checkpoints.Count>0?0:-1};
        System.Windows.Automation.AutomationProperties.SetName(recoveryList,"Saved checkpoints");
        stack.Children.Add(recoveryList);
        var restore=Button("Restore checkpoint",()=> { if(recoveryList?.SelectedItem is Checkpoint cp) Review("Restore checkpoint",$"Restore the files saved by “{cp.Action}”. Your current state is saved first, so you can undo this recovery.",ct=>engine.RestoreCheckpoint(cp,ct)); });
        restore.IsEnabled=checkpoints.Count>0;stack.Children.Add(restore);
        if(checkpoints.Count==0)stack.Children.Add(Text("No checkpoints yet. One is created automatically before file changes.",11));
        PageContent.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=stack,Margin=new(0,0,0,12)});
        Pair(Card("Original Spotify","Restore the unmodified client. Keep Spicetify and your settings.","Restore original",()=>Review("Restore original Spotify","Restore Spotify’s original files using Spicetify. Keep themes, extensions and configuration for later.",ct=>engine.RestoreVanilla(false,ct))),Card("Remove Spicetify","Restore Spotify first, then remove the CLI. Keep your customisation files.","Remove Spicetify",()=>Review("Remove Spicetify","First restore and verify vanilla Spotify. Only then remove the Spicetify program folder. Configuration, themes and recovery checkpoints are kept. Existing auto-apply shortcuts will no longer work; use your normal Spotify shortcut. Update blocks are not changed.",ct=>engine.RestoreVanilla(true,ct))));
        PageContent.Children.Add(Card("Spotify version history","Advanced: select a version, verify Spotify’s signature, and save a full checkpoint before replacement.","Choose a version",()=>_ = ShowBuilds()));
    }
    async Task ShowBuilds()
    {
        if(busy)return;
        NoticeText.Text="Loading Spotify versions…";
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));builds=await engine.Builds(timeout.Token);
            if(page!="recovery")return;
            PageContent.Children.Clear();Heading("Choose a Spotify version","Version catalogue: LoaderSpot. Downloads must pass Spotify signature checks. Compatibility with Spicetify is not guaranteed.");
            buildsList=new ComboBox{ItemsSource=builds,SelectedIndex=builds.Count>0?0:-1};PageContent.Children.Add(buildsList);
            PageContent.Children.Add(Text("A full Spotify checkpoint can take extra disk space. Login and preferences are preserved. Updates are not automatically blocked."));
            PageContent.Children.Add(Button("Review version change",()=> { if(buildsList?.SelectedItem is SpotifyBuild build) Review("Install Spotify "+build.Version,"Download the selected build, verify Spotify’s signature and exact version, save a full checkpoint, then replace Spotify. Reapply Spicetify if installed. A failed change triggers recovery. Unsupported mirror hosts are rejected.",ct=>engine.Downgrade(build,ct)); },true));
            NoticeText.Text=builds.Count == 0 ? "No versions with supported download hosts are available. No files changed." : $"{builds.Count} supported versions available. Newest is listed first.";
        }
        catch(Exception e){NoticeText.Text="Could not load versions: "+Clean(e.Message);}
    }
    void ActivityPage()
    {
        Heading("Operation log","Stored locally. User-profile paths are redacted before logging or export.");
        logBox=new TextBox{Text=string.Join(Environment.NewLine,log),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=340,Background=Brush("#0D1B27"),Foreground=Brush("#ABC9DA"),BorderBrush=Brush("#304A5E"),Padding=new(13),FontFamily=new("Cascadia Mono, Consolas"),FontSize=11};PageContent.Children.Add(logBox);
        PageContent.Children.Add(Button("Save redacted log…",ExportLog));
        if(demo)
        {
            var fail=new CheckBox{Content="Simulate failure on the next file operation",IsChecked=engine.FailNextDemo};fail.Checked+=(_,_)=>engine.FailNextDemo=true;fail.Unchecked+=(_,_)=>engine.FailNextDemo=false;fail.Margin=new(0,22,0,0);PageContent.Children.Add(fail);
        }
    }
    void AppearancePage()
    {
        Heading("About Tempest", "A local utility for managing Spicetify on Windows.");
        var info=new StackPanel();
        info.Children.Add(Text("Tempest " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3),18,"#E9F3FC",FontWeights.SemiBold));
        var description=Text("Dark blue appearance · Windows Fluent controls");description.Margin=new(0,8,0,16);info.Children.Add(description);
        info.Children.Add(Text("No account or telemetry. Recovery checkpoints and activity logs stay on this computer."));
        PageContent.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=info,Margin=new(0,0,0,12)});
        PageContent.Children.Add(Card("Activity log","View recent operations and export a redacted log.","View activity",()=>ShowPage("activity")));
        var note=Text("Independent community utility. Not affiliated with Spotify or Spicetify.",12);note.Margin=new(0,20,0,0);PageContent.Children.Add(note);
    }
    void ExportLog()
    {
        var dialog=new SaveFileDialog{Filter="Text log|*.txt",FileName="tempest-log.txt"};
        if(dialog.ShowDialog(this)==true) {File.WriteAllText(dialog.FileName,string.Join(Environment.NewLine,log.Select(Clean)));NoticeText.Text="Redacted log saved locally. Nothing was uploaded.";}
    }
    async void Refresh_Click(object sender,RoutedEventArgs e){if(!busy){await Refresh();ShowPage(page);}}
    void OpenSpotify_Click(object sender,RoutedEventArgs e)
    {
        if(busy){NoticeText.Text="Wait for the current operation to finish before opening Spotify.";return;}
        if(demo){NoticeText.Text="Demo mode: Open Spotify does not launch the real app.";return;}
        if(!File.Exists(engine.Paths.SpotifyExe)){ShowPage("install");return;}
        Process.Start(new ProcessStartInfo(engine.Paths.SpotifyExe){UseShellExecute=true});
    }
    void Docs_Click(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo("https://spicetify.app/docs/getting-started"){UseShellExecute=true});
    void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
