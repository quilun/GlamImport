using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GlamImport.Data;
using GlamImport.Parsing;

namespace GlamImport;

public enum PieceState
{
    /// <summary>Found and will be applied.</summary>
    Ok,
    /// <summary>No item with this name for this slot.</summary>
    NotFound,
    /// <summary>A fashion accessory (knapsack, parasol...): Glamourer can't apply these.</summary>
    Unsupported,
    /// <summary>A label this plugin doesn't know.</summary>
    UnknownSlot,
}

/// <summary>A parsed piece matched to game data.</summary>
public sealed class ResolvedPiece
{
    public required ParsedPiece Source { get; init; }
    public PieceState State { get; set; }
    public uint ItemId { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }

    /// <summary>Game icon id for the preview, 0 if not found.</summary>
    public uint IconId { get; set; }

    /// <summary>The weapon has a paired offhand (rapier focus, daggers, bow quiver...) that should change with it.</summary>
    public bool HasPairedOffhand { get; set; }

    /// <summary>Weapons only: the jobs that can use it ("VPR").</summary>
    public List<string> UsedBy { get; set; } = new();

    /// <summary>Weapons only: whether your current job can use it ("Apply to Yourself" skips it otherwise).</summary>
    public bool UsableByCurrentJob { get; set; } = true;

    /// <summary>Non-fatal problems such as unknown dye names.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>"Female-only", "Male-only" or "Not for your race" when the game wouldn't let this character wear it.</summary>
    public string? Restriction { get; set; }

    public string SlotLabel => this.Source.Label;
}

public sealed record PlayerInfo(bool IsFemale, uint RaceId, uint TribeId, string JobAbbreviation);

/// <summary>Extra data for a saved preset: its name, Glamourer tags, and Glamourer's defaults for new designs.</summary>
public sealed record PresetInfo(string Name, IReadOnlyList<string> Tags, Glamourer.GlamourerDefaults Defaults, string Description = "");

public sealed class ImportResult
{
    public required ParsedGlam Parsed { get; init; }
    public List<ResolvedPiece> Pieces { get; } = new();

    public IEnumerable<ResolvedPiece> Applicable => this.Pieces.Where(p => p.State is PieceState.Ok or PieceState.NotFound);

    /// <summary>"Imported Sample Glam: 5/5 gear pieces."</summary>
    public string Summary
    {
        get
        {
            var total = this.Applicable.Count();
            var found = this.Pieces.Count(p => p.State == PieceState.Ok);
            var name = this.Parsed.Title is { } t ? $"Imported {t}" : "Imported";
            return total == 0 ? "No gear found in the pasted text." : $"{name}: {found}/{total} gear pieces.";
        }
    }

    /// <summary>One short line per problem, for the status area.</summary>
    public IEnumerable<string> Notes
    {
        get
        {
            foreach (var p in this.Pieces)
            {
                switch (p.State)
                {
                    case PieceState.NotFound:
                        yield return $"Couldn't find {p.Source.ItemName} ({p.SlotLabel.ToLowerInvariant()}).";
                        break;
                    case PieceState.Unsupported:
                        yield return $"Can't apply {p.Source.ItemName}.";
                        break;
                    case PieceState.UnknownSlot:
                        yield return $"Skipped {p.SlotLabel}: {p.Source.ItemName}.";
                        break;
                }
            }
        }
    }

    /// <summary>Dye problems, plus one grouped line per restriction kind:
    /// "Female-only: Avalanche Brawler's Top, Avalanche Brawler's Boots."</summary>
    public IEnumerable<string> Warnings
    {
        get
        {
            foreach (var w in this.Pieces.SelectMany(p => p.Warnings))
                yield return w;
            foreach (var group in this.Pieces.Where(p => p.Restriction != null).GroupBy(p => p.Restriction!))
                yield return $"{group.Key}: {string.Join(", ", group.Select(p => p.Source.ItemName))}.";
        }
    }

    public bool HasRestricted => this.Pieces.Any(p => p.Restriction != null);
}

public static class Importer
{
    public static ImportResult Resolve(ParsedGlam parsed, GameData data, PlayerInfo? player)
    {
        var result = new ImportResult { Parsed = parsed };
        foreach (var src in parsed.Pieces)
        {
            var piece = new ResolvedPiece { Source = src };
            result.Pieces.Add(piece);

            switch (src.Slot)
            {
                case SlotKind.Fashion:
                    piece.State = PieceState.Unsupported;
                    continue;
                case SlotKind.Unknown:
                    piece.State = PieceState.UnknownSlot;
                    continue;
                case SlotKind.Facewear:
                {
                    // The "dye" shown for facewear is really part of the item name.
                    var color = src.Dyes.FirstOrDefault(d => d != null);
                    if (data.TryFindGlasses(src.ItemName, color, out var glassesId))
                    {
                        piece.State = PieceState.Ok;
                        piece.ItemId = glassesId;
                        piece.IconId = data.GlassesIcon(glassesId);
                    }
                    else
                    {
                        piece.State = PieceState.NotFound;
                    }

                    continue;
                }
            }

            if (!data.TryFindItem(src.ItemName, src.Slot, out var item))
            {
                piece.State = PieceState.NotFound;
                continue;
            }

            piece.State = PieceState.Ok;
            piece.ItemId = item.RowId;
            piece.IconId = item.Icon;
            piece.Stain1 = Stain(src, 0, data, piece);
            piece.Stain2 = Stain(src, 1, data, piece);

            if (src.Slot == SlotKind.MainHand)
            {
                piece.HasPairedOffhand = item.ModelSub != 0;
                piece.UsedBy = data.JobsFor(item);
                if (player != null)
                    piece.UsableByCurrentJob = data.JobCanUse(item, player.JobAbbreviation);
            }
            else if (player != null)
            {
                piece.Restriction = GameData.Restriction(item, player.IsFemale, player.RaceId);
            }
        }

        return result;
    }

    private static byte Stain(ParsedPiece src, int channel, GameData data, ResolvedPiece piece)
    {
        if (channel >= src.Dyes.Count || src.Dyes[channel] is not { } name)
            return 0;
        if (data.TryFindStain(name, out var id))
            return id;
        piece.Warnings.Add($"Unknown dye \"{name}\" on {src.ItemName}, left undyed.");
        return 0;
    }

    public static string SlotKey(SlotKind slot) => slot switch
    {
        SlotKind.MainHand => "MainHand",
        SlotKind.OffHand => "OffHand",
        SlotKind.Head => "Head",
        SlotKind.Body => "Body",
        SlotKind.Hands => "Hands",
        SlotKind.Legs => "Legs",
        SlotKind.Feet => "Feet",
        SlotKind.Ears => "Ears",
        SlotKind.Neck => "Neck",
        SlotKind.Wrists => "Wrists",
        SlotKind.RightRing => "RFinger",
        SlotKind.LeftRing => "LFinger",
        _ => string.Empty,
    };

    /// <summary>
    /// Builds a Glamourer design (FileVersion 1 JSON, Base64 encoded) that applies only the found pieces.
    /// Everything else, including all customization, is left switched off.
    /// </summary>
    /// <param name="preset">When given, writes a full Glamourer design (FileVersion 3) with name, tags and new-design defaults.</param>
    public static string BuildDesign(ImportResult result, PlayerInfo? player, bool includeWeapon,
        RestrictedGearMode restricted = RestrictedGearMode.LetGlamourerDecide, PresetInfo? preset = null)
    {
        using var stream = new MemoryStream();
        using (var j = new Utf8JsonWriter(stream))
        {
            j.WriteStartObject();
            if (preset == null)
            {
                j.WriteNumber("FileVersion", 1);
            }
            else
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                j.WriteNumber("FileVersion", 3);
                j.WriteString("Identifier", Guid.NewGuid());
                j.WriteNumber("CreationDate", now);
                j.WriteNumber("LastEdit", now);
                j.WriteString("Name", preset.Name);
                if (preset.Description.Length > 0)
                    j.WriteString("Description", preset.Description);
                j.WriteBoolean("ForcedRedraw", preset.Defaults.AlwaysForceRedrawing);
                j.WriteBoolean("ResetTemporarySettings", preset.Defaults.ResetTemporarySettings);
                if (preset.Defaults.ResetAdvancedDyes)
                    j.WriteBoolean("ResetAdvancedDyes", true); // true = all slots
                j.WriteBoolean("QuickDesign", preset.Defaults.ShowQuickDesignBar);
                j.WriteStartArray("Tags");
                foreach (var tag in preset.Tags)
                    j.WriteStringValue(tag);
                j.WriteEndArray();
            }

            j.WriteStartObject("Equipment");
            var hasOffhandPiece = result.Pieces.Any(p => p.State == PieceState.Ok && p.Source.Slot == SlotKind.OffHand);
            foreach (var p in result.Pieces.Where(p => p.State == PieceState.Ok && p.Source.Slot != SlotKind.Facewear))
            {
                var slot = p.Source.Slot;
                if (slot == SlotKind.MainHand && !includeWeapon)
                    continue;
                if (slot == SlotKind.OffHand && !includeWeapon)
                    continue;

                // Gear this character can't normally wear: apply it (Glamourer decides), skip it, or leave the slot empty.
                if (p.Restriction != null && restricted == RestrictedGearMode.KeepCurrent)
                    continue;
                if (p.Restriction != null && restricted == RestrictedGearMode.EquipNothing)
                {
                    WriteItem(j, SlotKey(slot), NothingId(slot), 0, 0, true);
                    continue;
                }

                WriteItem(j, SlotKey(slot), p.ItemId, p.Stain1, p.Stain2, true);

                // Paired offhands (focus, second dagger, quiver...) use the weapon's own item id in Glamourer.
                // For anything else, write the id but leave it switched off so Glamourer picks its default.
                if (slot == SlotKind.MainHand && !hasOffhandPiece)
                {
                    if (p.HasPairedOffhand)
                        WriteItem(j, "OffHand", p.ItemId, p.Stain1, p.Stain2, true);
                    else
                        WriteItem(j, "OffHand", NothingShieldId, 0, 0, false); // valid for one-handers and two-handers alike
                }
            }

            // A complete glam takes off whatever it doesn't list (never the weapon: that slot can't be empty).
            foreach (var slot in ClearedSlots(result))
                WriteItem(j, SlotKey(slot), NothingId(slot), 0, 0, true);

            j.WriteEndObject();

            // Facewear: the glam's glasses, or (for a complete glam without any) BonusId 0 = take facewear off.
            j.WriteStartObject("Bonus");
            var glasses = result.Pieces.FirstOrDefault(p => p.Source.Slot == SlotKind.Facewear);
            if (glasses is { State: PieceState.Ok } || glasses == null && result.Parsed.IsCompleteGlam)
            {
                j.WriteStartObject("Glasses");
                j.WriteNumber("BonusId", glasses?.ItemId ?? 0);
                j.WriteBoolean("Apply", true);
                j.WriteEndObject();
            }

            j.WriteEndObject();

            // Customization is required by the format. Use the player's own race and gender (all switched off)
            // so Glamourer doesn't complain about invalid values.
            j.WriteStartObject("Customize");
            j.WriteNumber("ModelId", 0);
            WriteCustomize(j, "Race", (byte)(player?.RaceId is >= 1 and <= 8 ? player.RaceId : 1));
            WriteCustomize(j, "Clan", (byte)(player?.TribeId is >= 1 and <= 16 ? player.TribeId : 1));
            WriteCustomize(j, "Gender", (byte)(player?.IsFemale == true ? 1 : 0));
            WriteCustomize(j, "BodyType", 0);
            j.WriteEndObject();

            j.WriteEndObject();
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    private static readonly SlotKind[] ArmorSlots =
    {
        SlotKind.Head, SlotKind.Body, SlotKind.Hands, SlotKind.Legs, SlotKind.Feet,
        SlotKind.Ears, SlotKind.Neck, SlotKind.Wrists, SlotKind.RightRing, SlotKind.LeftRing,
    };

    /// <summary>Armor and accessory slots a complete glam leaves empty, which get taken off. Empty for partial pastes.
    /// A piece that's listed but wasn't found counts as listed, so a failed lookup never strips you.</summary>
    public static IEnumerable<SlotKind> ClearedSlots(ImportResult result)
    {
        if (!result.Parsed.IsCompleteGlam)
            return Array.Empty<SlotKind>();
        var listed = result.Parsed.Pieces.Select(p => p.Slot).ToHashSet();
        return ArmorSlots.Where(s => !listed.Contains(s));
    }

    /// <summary>Whether a complete glam without facewear will take yours off.</summary>
    public static bool ClearsFacewear(ImportResult result)
        => result.Parsed.IsCompleteGlam && result.Parsed.Pieces.All(p => p.Slot != SlotKind.Facewear);

    /// <summary>Glamourer's empty shield: uint.MaxValue - 384 - FullEquipType.Shield (37).</summary>
    private const uint NothingShieldId = uint.MaxValue - 384 - 37;

    /// <summary>Glamourer's "Nothing" item for a slot: uint.MaxValue - 128 - slot (left ring shares the right ring's value).</summary>
    private static uint NothingId(SlotKind slot)
    {
        var equipSlot = slot switch
        {
            SlotKind.Head => 3u,
            SlotKind.Body => 4u,
            SlotKind.Hands => 5u,
            SlotKind.Legs => 7u,
            SlotKind.Feet => 8u,
            SlotKind.Ears => 9u,
            SlotKind.Neck => 10u,
            SlotKind.Wrists => 11u,
            SlotKind.RightRing or SlotKind.LeftRing => 12u,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
        return uint.MaxValue - 128 - equipSlot;
    }

    private static void WriteItem(Utf8JsonWriter j, string key, uint itemId, byte stain1, byte stain2, bool apply)
    {
        j.WriteStartObject(key);
        j.WriteNumber("ItemId", itemId);
        j.WriteNumber("Stain", stain1);
        j.WriteNumber("Stain2", stain2);
        j.WriteBoolean("Apply", apply);
        j.WriteBoolean("ApplyStain", apply);
        j.WriteEndObject();
    }

    private static void WriteCustomize(Utf8JsonWriter j, string key, byte value)
    {
        j.WriteStartObject(key);
        j.WriteNumber("Value", value);
        j.WriteBoolean("Apply", false);
        j.WriteEndObject();
    }

    /// <summary>For debugging: the design as readable JSON.</summary>
    public static string DecodeDesign(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
