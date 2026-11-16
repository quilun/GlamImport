using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GlamImport.Parsing;

namespace GlamImport;

/// <summary>One remembered glam: what was pasted, so it can be loaded again without finding the page.</summary>
public sealed record RecentImport(string Title, string? Creator, DateTime UsedAt, string Text);

/// <summary>
/// The last few glams you applied or saved, newest first. Kept in recent.json next to the plugin's settings,
/// so the settings file stays small.
/// </summary>
public sealed class RecentImports
{
    public const int MaxEntries = 10;

    private readonly string file;
    private List<RecentImport> entries = new();

    public RecentImports(string configDirectory)
    {
        this.file = Path.Combine(configDirectory, "recent.json");
        try
        {
            if (File.Exists(this.file))
                this.entries = JsonSerializer.Deserialize<List<RecentImport>>(File.ReadAllText(this.file)) ?? new();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't read recent imports, starting fresh");
        }
    }

    public IReadOnlyList<RecentImport> Entries => this.entries;

    public void Add(ParsedGlam glam, string pastedText)
    {
        var text = Trim(pastedText);
        var title = glam.Title ?? (glam.Pieces.FirstOrDefault()?.ItemName is { } first ? $"Partial: {first}" : "Partial glam");
        this.entries.RemoveAll(e => e.Text == text || (glam.Title != null && e.Title == glam.Title && e.Creator == glam.Creator));
        this.entries.Insert(0, new RecentImport(title, glam.Creator, DateTime.Now, text));
        if (this.entries.Count > MaxEntries)
            this.entries.RemoveRange(MaxEntries, this.entries.Count - MaxEntries);
        this.Save();
    }

    public void Clear()
    {
        this.entries.Clear();
        this.Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.file)!);
            File.WriteAllText(this.file, JsonSerializer.Serialize(this.entries));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't save recent imports");
        }
    }

    /// <summary>Drops the page's tail ("More Glamours by...", ads, footer), which the parser never reads.</summary>
    private static string Trim(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var cut = lines.FindIndex(l =>
        {
            var t = GlamParser.Clean(l);
            return t.StartsWith("More Glamours by", StringComparison.OrdinalIgnoreCase) || t.Equals("Advertisement", StringComparison.OrdinalIgnoreCase);
        });
        return string.Join("\n", cut >= 0 ? lines.Take(cut) : lines).Trim();
    }
}
