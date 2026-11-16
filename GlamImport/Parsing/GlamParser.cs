using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GlamImport.Parsing;

/// <summary>Which kind of slot an Eorzea Collection label stands for.</summary>
public enum SlotKind
{
    Unknown,
    MainHand,
    OffHand,
    Head,
    Body,
    Hands,
    Legs,
    Feet,
    Ears,
    Neck,
    Wrists,
    RightRing,
    LeftRing,
    Facewear,
    Fashion,
}

/// <summary>One gear piece as written on the page: the label, the item name and the dye names (null = undyed).</summary>
public sealed record ParsedPiece(string Label, SlotKind Slot, string ItemName, IReadOnlyList<string?> Dyes);

/// <summary>Everything read from a pasted Eorzea Collection page.</summary>
/// <param name="IsCompleteGlam">True when the paste includes the "Equipment" heading, i.e. the whole gear section was copied.
/// Only then does a missing slot mean "the glam has nothing there" (used to take off facewear).</param>
/// <param name="IntendedFor">The page's "Intended for" value ("Monk", "All Classes"), if the whole page was copied.</param>
/// <param name="Fits">The page's "Fits" value ("Female", "Male", "Any Gender"), if the whole page was copied.</param>
/// <param name="Model">The page's "Model" value ("Au Ra Female"): the character the glam was made on.</param>
/// <param name="Tags">The glam's Eorzea Collection tags ("Cute", "Academic Clothes"...), one per line between "Tags:" and "Submitted on:".</param>
/// <param name="Description">The creator's description from the page, if there is one.</param>
public sealed record ParsedGlam(string? Title, string? Creator, IReadOnlyList<ParsedPiece> Pieces, bool IsCompleteGlam = false,
    string? IntendedFor = null, string? Fits = null, string? Model = null, IReadOnlyList<string>? Tags = null, string? Description = null);

/// <summary>
/// Reads text copied from an Eorzea Collection glamour page. Works with the whole page or just the gear section.
/// Rules: a slot label is an all-caps line (HEAD, BODY, RING, FACE...). The first line after it is the item name.
/// Lines starting with ⬤ or ◯ after that are the dye channels, in order. Anything else is ignored.
/// </summary>
public static class GlamParser
{
    private static readonly Regex MarkdownLink = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex CapsLabel = new(@"^[A-Z][A-Z '\-]{1,24}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, SlotKind> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WEAPON"] = SlotKind.MainHand,
        ["MAIN HAND"] = SlotKind.MainHand,
        ["MAINHAND"] = SlotKind.MainHand,
        ["OFF HAND"] = SlotKind.OffHand,
        ["OFFHAND"] = SlotKind.OffHand,
        ["SHIELD"] = SlotKind.OffHand,
        ["HEAD"] = SlotKind.Head,
        ["BODY"] = SlotKind.Body,
        ["HANDS"] = SlotKind.Hands,
        ["LEGS"] = SlotKind.Legs,
        ["FEET"] = SlotKind.Feet,
        ["EARRINGS"] = SlotKind.Ears,
        ["NECKLACE"] = SlotKind.Neck,
        ["BRACELETS"] = SlotKind.Wrists,
        ["RING"] = SlotKind.RightRing, // the second RING becomes the left ring
        ["FACE"] = SlotKind.Facewear,
        ["FASHION"] = SlotKind.Fashion,
    };

    /// <summary>Sections that come after the gear. Reading stops at the first one, so album titles, related glams
    /// and their like counts ("WHM / 35") aren't mistaken for gear.</summary>
    private static readonly HashSet<string> EndMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Featured in Albums", "Related to", "Creator", "About this glamour", "More Glamours",
    };

    /// <summary>Lines that look like labels but are page furniture, not slots.</summary>
    private static readonly HashSet<string> NotLabels = new(StringComparer.OrdinalIgnoreCase) { "TOP" };

    public static ParsedGlam Parse(string text)
    {
        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(Clean)
            .ToList();

        var equipStart = lines.FindIndex(l => l.Equals("Equipment", StringComparison.OrdinalIgnoreCase));
        var bodyStart = equipStart >= 0 ? equipStart + 1 : 0;
        var bodyEnd = lines.FindIndex(bodyStart, l => EndMarkers.Contains(l));
        if (bodyEnd < 0)
            bodyEnd = lines.Count;

        var title = FindTitle(lines, equipStart >= 0 ? equipStart : lines.Count);
        var creator = FindCreator(lines);
        var pieces = ParsePieces(lines, bodyStart, bodyEnd);
        return new ParsedGlam(title, creator, pieces, equipStart >= 0, ValueAfter(lines, "Intended for"), ValueAfter(lines, "Fits"),
            ValueAfter(lines, "Model"), FindTags(lines, equipStart >= 0 ? equipStart : lines.Count),
            FindDescription(lines, equipStart));
    }

    /// <summary>Strips whitespace, markdown links ("[Name](url)" → "Name") and curly apostrophes.</summary>
    public static string Clean(string line)
    {
        var s = MarkdownLink.Replace(line, "$1").Trim();
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace(' ', ' ');
        return s;
    }

    private static bool IsDyeLine(string l)
        => l.Length > 0 && "⬤◯●○•".Contains(l[0]);

    private static bool IsSectionHeader(string l)
        => l.StartsWith('—') || l.StartsWith('-') && l.EndsWith('-');

    private static bool IsLabel(string l, out SlotKind kind)
    {
        if (Labels.TryGetValue(l, out kind))
            return true;
        kind = SlotKind.Unknown;
        return CapsLabel.IsMatch(l) && !NotLabels.Contains(l);
    }

    private static List<ParsedPiece> ParsePieces(List<string> lines, int start, int end)
    {
        var result = new List<ParsedPiece>();
        var ringCount = 0;
        var i = start;
        while (i < end)
        {
            var line = lines[i];
            if (!IsLabel(line, out var kind))
            {
                i++;
                continue;
            }

            // The item name is the next non-empty line that isn't another label, a header or a dye.
            var j = i + 1;
            while (j < end && lines[j].Length == 0)
                j++;
            if (j >= end || IsLabel(lines[j], out _) || IsSectionHeader(lines[j]) || IsDyeLine(lines[j]) ||
                kind == SlotKind.Unknown && IsNumber(lines[j]))
            {
                i = j;
                continue;
            }

            var name = lines[j];
            var dyes = new List<string?>();
            j++;
            while (j < end && !IsLabel(lines[j], out _) && !IsSectionHeader(lines[j]))
            {
                var l = lines[j];
                if (IsDyeLine(l))
                {
                    var dye = l[1..].Trim();
                    dyes.Add(dye.Length == 0 || dye.Equals("Undyed", StringComparison.OrdinalIgnoreCase) ? null : dye);
                }

                j++;
            }

            if (kind == SlotKind.RightRing && ringCount++ > 0)
                kind = SlotKind.LeftRing;

            result.Add(new ParsedPiece(line, kind, name, dyes));
            i = j;
        }

        return result;
    }

    /// <summary>The title sits above the like count, which sits above "Tags:" (or "Submitted on:" if there are no tags).</summary>
    private static string? FindTitle(List<string> lines, int searchEnd)
    {
        var anchor = -1;
        for (var k = 0; k < searchEnd; k++)
        {
            if (lines[k].StartsWith("Tags:", StringComparison.OrdinalIgnoreCase) ||
                lines[k].StartsWith("Submitted on", StringComparison.OrdinalIgnoreCase))
            {
                anchor = k;
                break;
            }
        }

        if (anchor < 0)
            return null;

        for (var k = anchor - 1; k >= 0; k--)
        {
            var l = lines[k];
            // Skip the like count and the screenshot gallery's alt texts ("First image", "Third image"...),
            // which only show up when copying as plain text (e.g. into the game).
            if (l.Length == 0 || IsNumber(l) || ImageAltText.IsMatch(l))
                continue;
            return l;
        }

        return null;
    }

    private static readonly Regex ImageAltText = new(@"^(\w+ )?(image|screenshot|photo)( \d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool IsNumber(string l)
        => l.Length > 0 && l.All(c => char.IsDigit(c) || c is ',' or '.' or 'k' or 'K') && char.IsDigit(l[0]);

    private static readonly Regex FromServer = new(@"\s+from\b.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// "Creator Name from «World»" / "Creator Name from ⧫ World" → "Creator Name".
    /// Character names are always two words, so everything from " from" onward is the server.
    /// </summary>
    public static string CleanCreator(string l)
    {
        foreach (var marker in new[] { '⧫', '«' })
        {
            var at = l.IndexOf(marker);
            if (at >= 0)
                l = l[..at];
        }

        l = l.Trim();
        var words = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 2)
            l = FromServer.Replace(l, string.Empty).Trim();
        else if (words.Length == 2 && words[1].Equals("from", StringComparison.OrdinalIgnoreCase))
            l = words[0];
        return l;
    }

    /// <summary>The first non-empty line after a heading like "Intended for" or "Fits" in the "About this glamour" block.</summary>
    private static string? ValueAfter(List<string> lines, string heading)
    {
        var about = lines.FindIndex(l => l.Equals("About this glamour", StringComparison.OrdinalIgnoreCase));
        if (about < 0)
            return null;
        var k = lines.FindIndex(about, l => l.Equals(heading, StringComparison.OrdinalIgnoreCase));
        if (k < 0)
            return null;
        for (k++; k < lines.Count; k++)
        {
            if (lines[k].Length > 0)
                return lines[k];
        }

        return null;
    }

    /// <summary>
    /// Tags come one per line after "Tags:" until "Submitted on:". (If a copy ever puts them on the "Tags:" line itself,
    /// multi-word tags like "Final Fantasy" can't be split reliably, so that line is ignored.)
    /// </summary>
    private static List<string> FindTags(List<string> lines, int searchEnd)
    {
        var tags = new List<string>();
        var start = lines.FindIndex(0, Math.Min(searchEnd, lines.Count), l => l.StartsWith("Tags:", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return tags;

        // All on one line ("Tags: Cute Final Fantasy Cosplay"): split by matching Eorzea Collection's known tags, longest first.
        var sameLine = lines[start]["Tags:".Length..].Trim();
        if (sameLine.Length > 0)
            return SplitKnownTags(sameLine);
        for (var k = start + 1; k < searchEnd; k++)
        {
            var l = lines[k];
            if (l.StartsWith("Submitted on", StringComparison.OrdinalIgnoreCase) || l.Equals("Equipment", StringComparison.OrdinalIgnoreCase))
                break;
            if (l.Length > 0 && !tags.Contains(l, StringComparer.OrdinalIgnoreCase))
                tags.Add(l);
        }

        return tags;
    }

    /// <summary>The creator's text between the "Description" heading and "Equipment" (without the page's "Report" link).</summary>
    private static string? FindDescription(List<string> lines, int equipStart)
    {
        if (equipStart < 0)
            return null;
        var start = lines.FindLastIndex(equipStart, l => l.Equals("Description", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return null;

        var text = lines.Skip(start + 1).Take(equipStart - start - 1)
            .Where(l => !l.Equals("Report", StringComparison.OrdinalIgnoreCase))
            .ToList();
        while (text.Count > 0 && text[0].Length == 0)
            text.RemoveAt(0);
        while (text.Count > 0 && text[^1].Length == 0)
            text.RemoveAt(text.Count - 1);
        return text.Count > 0 ? string.Join("\n", text) : null;
    }

    /// <summary>Every tag Eorzea Collection offers (classification, theme, color, style), from the site's tag filter.</summary>
    private static readonly string[] KnownTags =
    {
        "Athletic",
        "Cool",
        "Cute",
        "Divine",
        "Elegant",
        "Fashionable",
        "Glamorous",
        "Heroic",
        "Sexy",
        "Strong",
        "Sweet",
        "Villainous",
        "Youthful",
        "Academic Clothes",
        "Adventurer",
        "Autumn Outfit",
        "Battle Gear",
        "Crafter",
        "Cyber Outfit",
        "Desert Outfit",
        "Eternal Bonding",
        "Final Fantasy",
        "Forest Outfit",
        "Gatherer",
        "House Wear",
        "Idle Clothes",
        "Military Gear",
        "Nature Outfit",
        "Ninja",
        "Party Outfit",
        "Pirate",
        "Royalty",
        "Seasonal Event",
        "Special Occasions",
        "Sportswear",
        "Spring Outfit",
        "Summer Outfit",
        "Swimwear",
        "Wild West Outfit",
        "Winter Outfit",
        "Beige",
        "Black",
        "Blue",
        "Brown",
        "Gold",
        "Green",
        "Grey",
        "Metallic",
        "Monochromatic",
        "Neon",
        "Orange",
        "Pastel",
        "Pink",
        "Purple",
        "Red",
        "Silver",
        "Turquoise",
        "Vibrant",
        "White",
        "Yellow",
        "Ancient",
        "Bohemian",
        "Casual",
        "Chic",
        "Cosplay",
        "Dark",
        "Earth",
        "Eastern",
        "Fantasy",
        "Fire",
        "Formal",
        "Futuristic",
        "Goth",
        "Grunge",
        "Historical",
        "Holy",
        "Ice",
        "Lightning",
        "Magical",
        "Medieval",
        "Modern",
        "Retro",
        "Spiritual",
        "Steampunk",
        "Tribal",
        "Vacation",
        "Vintage",
        "Water",
        "Wind",
    };

    private static List<string> SplitKnownTags(string text)
    {
        var found = new List<string>();
        var rest = text;
        while (rest.Length > 0)
        {
            var match = KnownTags.Where(t => rest.StartsWith(t, StringComparison.OrdinalIgnoreCase) &&
                                             (rest.Length == t.Length || rest[t.Length] == ' '))
                .OrderByDescending(t => t.Length).FirstOrDefault();
            if (match == null)
            {
                // Unknown word (a tag added after this list was made): keep it as its own tag.
                var space = rest.IndexOf(' ');
                match = space < 0 ? rest : rest[..space];
                found.Add(match);
            }
            else
            {
                found.Add(match);
            }

            rest = rest[match.Length..].TrimStart();
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindCreator(List<string> lines)
    {
        var k = lines.FindIndex(l => l.Equals("Creator", StringComparison.OrdinalIgnoreCase));
        if (k < 0)
            return null;
        for (k++; k < lines.Count; k++)
        {
            var l = CleanCreator(lines[k]);
            if (l.Length > 0)
                return l;
        }

        return null;
    }
}
