using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game;
using GlamImport.Parsing;
using Lumina.Excel.Sheets;

namespace GlamImport.Data;

/// <summary>Looks up items, dyes and facewear by their English names, the way Eorzea Collection writes them.</summary>
public sealed class GameData
{
    private readonly Dictionary<string, List<uint>> itemsByName = new();
    private readonly Dictionary<string, byte> stainsByName = new();
    private readonly Dictionary<string, uint> glassesByName = new();
    private readonly Dictionary<byte, Vector4> stainColors = new();
    private readonly Dictionary<uint, string> glassesNames = new();
    private readonly Dictionary<uint, uint> glassesIcons = new();
    private readonly Dictionary<string, string> jobAbbreviations = new();
    private readonly HashSet<string> baseClasses = new();
    private readonly Dictionary<string, PropertyInfo?> jobColumns = new();

    public bool Ready { get; private set; }

    /// <summary>Builds the name tables. Takes a moment (tens of thousands of items), so it runs once in the background.</summary>
    public void Load()
    {
        var items = Plugin.DataManager.GetExcelSheet<Item>(ClientLanguage.English);
        foreach (var item in items)
        {
            if (item.EquipSlotCategory.RowId == 0)
                continue;
            var name = Normalize(item.Name.ExtractText());
            if (name.Length == 0)
                continue;
            if (!this.itemsByName.TryGetValue(name, out var list))
                this.itemsByName[name] = list = new List<uint>(1);
            list.Add(item.RowId);
        }

        foreach (var stain in Plugin.DataManager.GetExcelSheet<Stain>(ClientLanguage.English))
        {
            var name = Normalize(stain.Name.ExtractText());
            if (name.Length > 0 && stain.RowId is > 0 and <= byte.MaxValue)
            {
                this.stainsByName.TryAdd(name, (byte)stain.RowId);
                var c = stain.Color; // 0xRRGGBB
                this.stainColors[(byte)stain.RowId] = new Vector4(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, 1f);
            }
        }

        foreach (var glasses in Plugin.DataManager.GetExcelSheet<Glasses>(ClientLanguage.English))
        {
            var display = glasses.Name.ExtractText();
            if (display.Length > 0)
                this.glassesNames[glasses.RowId] = display;
            if (glasses.Icon > 0)
                this.glassesIcons[glasses.RowId] = (uint)glasses.Icon;
            foreach (var text in new[] { glasses.Name.ExtractText(), glasses.Singular.ExtractText() })
            {
                var name = Normalize(text);
                if (name.Length > 0)
                    this.glassesByName.TryAdd(name, glasses.RowId);
            }
        }

        foreach (var job in Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English))
        {
            var abbr = job.Abbreviation.ExtractText();
            if (abbr.Length == 0)
                continue;
            if (job.JobIndex == 0 && job.DohDolJobIndex < 0)
                this.baseClasses.Add(abbr);
            foreach (var text in new[] { job.Name.ExtractText(), job.NameEnglish.ExtractText(), abbr })
            {
                var name = Normalize(text);
                if (name.Length > 0)
                    this.jobAbbreviations.TryAdd(name, abbr);
            }
        }

        this.Ready = true;
    }

    /// <summary>Lower case, straight apostrophes, single spaces: "Everseeker’s  Coat" and "everseeker's coat" match.</summary>
    public static string Normalize(string s)
    {
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace(' ', ' ').Trim().ToLowerInvariant();
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Finds the item with this name that fits the slot. Returns false if there is none.</summary>
    public bool TryFindItem(string name, SlotKind slot, out Item item)
    {
        item = default;
        if (!this.itemsByName.TryGetValue(Normalize(name), out var ids))
            return false;

        var sheet = Plugin.DataManager.GetExcelSheet<Item>(ClientLanguage.English);
        foreach (var id in ids)
        {
            var candidate = sheet.GetRow(id);
            if (Fits(candidate.EquipSlotCategory.Value, slot))
            {
                item = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>"Monk" → "MNK". Null for anything that isn't a single class or job ("All Classes", "Healers"...).</summary>
    public string? JobAbbreviation(string name)
        => this.jobAbbreviations.TryGetValue(Normalize(name), out var abbr) ? abbr : null;

    /// <summary>The dye's color for swatches, or null for undyed/unknown.</summary>
    public Vector4? StainColor(byte stainId)
        => this.stainColors.TryGetValue(stainId, out var c) ? c : null;

    /// <summary>The facewear's icon id, or 0.</summary>
    public uint GlassesIcon(uint glassesId)
        => this.glassesIcons.TryGetValue(glassesId, out var i) ? i : 0;

    /// <summary>The facewear's full in-game name ("Purple Tinted Sunglasses").</summary>
    public string? GlassesName(uint glassesId)
        => this.glassesNames.TryGetValue(glassesId, out var n) ? n : null;

    public bool TryFindStain(string name, out byte stainId)
        => this.stainsByName.TryGetValue(Normalize(name), out stainId);

    /// <summary>
    /// Facewear colors are separate items in the game ("Purple Tinted Sunglasses"), not dyes.
    /// Tries "{color} {name}" first, then the plain name for styles that come in one color only.
    /// </summary>
    public bool TryFindGlasses(string name, string? color, out uint glassesId)
    {
        if (color != null && this.glassesByName.TryGetValue(Normalize($"{color} {name}"), out glassesId))
            return true;
        return this.glassesByName.TryGetValue(Normalize(name), out glassesId);
    }

    private static bool Fits(EquipSlotCategory c, SlotKind slot) => slot switch
    {
        SlotKind.MainHand => c.MainHand != 0,
        SlotKind.OffHand => c.OffHand != 0,
        SlotKind.Head => c.Head != 0,
        SlotKind.Body => c.Body != 0,
        SlotKind.Hands => c.Gloves != 0,
        SlotKind.Legs => c.Legs != 0,
        SlotKind.Feet => c.Feet != 0,
        SlotKind.Ears => c.Ears != 0,
        SlotKind.Neck => c.Neck != 0,
        SlotKind.Wrists => c.Wrists != 0,
        SlotKind.RightRing or SlotKind.LeftRing => c.FingerL != 0 || c.FingerR != 0,
        _ => false,
    };

    /// <summary>Abbreviations of the jobs (and classes) that can use this item, e.g. "VPR" or "PLD, GLA".</summary>
    public List<string> JobsFor(Item item)
    {
        var result = new List<string>();
        if (!item.ClassJobCategory.IsValid)
            return result;
        var category = item.ClassJobCategory.Value;
        foreach (var job in Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English))
        {
            var abbr = job.Abbreviation.ExtractText();
            if (abbr.Length == 0 || abbr == "ADV")
                continue;
            if (!this.jobColumns.TryGetValue(abbr, out var column))
                this.jobColumns[abbr] = column = typeof(ClassJobCategory).GetProperty(abbr);
            if (column?.GetValue(category) is true)
                result.Add(abbr);
        }

        // Jobs first (they're what people switch to), then base classes.
        return result.OrderBy(a => this.baseClasses.Contains(a)).ToList();
    }

    /// <summary>Can the given job (by its English abbreviation, e.g. "RDM") use this item?</summary>
    public bool JobCanUse(Item item, string jobAbbreviation)
    {
        if (jobAbbreviation.Length == 0 || !item.ClassJobCategory.IsValid)
            return true;
        if (!this.jobColumns.TryGetValue(jobAbbreviation, out var column))
            this.jobColumns[jobAbbreviation] = column = typeof(ClassJobCategory).GetProperty(jobAbbreviation);
        if (column == null)
            return true; // unknown job column (new job?): don't block
        return column.GetValue(item.ClassJobCategory.Value) is true;
    }

    /// <summary>
    /// Returns "Male-only", "Female-only" or "Not for your race" if the item normally can't be worn by this character
    /// (e.g. a female-only top on a male character), or null if it's fine.
    /// Race ids: 1 Hyur, 2 Elezen, 3 Lalafell, 4 Miqo'te, 5 Roegadyn, 6 Au Ra, 7 Hrothgar, 8 Viera.
    /// </summary>
    public static string? Restriction(Item item, bool isFemale, uint raceId)
    {
        if (!item.EquipRestriction.IsValid)
            return null;
        var r = item.EquipRestriction.Value;

        if (r.Male && !r.Female && isFemale)
            return "Male-only";
        if (r.Female && !r.Male && !isFemale)
            return "Female-only";

        var races = new[] { r.Hyur, r.Elezen, r.Lalafell, r.Miqote, r.Roegadyn, r.AuRa, r.Hrothgar, r.Viera };
        var anyRace = Array.IndexOf(races, true) >= 0;
        if (anyRace && raceId is >= 1 and <= 8 && !races[raceId - 1])
            return "Not for your race";
        return null;
    }
}
