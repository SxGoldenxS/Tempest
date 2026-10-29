using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Tempest.Core;

public record Installation(string Spotify, string Cli, string Config, string Data)
{
    public string SpotifyExe => Path.Combine(Spotify, "Spotify.exe");
    public string CliExe => Path.Combine(Cli, "spicetify.exe");
    public string Ini => Path.Combine(Config, "config-xpui.ini");
    public string Marketplace => Path.Combine(Config, "CustomApps", "marketplace");
    public static Installation Current() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "spicetify"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "spicetify"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tempest"));
}

public record Status(bool Spotify, bool Cli, bool Marketplace, string SpotifyVersion, string CliVersion, string Theme, string Applied, string Location);
public record ReleaseAsset(string Name, string Url, string Digest, long Size);
public record Release(string Tag, List<ReleaseAsset> Assets);
public record SpotifyBuild(string Version, string Url, long Size, string Date)
{
    public override string ToString() => $"{Version}    ·    {Date}    ·    {Size / 1048576} MB";
}
public record CommandResult(int ExitCode, string Output);
public interface ICommandRunner
{
    Task<CommandResult> Run(string file, IEnumerable<string> args, CancellationToken token, Action<string>? log = null, IReadOnlyDictionary<string,string>? env = null);
}

public sealed class CommandRunner : ICommandRunner
{
    public async Task<CommandResult> Run(string file, IEnumerable<string> args, CancellationToken token, Action<string>? log = null, IReadOnlyDictionary<string,string>? env = null)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (env != null) foreach (var entry in env) start.Environment[entry.Key] = entry.Value;
        using var process = new Process { StartInfo = start };
        process.Start();
        process.StandardInput.Close(); // Never leave an invisible interactive prompt waiting.
        var output = new System.Text.StringBuilder();
        async Task Read(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                line = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                lock(output) output.AppendLine(line);
                log?.Invoke(line);
            }
        }
        var reads = Task.WhenAll(Read(process.StandardOutput), Read(process.StandardError));
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await reads;
            throw;
        }
        await reads;
        return new(process.ExitCode, output.ToString());
    }
}

public static class Ini
{
    public static string Get(string text, string section, string key)
    {
        var current = "";
        foreach (var line in text.Split('\n'))
        {
            var clean = line.Trim();
            if (clean.StartsWith('[') && clean.EndsWith(']')) current = clean[1..^1];
            else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && clean.Contains('='))
            {
                var split = clean.IndexOf('=');
                if (clean[..split].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return clean[(split+1)..].Trim();
            }
        }
        return "";
    }
    public static string Set(string text, string section, string key, string value)
    {
        var lines = text.Replace("\r", "").Split('\n').ToList();
        var start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) { lines.Add($"[{section}]"); lines.Add($"{key} = {value}"); }
        else
        {
            var end = lines.FindIndex(start+1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            var index = lines.FindIndex(start+1, end-start-1, l => l.Contains('=') && l[..l.IndexOf('=')].Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) lines.Insert(end, $"{key} = {value}"); else lines[index] = $"{key} = {value}";
        }
        return string.Join(Environment.NewLine, lines);
    }
}
