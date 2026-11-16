using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GlamImport.Parsing;

namespace GlamImport;

/// <summary>Fills in preset name and description templates like "{name} ({creator})".</summary>
public static class PresetName
{
    /// <summary>Tag, what it becomes. Shown in the settings tooltips.</summary>
    public static readonly (string Tag, string Meaning)[] Tags =
    {
        ("{name}", "Glam name"),
        ("{creator}", "Creator name"),
        ("{job}", "Class/job the glam is meant for (MNK, SCH...), or All"),
        ("{gender}", "Gender the glam fits: M, F, or Any"),
        ("{race}", "Race the glam was made on (Au Ra, Viera...)"),
        ("{model}", "Race and gender the glam was made on (Au Ra Female)"),
        ("{character}", "Your character's name"),
        ("{date}", "Today's date (2026-11-16)"),
        ("{DD} {MM} {YY} {YYYY}", "Day, month, year parts for your own date format"),
        ("{time}", "Current time (14:05)"),
        ("{HH} {mm}", "Hour and minute parts"),
    };

    private static readonly Regex TagPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);
    private static readonly Regex EmptyBrackets = new(@"\(\s*\)|\[\s*\]|\{\s*\}|<\s*>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t]{2,}", RegexOptions.Compiled);

    /// <summary>Preset name. "/" in the template makes Glamourer folders; slashes inside values become dashes.</summary>
    /// <param name="jobAbbreviation">Turns "Monk" into "MNK"; returns null if the text isn't a single job.</param>
    /// <param name="character">Your character's name, for {character}.</param>
    public static string Format(string template, ParsedGlam glam, DateTime now, Func<string, string?> jobAbbreviation, string? character = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = Configuration.DefaultPresetName;

        var values = Values(glam, now, jobAbbreviation, character);
        values["name"] = glam.Title ?? $"GlamImport {now:yyyy-MM-dd}"; // partial pastes have no title
        values["job"] = values["job"].Length > 0 ? values["job"] : "All";

        // Unknown tags are left as typed so typos are visible.
        var name = TagPattern.Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v.Replace('/', '-') : m.Value);

        // Tidy each folder level and the name separately: "Glam ()" → "Glam", "  - Glam" → "Glam", no empty folders.
        var parts = name.Split('/').Select(Tidy).Where(p => p.Length > 0).ToList();
        var endsWithFolder = name.TrimEnd().EndsWith('/') || parts.Count == 0 || Tidy(name.Split('/')[^1]).Length == 0;
        if (endsWithFolder)
            parts.Add($"GlamImport {now:yyyy-MM-dd}"); // Glamourer would call a name ending in "/" "<Unnamed>"
        return string.Join('/', parts);
    }

    /// <summary>
    /// Description info block. A line is left out if any of its tags has no value (e.g. no creator in a partial paste),
    /// so "{name} by {creator}" never turns into " by ".
    /// </summary>
    public static string FormatText(string template, ParsedGlam glam, DateTime now, Func<string, string?> jobAbbreviation, string? character = null)
    {
        var values = Values(glam, now, jobAbbreviation, character);
        var lines = new List<string>();
        foreach (var line in (template ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var missing = false;
            var filled = TagPattern.Replace(line, m =>
            {
                if (!values.TryGetValue(m.Groups[1].Value, out var v))
                    return m.Value;
                if (v.Length == 0)
                    missing = true;
                return v;
            });
            if (!missing)
                lines.Add(Spaces.Replace(filled, " ").TrimEnd());
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The full description: the creator's text, a separator, then the info block. Either part can be missing;
    /// the separator only appears between two parts.
    /// </summary>
    public static string Description(DescriptionMode mode, string infoTemplate, ParsedGlam glam, DateTime now,
        Func<string, string?> jobAbbreviation, string? character = null)
    {
        if (mode == DescriptionMode.Off)
            return string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(glam.Description))
            parts.Add(glam.Description.Trim());
        if (mode == DescriptionMode.CreatorTextAndInfo)
        {
            var info = FormatText(infoTemplate, glam, now, jobAbbreviation, character);
            if (info.Length > 0)
                parts.Add(info);
        }

        return string.Join("\n\n―――――\n", parts);
    }

    /// <summary>All tag values. Empty means "not on the page".</summary>
    private static Dictionary<string, string> Values(ParsedGlam glam, DateTime now, Func<string, string?> jobAbbreviation, string? character)
        => new(StringComparer.Ordinal)
        {
            ["name"] = glam.Title ?? string.Empty,
            ["creator"] = glam.Creator ?? string.Empty,
            ["job"] = Job(glam.IntendedFor, jobAbbreviation),
            ["gender"] = Gender(glam.Fits),
            ["race"] = Race(glam.Model),
            ["model"] = glam.Model?.Trim() ?? string.Empty,
            ["character"] = character ?? string.Empty,
            ["date"] = now.ToString("yyyy-MM-dd"),
            ["time"] = now.ToString("HH:mm"),
            ["DD"] = now.ToString("dd"),
            ["MM"] = now.ToString("MM"),
            ["YY"] = now.ToString("yy"),
            ["YYYY"] = now.ToString("yyyy"),
            ["HH"] = now.ToString("HH"),
            ["mm"] = now.ToString("mm"),
        };

    private static string Tidy(string s)
    {
        s = EmptyBrackets.Replace(s, string.Empty);
        return Spaces.Replace(s, " ").Trim().Trim('-', '–', '|', '_', ',', ' ');
    }

    /// <summary>"Monk", "MNK", or "SGE Sage" → the abbreviation; "All Classes", "Healers" → All; not on the page → empty.</summary>
    private static string Job(string? intendedFor, Func<string, string?> jobAbbreviation)
    {
        if (string.IsNullOrWhiteSpace(intendedFor))
            return string.Empty;
        if (jobAbbreviation(intendedFor) is { } whole)
            return whole;
        foreach (var word in intendedFor.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (jobAbbreviation(word) is { } abbr)
                return abbr;
        }

        return "All";
    }

    /// <summary>"Au Ra Female" → "Au Ra".</summary>
    private static string Race(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return string.Empty;
        var words = model.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[^1] is "Male" or "Female")
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    /// <summary>"Female" → F, "Male" → M, "Any Gender" / "Any Any Gender" → Any (plain-text copies repeat words).</summary>
    private static string Gender(string? fits)
    {
        var words = (fits ?? string.Empty).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return string.Empty;
        if (words.Contains("any"))
            return "Any";
        if (words.Contains("female"))
            return "F";
        return words.Contains("male") ? "M" : "Any";
    }
}
