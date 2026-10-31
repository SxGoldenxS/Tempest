using System.IO.Compression;
using System.Text.Json;

namespace Tempest.Core;

public static class SafeFiles
{
    public static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"Linked folders are not supported: {p}");
    }
    public static void Copy(string source, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        NoLinks(source); NoLinks(destination);
        if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); return; }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) Copy(file, Path.Combine(destination, Path.GetFileName(file)), token);
        foreach (var dir in Directory.EnumerateDirectories(source)) Copy(dir, Path.Combine(destination, Path.GetFileName(dir)), token);
    }
    public static void Delete(string path)
    {
        NoLinks(path);
        if (Directory.Exists(path))
        {
            foreach(var child in Directory.EnumerateFileSystemEntries(path)) Delete(child);
            Directory.Delete(path);
        }
        else if (File.Exists(path)) File.Delete(path);
    }
    public static void ExtractZip(string file, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        NoLinks(destination);
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(file);
        long total = 0;
        foreach(var entry in zip.Entries)
        {
            total += entry.Length;
            if (total > 2L * 1024 * 1024 * 1024) throw new IOException("Archive exceeds the 2 GB unpacking limit.");
            if (entry.FullName.Contains(':')) throw new IOException("Archive contains an alternate data stream or drive path.");
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('\\', '/')));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe archive path.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Archive contains symbolic links.");
            NoLinks(target);
            if (entry.Name.Length == 0) Directory.CreateDirectory(target);
            else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, true); }
        }
    }
}

public record SavedItem(string Target, string Stored, bool Existed, bool Directory);
public record Checkpoint(string Id, string Action, DateTime Created, List<SavedItem> Items)
{
    public override string ToString() => $"{Created.ToLocalTime():dd MMM, HH:mm}  ·  {Action}";
}

public sealed class RecoveryStore(string root, IEnumerable<string> allowedTargets)
{
    readonly HashSet<string> allowed = allowedTargets.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public string Root => root;
    void ValidateTarget(string path)
    {
        if (!allowed.Contains(Path.GetFullPath(path))) throw new IOException("Recovery target is outside the managed installation.");
        SafeFiles.NoLinks(path);
    }
    string Folder(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{17}-[a-f0-9]{8}$")) throw new IOException("Invalid checkpoint identifier.");
        return Path.Combine(root, id);
    }
    public Checkpoint Save(string action, IEnumerable<string> targets, CancellationToken token)
    {
        var id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var dir = Folder(id);
        SafeFiles.NoLinks(root);
        Directory.CreateDirectory(dir);
        var items = new List<SavedItem>();
        foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ValidateTarget(target);
            var exists = File.Exists(target) || Directory.Exists(target);
            var saved = items.Count.ToString();
            if (exists) SafeFiles.Copy(target, Path.Combine(dir, saved), token);
            items.Add(new(target, saved, exists, Directory.Exists(target)));
        }
        var checkpoint = new Checkpoint(id, action, DateTime.UtcNow, items);
        // Manifest is written last: incomplete snapshots never appear as restorable.
        File.WriteAllText(Path.Combine(dir, "checkpoint.json"), JsonSerializer.Serialize(checkpoint));
        return checkpoint;
    }
    public List<Checkpoint> List()
    {
        if (!Directory.Exists(root)) return [];
        var list = new List<Checkpoint>();
        foreach(var dir in Directory.GetDirectories(root))
        {
            try
            {
                var checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(Path.Combine(dir, "checkpoint.json")));
                if(checkpoint != null && Path.GetFullPath(Folder(checkpoint.Id)).Equals(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
                {
                    Validate(checkpoint);
                    list.Add(checkpoint);
                }
            } catch (Exception e) when (e is IOException or JsonException or ArgumentException) { }
        }
        return list.OrderByDescending(x => x.Created).ToList();
    }
    void Validate(Checkpoint checkpoint)
    {
        if (checkpoint.Items == null || checkpoint.Items.Count == 0) throw new IOException("Checkpoint has no recovery entries.");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < checkpoint.Items.Count; i++)
        {
            var item = checkpoint.Items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.Target)) throw new IOException("Invalid recovery entry.");
            ValidateTarget(item.Target);
            var target = Path.GetFullPath(item.Target);
            if (!targets.Add(target)) throw new IOException("Duplicate recovery target.");
            if (item.Stored != i.ToString()) throw new IOException("Invalid recovery entry.");
            var saved = Path.Combine(Folder(checkpoint.Id), item.Stored);
            SafeFiles.NoLinks(saved);
            if (item.Existed && !(item.Directory ? Directory.Exists(saved) : File.Exists(saved))) throw new IOException("Checkpoint is incomplete.");
        }
        foreach (var target in targets)
            if (targets.Any(other => !other.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                target.StartsWith(Path.TrimEndingDirectorySeparator(other) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Overlapping recovery targets.");
    }
    public void Restore(Checkpoint checkpoint)
    {
        var dir = Folder(checkpoint.Id);
        SafeFiles.NoLinks(dir);
        Validate(checkpoint);
        foreach(var item in checkpoint.Items)
        {
            // Prepare a sibling first. If replacement fails, keep the previous item.
            var staged = item.Target + ".tempest-" + Guid.NewGuid().ToString("N");
            var previous = staged + ".previous";
            if(item.Existed) SafeFiles.Copy(Path.Combine(dir, item.Stored), staged, CancellationToken.None);
            bool oldDir = Directory.Exists(item.Target), oldFile = File.Exists(item.Target);
            try
            {
                if(oldDir) Directory.Move(item.Target, previous); else if(oldFile) File.Move(item.Target, previous);
                if(item.Existed) { if(item.Directory) Directory.Move(staged, item.Target); else File.Move(staged, item.Target); }
            }
            catch
            {
                if(Directory.Exists(previous)) Directory.Move(previous, item.Target);
                else if(File.Exists(previous)) File.Move(previous, item.Target);
                throw;
            }
            // Keep displaced state alongside the target if cleanup is blocked.
            try { SafeFiles.Delete(previous); } catch(IOException) { }
        }
    }
}
