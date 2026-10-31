using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempest.Core;

public sealed class Engine
{
    public Installation Paths { get; }
    public RecoveryStore Recovery { get; }
    public bool Demo { get; }
    public bool FailNextDemo { get; set; }
    readonly ICommandRunner runner;
    readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(8) };
    readonly SemaphoreSlim gate = new(1, 1);
    public event Action<string>? Log;
    public event Action<string>? Stage;
    string[] Targets => [Paths.Config, Paths.Cli, Path.Combine(Paths.Spotify,"Apps"), Paths.SpotifyExe];
    public Engine(Installation paths, bool demo = false, ICommandRunner? commandRunner = null)
    {
        Paths = paths; Demo = demo; runner = commandRunner ?? new CommandRunner();
        Recovery = new(Path.Combine(paths.Data, "Recovery"), [..Targets, paths.Spotify]);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tempest-Spicetify", "0.1"));
    }
    void Write(string message) => Log?.Invoke(message);
    void Step(string message) { Stage?.Invoke(message); Write(message); }
    async Task<CommandResult> Run(string file, IEnumerable<string> args, CancellationToken token, bool check = true, IReadOnlyDictionary<string,string>? env = null)
    {
        var result = await runner.Run(file, args, token, Write, env);
        if(check && result.ExitCode != 0) throw new IOException($"{Path.GetFileName(file)} exited with code {result.ExitCode}. {result.Output.Trim()}");
        return result;
    }
    Task<CommandResult> Spx(CancellationToken token, params string[] args) => Run(Paths.CliExe, args, token);
    string PowerShell => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    Task<CommandResult> Ps(string script, CancellationToken token, IReadOnlyDictionary<string,string>? env = null) => Run(PowerShell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script))], token, env: env);

    public async Task<Status> Inspect(CancellationToken token)
    {
        var spotify = File.Exists(Paths.SpotifyExe);
        var cli = File.Exists(Paths.CliExe);
        var cfg = File.Exists(Paths.Ini) ? await File.ReadAllTextAsync(Paths.Ini, token) : "";
        var version = "Not installed";
        if(cli)
        {
            if(Demo) version = "2.46.2 · demo";
            else
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try { version = (await runner.Run(Paths.CliExe, ["-v"], timeout.Token)).Output.Trim(); }
                catch(Exception e) when (e is IOException or OperationCanceledException) { version = "Installed · check failed"; }
            }
        }
        var state = !spotify ? "Not installed" : File.Exists(Path.Combine(Paths.Spotify, "Apps", "xpui.spa")) ? "Needs applying" : Directory.Exists(Path.Combine(Paths.Spotify,"Apps","xpui")) ? "Applied (detected)" : "Unknown";
        return new(spotify, cli, File.Exists(Path.Combine(Paths.Marketplace,"manifest.json")) && File.Exists(Path.Combine(Paths.Marketplace,"index.js")),
            spotify ? Demo ? "1.2.93 · demo" : FileVersionInfo.GetVersionInfo(Paths.SpotifyExe).FileVersion ?? "Installed" : "Not installed",
            version, Ini.Get(cfg,"Setting","current_theme") is { Length: >0 } theme ? theme : "Default", state, Paths.Spotify);
    }

    public async Task<Release> Latest(string repo, CancellationToken token)
    {
        if(repo != "cli" && repo != "marketplace") throw new ArgumentException("Unknown release repository.");
        using var response = await http.GetAsync($"https://api.github.com/repos/spicetify/{repo}/releases/latest", token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = json.RootElement;
        return new(root.GetProperty("tag_name").GetString()!, root.GetProperty("assets").EnumerateArray().Select(x => new ReleaseAsset(x.GetProperty("name").GetString()!, x.GetProperty("browser_download_url").GetString()!, x.TryGetProperty("digest",out var digest) ? digest.GetString() ?? "" : "", x.GetProperty("size").GetInt64())).ToList());
    }
    public static bool AllowedDownload(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("download.scdn.co",StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("raw.githubusercontent.com",StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".scdn.co",StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("upgrade.scdn.co",StringComparison.OrdinalIgnoreCase));
    }
    async Task Download(string url, string file, CancellationToken token, string digest = "", long size = 0)
    {
        if(!AllowedDownload(url)) throw new IOException("Download URL is outside the supported HTTPS sources.");
        Step("Downloading " + Path.GetFileName(file));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using(var output = File.Create(file)) await response.Content.CopyToAsync(output, token);
        if(size > 0 && new FileInfo(file).Length != size) throw new IOException("Download size does not match the release.");
        if(!string.IsNullOrEmpty(digest))
        {
            if(!digest.StartsWith("sha256:")) throw new IOException("Unsupported release digest.");
            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream,token));
            if(!hash.Equals(digest[7..],StringComparison.OrdinalIgnoreCase)) throw new IOException("Download checksum verification failed.");
            Write("SHA-256 matches GitHub release metadata.");
        }
    }
    async Task SignedSpotify(string file, CancellationToken token)
    {
        await Ps("$s=Get-AuthenticodeSignature -LiteralPath $env:TEMPEST_VERIFY; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch '(^|, )O=Spotify AB(,|$)'){throw 'Spotify signature verification failed'}; Write-Output 'Spotify digital signature verified.'", token, new Dictionary<string,string>{{"TEMPEST_VERIFY",file}});
    }
    async Task StopSpotify()
    {
        if(Demo) return;
        // Match only this installation, never kill unrelated same-name processes.
        foreach(var process in Process.GetProcessesByName("Spotify"))
        {
            using(process)
            {
                try
                {
                    if(process.MainModule?.FileName is { } path && Path.GetFullPath(path).Equals(Path.GetFullPath(Paths.SpotifyExe),StringComparison.OrdinalIgnoreCase))
                    { process.Kill(true); await process.WaitForExitAsync(); }
                }
                catch(InvalidOperationException) { }
            }
        }
    }
    void RequireSpotify()
    {
        if(!File.Exists(Paths.SpotifyExe)) throw new IOException("Install the desktop edition of Spotify first, then open it and sign in.");
        if(!File.Exists(Path.Combine(Paths.Spotify,"prefs"))) throw new IOException("Open Spotify, sign in and leave it open for at least 60 seconds, then try again.");
        var cfg = File.Exists(Paths.Ini) ? File.ReadAllText(Paths.Ini) : "";
        var configured = Ini.Get(cfg,"Setting","spotify_path");
        if(configured.Length > 0 && !Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured)).TrimEnd('\\').Equals(Path.GetFullPath(Paths.Spotify).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Spicetify points to a different Spotify installation. Use Repair location first so checkpoints cover the correct files.");
    }
    async Task Transaction(string name, Func<CancellationToken,Task> action, CancellationToken token, bool fullSpotify = false)
    {
        await gate.WaitAsync(token);
        Checkpoint? checkpoint = null;
        try
        {
            Step("Closing Spotify"); await StopSpotify();
            Step("Saving recovery checkpoint");
            checkpoint = await Task.Run(() => Recovery.Save(name, fullSpotify ? [Paths.Config, Paths.Cli, Paths.Spotify] : Targets, token), token);
            Write("Checkpoint saved: " + checkpoint.Id);
            await action(token);
            Step("Completed successfully");
        }
        catch(Exception error)
        {
            Write(error is OperationCanceledException ? "Cancellation requested." : "Operation failed: " + error.Message);
            if(checkpoint != null)
            {
                Step("Restoring the previous state");
                try { await StopSpotify(); await Task.Run(()=>Recovery.Restore(checkpoint)); Write("Previous files restored. Checkpoint retained."); }
                catch(Exception rollback) { throw new IOException($"Recovery needs attention: {rollback.Message}. Saved checkpoint: {checkpoint.Id}. Original error: {error.Message}",rollback); }
            }
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task Install(CancellationToken token)
    {
        RequireSpotify();
        if(Demo) { await Simulate("Install Spicetify + Marketplace", token); return; }
        var temp = Path.Combine(Paths.Data,"Downloads",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var release = await Latest("cli",token);
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : Environment.Is64BitOperatingSystem ? "x64" : "x32";
            var asset = release.Assets.Single(a=>a.Name.EndsWith($"windows-{arch}.zip"));
            if(string.IsNullOrEmpty(asset.Digest)) throw new IOException("This CLI release has no SHA-256 metadata. No files changed.");
            await Download(asset.Url, Path.Combine(temp,"cli.zip"),token,asset.Digest,asset.Size);
            SafeFiles.ExtractZip(Path.Combine(temp,"cli.zip"),Path.Combine(temp,"cli"));
            if(!File.Exists(Path.Combine(temp,"cli","spicetify.exe"))) throw new IOException("CLI archive is incomplete.");
            var market = await Latest("marketplace",token);
            var mp = market.Assets.Single(a=>a.Name == "marketplace.zip");
            if(string.IsNullOrEmpty(mp.Digest)) throw new IOException("This Marketplace release has no SHA-256 metadata. No files changed.");
            await Download(mp.Url,Path.Combine(temp,"marketplace.zip"),token,mp.Digest,mp.Size);
            SafeFiles.ExtractZip(Path.Combine(temp,"marketplace.zip"),Path.Combine(temp,"marketplace"));
            var mpSource = Path.Combine(temp,"marketplace","marketplace-dist");
            if(!Directory.Exists(mpSource)) mpSource = Path.Combine(temp,"marketplace");
            if(!File.Exists(Path.Combine(mpSource,"index.js")) || !File.Exists(Path.Combine(mpSource,"manifest.json"))) throw new IOException("Marketplace archive is incomplete.");
            await Download($"https://raw.githubusercontent.com/spicetify/marketplace/{market.Tag}/resources/color.ini",Path.Combine(temp,"color.ini"),token);
            await Transaction("Install " + release.Tag, async ct =>
            {
                Step("Installing verified release files");
                SafeFiles.Copy(Path.Combine(temp,"cli"),Paths.Cli,ct);
                SafeFiles.Delete(Paths.Marketplace); SafeFiles.Copy(mpSource,Paths.Marketplace,ct);
                var theme = Path.Combine(Paths.Config,"Themes","marketplace"); Directory.CreateDirectory(theme);
                File.Copy(Path.Combine(temp,"color.ini"),Path.Combine(theme,"color.ini"),true);
                await Configure(ct);
                await Spx(ct,"config","custom_apps","marketplace","inject_css","1","replace_colors","1");
                // Preserve an existing local theme.
                if(Ini.Get(File.ReadAllText(Paths.Ini),"Setting","current_theme").Length == 0) await Spx(ct,"config","current_theme","marketplace");
                Step("Backing up and applying Spicetify");
                await Spx(ct,"backup","apply","--no-restart");
                if(!File.Exists(Path.Combine(Paths.Marketplace,"index.js"))) throw new IOException("Marketplace verification failed.");
            },token);
        }
        finally { try { SafeFiles.Delete(temp); } catch(IOException) { } }
    }
    Task Configure(CancellationToken token) => Spx(token,"config","spotify_path",Paths.Spotify,"prefs_path",Path.Combine(Paths.Spotify,"prefs"));
    public async Task Repair(string kind, CancellationToken token)
    {
        if(!File.Exists(Paths.CliExe)) throw new IOException("Install Spicetify first.");
        if(kind != "location") RequireSpotify();
        else if(!File.Exists(Paths.SpotifyExe) || !File.Exists(Path.Combine(Paths.Spotify,"prefs"))) throw new IOException("Open desktop Spotify and sign in first.");
        if(Demo) { await Simulate("Repair: " + kind,token); return; }
        await Transaction("Repair: " + kind, async ct =>
        {
            await Configure(ct);
            if(kind == "reset")
            {
                Step("Disabling customisations");
                var ini = File.ReadAllText(Paths.Ini);
                foreach(var key in new[]{"current_theme","color_scheme"}) ini = Ini.Set(ini,"Setting",key,"");
                foreach(var key in new[]{"extensions","custom_apps"}) ini = Ini.Set(ini,"AdditionalOptions",key,"");
                File.WriteAllText(Paths.Ini,ini);
            }
            if(kind == "devtools") { await Spx(ct,"enable-devtools","--no-restart"); return; }
            Step("Applying Spicetify"); await Spx(ct,"backup","apply","--no-restart");
        },token);
    }
    public async Task RestoreVanilla(bool remove, CancellationToken token)
    {
        RequireSpotify();
        if(!File.Exists(Paths.CliExe)) throw new IOException("Spicetify is not installed. No files changed.");
        if(Demo) { await Simulate(remove ? "Remove Spicetify" : "Restore original Spotify",token,remove); return; }
        await Transaction(remove ? "Remove Spicetify" : "Restore original Spotify", async ct =>
        {
            Step("Restoring Spotify's original files");
            await Spx(ct,"restore","--no-restart");
            if(!File.Exists(Path.Combine(Paths.Spotify,"Apps","xpui.spa"))) throw new IOException("Cannot verify vanilla Spotify. Spicetify files were kept.");
            if(remove)
            {
                // Config, themes and backups stay available for a later reinstall.
                Step("Removing the Spicetify executable; keeping your customisations");
                SafeFiles.Delete(Paths.Cli);
            }
        },token);
    }
    public async Task RestoreCheckpoint(Checkpoint checkpoint, CancellationToken token)
    {
        await Transaction("Before recovery", async ct => { Step("Restoring selected checkpoint"); await Task.Run(()=>Recovery.Restore(checkpoint)); }, token, checkpoint.Items.Any(x=>x.Target.Equals(Paths.Spotify,StringComparison.OrdinalIgnoreCase)));
    }
    public async Task InstallSpotify(CancellationToken token)
    {
        if(Demo) { await Simulate("Install Spotify", token); return; }
        var dir = Path.Combine(Paths.Data,"Downloads",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir,"SpotifySetup.exe");
            await Download("https://download.scdn.co/SpotifySetup.exe",file,token); await SignedSpotify(file,token);
            // Installer may spawn children: don't interrupt once launched.
            Step("Running Spotify's verified installer");
            await Run(file,[],CancellationToken.None);
            for(var i=0;i<60 && !File.Exists(Paths.SpotifyExe);i++) await Task.Delay(1000);
            if(!File.Exists(Paths.SpotifyExe)) throw new IOException("Desktop Spotify was not detected. Complete Spotify's installer, then refresh.");
            Write("Open Spotify and sign in before installing Spicetify. Store Spotify has not been removed.");
        }
        finally { try { SafeFiles.Delete(dir); } catch(IOException) { } }
    }
    public async Task<List<SpotifyBuild>> Builds(CancellationToken token)
    {
        if(Demo) return [new("1.2.92.600","https://download.scdn.co/demo",110000000,"Demo build"),new("1.2.91.500","https://download.scdn.co/demo",108000000,"Demo build")];
        using var response = await http.GetAsync("https://raw.githubusercontent.com/LoaderSpot/table/main/table/versions.json",token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var builds = new List<SpotifyBuild>();
        foreach(var property in json.RootElement.EnumerateObject())
            if(Version.TryParse(property.Name,out _) && property.Value.TryGetProperty("win",out var win) && win.TryGetProperty(arch,out var build) && build.TryGetProperty("url",out var url))
                builds.Add(new(property.Name,url.GetString()!,build.TryGetProperty("size",out var size)?size.GetInt64():0,build.TryGetProperty("date",out var date)?date.GetString()??"":""));
        return builds.Where(x => AllowedDownload(x.Url)).OrderByDescending(x=>Version.Parse(x.Version)).Take(80).ToList();
    }
    public async Task Downgrade(SpotifyBuild build, CancellationToken token)
    {
        RequireSpotify();
        if(Demo) { await Simulate("Change Spotify to " + build.Version, token); return; }
        // Third-party catalogue URLs may vary. Only permit known Spotify/GitHub HTTPS hosts.
        if(!AllowedDownload(build.Url)) throw new IOException("This catalogue entry uses an unsupported download host. No files changed.");
        var dir=Path.Combine(Paths.Data,"Downloads",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var setup=Path.Combine(dir,"SpotifySetup.exe");
            await Download(build.Url,setup,token,size:build.Size); await SignedSpotify(setup,token);
            var extracted=Path.Combine(dir,"client");
            await Run(setup,["/extract",extracted],token);
            var exe=Path.Combine(extracted,"Spotify.exe"); await SignedSpotify(exe,token);
            var actual=Regex.Match(FileVersionInfo.GetVersionInfo(exe).FileVersion??"",@"^\d+(\.\d+){3}").Value;
            if(actual != build.Version) throw new IOException("Downloaded Spotify version does not match your selection.");
            await Transaction("Spotify " + build.Version,async ct=>
            {
                Step("Replacing Spotify; preserving login and preferences");
                foreach(var item in Directory.GetFileSystemEntries(Paths.Spotify))
                    if(Path.GetFileName(item) is not ("Users" or "prefs")) SafeFiles.Delete(item);
                foreach(var item in Directory.GetFileSystemEntries(extracted))
                    if(Path.GetFileName(item) is not ("Users" or "prefs")) SafeFiles.Copy(item,Path.Combine(Paths.Spotify,Path.GetFileName(item)),ct);
                if(File.Exists(Paths.CliExe))
                {
                    SafeFiles.Delete(Path.Combine(Paths.Config,"Backup")); SafeFiles.Delete(Path.Combine(Paths.Config,"Extracted"));
                    if(File.Exists(Paths.Ini)) File.WriteAllText(Paths.Ini,Ini.Set(Ini.Set(File.ReadAllText(Paths.Ini),"Backup","version",""),"Backup","with",""));
                    await Configure(ct); await Spx(ct,"backup","apply","--no-restart");
                }
            },token,true);
        }
        finally { try { SafeFiles.Delete(dir); } catch(IOException) { } }
    }
    public async Task FixPath(CancellationToken token)
    {
        if(Demo) { Step("Demo: PATH would be repaired. No environment settings changed."); return; }
        if(!File.Exists(Paths.CliExe)) throw new IOException("Install Spicetify first.");
        await Ps("$p=[Environment]::GetEnvironmentVariable('Path','User'); $parts=@($p -split ';' | Where-Object {$_}); if(-not ($parts | Where-Object {[Environment]::ExpandEnvironmentVariables($_).TrimEnd('\\') -ieq $env:TEMPEST_CLI})){[Environment]::SetEnvironmentVariable('Path',(($parts + $env:TEMPEST_CLI) -join ';'),'User')}; Write-Output 'PATH repaired. Open a new terminal to use spicetify.'",token,new Dictionary<string,string>{{"TEMPEST_CLI",Paths.Cli}});
    }
    public async Task CreateShortcut(CancellationToken token)
    {
        if(Demo) { Step("Demo: an auto-apply shortcut would be created."); return; }
        if(!File.Exists(Paths.CliExe)) throw new IOException("Install Spicetify first.");
        await Ps("$p=Join-Path ([Environment]::GetFolderPath('Desktop')) 'Spotify - Tempest.lnk'; if(Test-Path -LiteralPath $p){throw 'Spotify - Tempest shortcut already exists. It was not overwritten.'}; $s=(New-Object -ComObject WScript.Shell).CreateShortcut($p); $s.TargetPath=$env:TEMPEST_CLI; $s.Arguments='auto'; $s.WorkingDirectory=Split-Path $env:TEMPEST_CLI; $s.IconLocation=$env:TEMPEST_SPOTIFY+',0'; $s.Save(); Write-Output 'Created Spotify - Tempest on your desktop.'",token,new Dictionary<string,string>{{"TEMPEST_CLI",Paths.CliExe},{"TEMPEST_SPOTIFY",Paths.SpotifyExe}});
    }
    public async Task Updates(bool block, CancellationToken token)
    {
        if(Demo) { Step(block?"Demo: updates would be blocked.":"Demo: updates would be allowed."); return; }
        if(!File.Exists(Paths.CliExe)) throw new IOException("Install the latest Spicetify first.");
        RequireSpotify();
        await Transaction(block?"Block Spotify updates":"Allow Spotify updates",ct=>Spx(ct,"spotify-updates",block?"block":"unblock"),token);
    }
    public void SeedDemo()
    {
        if(!Demo) throw new InvalidOperationException();
        Directory.CreateDirectory(Paths.Cli); Directory.CreateDirectory(Paths.Config); Directory.CreateDirectory(Path.Combine(Paths.Spotify,"Apps","xpui")); Directory.CreateDirectory(Paths.Marketplace);
        File.WriteAllText(Paths.CliExe,"demo"); File.WriteAllText(Paths.SpotifyExe,"demo"); File.WriteAllText(Path.Combine(Paths.Spotify,"prefs"),"demo");
        File.WriteAllText(Paths.Ini,"[Setting]\ncurrent_theme = marketplace\n[Backup]\nversion = 1.2.93.667\n");
        File.WriteAllText(Path.Combine(Paths.Marketplace,"index.js"),"demo"); File.WriteAllText(Path.Combine(Paths.Marketplace,"manifest.json"),"{}");
    }
    async Task Simulate(string action, CancellationToken token, bool remove = false)
    {
        await Transaction(action,async ct=>
        {
            foreach(var step in new[]{"Checking prerequisites","Preparing files","Applying changes","Verifying installation"}) { Step("Demo: " + step); await Task.Delay(600,ct); }
            File.WriteAllText(Paths.Ini,Ini.Set(File.ReadAllText(Paths.Ini),"Setting","current_theme","marketplace"));
            if(FailNextDemo) { FailNextDemo=false; throw new IOException("Simulated apply failure. This tests automatic rollback."); }
            if(remove) SafeFiles.Delete(Paths.Cli);
        },token);
    }
}
