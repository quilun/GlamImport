using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Player;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using GlamImport.Glamourer;
using GlamImport.Parsing;
using Lumina.Excel.Sheets;

namespace GlamImport.Windows;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Green = new(0.55f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.8f, 0.35f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.6f, 1f);

    private const int MaxPasteLength = 64 * 1024;

    private readonly Plugin plugin;
    private string input = string.Empty;
    private string presetName = string.Empty;
    private bool presetNameEdited;
    private ImportResult? result;
    private PlayerInfo? resolvedFor;
    private string? actionMessage;
    private bool actionFailed;

    // What you're wearing right now (from Glamourer), so "will be taken off" only lists slots that actually change.
    private (System.Collections.Generic.HashSet<string> Slots, bool Facewear)? worn;
    private DateTime wornCheckedAt = DateTime.MinValue;

    // Glamourer availability, checked every couple of seconds rather than every frame.
    private (GlamourerStatus Status, string Message) glamourerStatus = (GlamourerStatus.Missing, string.Empty);
    private DateTime glamourerCheckedAt = DateTime.MinValue;

    public MainWindow(Plugin plugin)
        : base("GlamImport###GlamImportMain")
    {
        this.plugin = plugin;
        this.Size = new Vector2(640, 600);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 360), MaximumSize = new Vector2(1600, 1600) };
    }

    // Layout, top to bottom, in the order you use it:
    // 1. paste   2. check (glam header, gear table, notes)   3. act (wear it / keep it) with the result right below.
    public override void Draw()
    {
        var player = ReadPlayer();
        if (this.result != null && player != this.resolvedFor && this.plugin.Data.Ready)
            this.Resolve(player); // job or character changed: refresh the weapon and restriction notes

        this.DrawPasteArea(player);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (!this.plugin.Data.Ready)
        {
            ImGui.TextColored(Grey, "Loading item data...");
            return;
        }

        if (this.result is { Pieces.Count: > 0 } r)
        {
            this.DrawGlamHeader(r);
            this.DrawGearTable(r);
            this.DrawNotes(r);
        }
        else
        {
            ImGui.TextColored(Grey, this.result == null
                ? "Paste a glam above to see its gear here."
                : "No gear found in the pasted text. Make sure the copy includes the Equipment section.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        this.DrawActions(player);
    }

    private void DrawPasteArea(PlayerInfo? player)
    {
        ImGuiComponents.HelpMarker(
            "1. Open the glam on ffxiv.eorzeacollection.com in your browser.\n" +
            "2. Select the gear section (or the whole page with Ctrl+A) and copy it.\n" +
            "3. Paste it below, or click \"Import from Clipboard\".\n" +
            "Copying the whole page (Ctrl+A) also picks up the glam's name, creator, tags and description.");
        ImGui.SameLine();
        ImGui.TextUnformatted("Paste a glam from Eorzea Collection:");
        var iconRow = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - iconRow);
        ImGui.BeginDisabled(this.plugin.Recent.Entries.Count == 0);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.History))
            ImGui.OpenPopup("##recent");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(this.plugin.Recent.Entries.Count == 0 ? "Recent imports (none yet)" : "Recent imports");
        this.DrawRecentPopup(player);
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
            this.plugin.ToggleConfig();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Settings");

        var buttonText = "Import from Clipboard";
        var style = ImGui.GetStyle();
        var buttonWidth = ImGui.CalcTextSize(buttonText).X + style.FramePadding.X * 2;
        var inputWidth = ImGui.GetContentRegionAvail().X - buttonWidth - style.ItemSpacing.X;
        var lines = this.result == null ? 5 : 3; // shrink the raw text once it has been read
        var inputHeight = ImGui.GetTextLineHeight() * lines + style.FramePadding.Y * 2;
        if (ImGui.InputTextMultiline("##paste", ref this.input, MaxPasteLength, new Vector2(inputWidth, inputHeight)))
            this.OnInputChanged(player);
        ImGui.SameLine();
        if (ImGui.Button(buttonText))
        {
            this.input = ImGui.GetClipboardText() ?? string.Empty;
            this.OnInputChanged(player);
        }
    }

    /// <summary>"Sample Glam  by Creator Name                 8/8 found"</summary>
    private void DrawGlamHeader(ImportResult r)
    {
        var total = r.Applicable.Count();
        var found = r.Pieces.Count(p => p.State == PieceState.Ok);
        var count = $"{found}/{total} found";

        ImGui.TextUnformatted(r.Parsed.Title ?? "Untitled glam");
        if (r.Parsed.Creator is { } creator)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"by {creator}");
        }

        ImGui.SameLine();
        var right = ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(count).X;
        if (right > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(right);
        ImGui.TextColored(found == total && total > 0 ? Green : Yellow, count);
    }

    private void DrawGearTable(ImportResult r)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##gear", 5, flags))
            return;

        var scale = ImGuiHelpers.GlobalScale;
        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 70 * scale);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 1.0f);
        ImGui.TableSetupColumn("Dye 1", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn("Dye 2", ImGuiTableColumnFlags.WidthStretch, 0.45f);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 80 * scale);
        ImGui.TableHeadersRow();

        foreach (var p in r.Pieces)
        {
            ImGui.PushID(p.GetHashCode());
            ImGui.TableNextRow();

            // Rows are one frame tall (icon and dye boxes); text is lined up to the middle.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Grey, SlotName(p.Source));

            ImGui.TableNextColumn();
            DrawIcon(p.IconId);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            var name = p.Source.Slot == SlotKind.Facewear && p.State == PieceState.Ok
                ? this.plugin.Data.GlassesName(p.ItemId) ?? p.Source.ItemName
                : p.Source.ItemName;
            if (p.State is PieceState.Unsupported or PieceState.UnknownSlot)
                ImGui.TextColored(Grey, name);
            else
                ImGui.TextUnformatted(name);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(name);

            for (var channel = 0; channel < 2; channel++)
            {
                ImGui.TableNextColumn();
                this.DrawDye(p, channel);
            }

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            this.DrawState(p);
            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    /// <summary>The item's game icon, one frame tall; an empty dark square if there's none.</summary>
    private static void DrawIcon(uint iconId)
    {
        var size = new Vector2(ImGui.GetFrameHeight());
        if (iconId != 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, size);
            return;
        }

        var min = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(min, min + size, ImGui.GetColorU32(ImGuiCol.FrameBg), ImGui.GetStyle().FrameRounding);
        ImGui.Dummy(size);
    }

    /// <summary>
    /// A box filled with the dye's color and its name inside, like Glamourer's dye buttons.
    /// "Undyed" is an empty dark box; a channel the item doesn't have is left blank.
    /// </summary>
    private void DrawDye(ResolvedPiece p, int channel)
    {
        if (p.Source.Slot == SlotKind.Facewear || channel >= p.Source.Dyes.Count)
            return;

        var dyeName = p.Source.Dyes[channel];
        var stain = channel == 0 ? p.Stain1 : p.Stain2;
        var color = dyeName == null ? null : this.plugin.Data.StainColor(stain);
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight());
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var draw = ImGui.GetWindowDrawList();
        var rounding = ImGui.GetStyle().FrameRounding;

        draw.AddRectFilled(min, max, color is { } c ? ImGui.GetColorU32(c) : ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
        if (color != null)
            draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.25f)), rounding); // keeps dark dyes visible

        var label = dyeName ?? "Undyed";
        var textColor = color is { } bg
            ? (0.299f * bg.X + 0.587f * bg.Y + 0.114f * bg.Z > 0.55f ? new Vector4(0.05f, 0.05f, 0.05f, 1f) : new Vector4(1f, 1f, 1f, 1f))
            : Grey;
        var pad = ImGui.GetStyle().FramePadding;
        draw.PushClipRect(min, max, true);
        draw.AddText(min + pad, ImGui.GetColorU32(textColor), label);
        draw.PopClipRect();

        ImGui.Dummy(size);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(label);
    }

    /// <summary>A green check for OK; words only where something needs your attention.</summary>
    private void DrawState(ResolvedPiece p)
    {
        if (p.State == PieceState.Ok && p.UsableByCurrentJob && p.Restriction == null && p.Warnings.Count == 0)
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(Green, FontAwesomeIcon.Check.ToIconString());
            ImGui.PopFont();
            return;
        }

        var (text, color) = p.State switch
        {
            PieceState.Ok when !p.UsableByCurrentJob => ("Other job", Yellow),
            PieceState.Ok when p.Restriction != null => (p.Restriction == "Not for your race" ? "Race-locked" : p.Restriction, Yellow),
            PieceState.Ok => ("Check", Yellow),
            PieceState.NotFound => ("Not found", Red),
            PieceState.Unsupported => ("Can't apply", Grey),
            _ => ("Skipped", Grey),
        };
        ImGui.TextColored(color, text);
        if (ImGui.IsItemHovered() && this.Tooltip(p) is { } tip)
            ImGui.SetTooltip(tip);
    }

    /// <summary>What needs attention, right under the table it's about.</summary>
    private void DrawNotes(ImportResult r)
    {
        ImGui.PushTextWrapPos(0f);
        foreach (var note in r.Notes)
            ImGui.TextColored(note.StartsWith("Couldn't") ? Red : Grey, note);
        foreach (var warning in r.Warnings)
            ImGui.TextColored(Yellow, warning);
        if (r.HasRestricted)
            ImGui.TextColored(Grey, RestrictedNote(this.plugin.Config.RestrictedGear));

        // Only mention slots where you're actually wearing something (if Glamourer can tell us).
        this.RefreshWorn();
        var cleared = Importer.ClearedSlots(r)
            .Where(s => this.worn == null || this.worn.Value.Slots.Contains(Importer.SlotKey(s)))
            .Select(SlotWord).ToList();
        if (Importer.ClearsFacewear(r) && (this.worn == null || this.worn.Value.Facewear))
            cleared.Add("facewear");
        if (cleared.Count > 0)
            ImGui.TextColored(Grey, $"Not part of this glam, will be taken off: {string.Join(", ", cleared.Distinct())}.");
        ImGui.PopTextWrapPos();
    }

    private static string RestrictedNote(RestrictedGearMode mode) => mode switch
    {
        RestrictedGearMode.KeepCurrent => "Your character can't normally wear these, so they're skipped and you keep what you're wearing there (see Settings).",
        RestrictedGearMode.EquipNothing => "Your character can't normally wear these, so they're skipped and those slots are left empty (see Settings).",
        _ => "Your character can't normally wear these. Turn on \"Use Replacement Gear for Gear Unavailable to Your Race or Gender\" " +
             "in Glamourer's settings to get a stand-in, otherwise they may show up broken. You can also skip them (see Settings).",
    };

    private string? Tooltip(ResolvedPiece p)
    {
        var lines = new List<string>(p.Warnings);
        if (p.Restriction != null)
            lines.Add($"{p.Restriction}: {RestrictedNote(this.plugin.Config.RestrictedGear)}");
        if (!p.UsableByCurrentJob)
            lines.Add((p.UsedBy.Count > 0 ? "Used by: " + string.Join(", ", p.UsedBy) + "\n" : string.Empty) +
                      "Your current job can't use this weapon. \"Apply to Yourself\" skips it; a saved preset still includes it.");
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    private static string SlotWord(SlotKind s) => s switch
    {
        SlotKind.Ears => "earrings",
        SlotKind.Neck => "necklace",
        SlotKind.Wrists => "bracelets",
        SlotKind.RightRing => "right ring",
        SlotKind.LeftRing => "left ring",
        _ => s.ToString().ToLowerInvariant(),
    };

    private static string SlotName(ParsedPiece p) => p.Slot switch
    {
        SlotKind.MainHand => "Weapon",
        SlotKind.OffHand => "Off hand",
        SlotKind.RightRing => "Ring (R)",
        SlotKind.LeftRing => "Ring (L)",
        SlotKind.Facewear => "Facewear",
        SlotKind.Fashion => "Fashion",
        SlotKind.Unknown => p.Label,
        _ => p.Slot.ToString(),
    };

    private void RefreshWorn(bool force = false)
    {
        if (!force && DateTime.UtcNow - this.wornCheckedAt < TimeSpan.FromSeconds(1))
            return;
        this.wornCheckedAt = DateTime.UtcNow;
        this.worn = Plugin.ObjectTable.LocalPlayer is { } me ? this.plugin.Glamourer.GetWorn(me.ObjectIndex) : null;
    }

    /// <summary>Wear it (apply / revert) on one row, keep it (name + save) on the next, and the result right below.</summary>
    private void DrawActions(PlayerInfo? player)
    {
        if (DateTime.UtcNow - this.glamourerCheckedAt > TimeSpan.FromSeconds(2))
        {
            this.glamourerCheckedAt = DateTime.UtcNow;
            this.glamourerStatus = this.plugin.Glamourer.CheckStatus();
        }

        var glamourerOk = this.glamourerStatus.Status == GlamourerStatus.Ok;
        var hasGear = this.result != null && this.result.Pieces.Any(p => p.State == PieceState.Ok);
        if (!glamourerOk)
        {
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(Red, this.glamourerStatus.Message);
            ImGui.PopTextWrapPos();
        }

        ImGui.BeginDisabled(!hasGear || !glamourerOk || player == null);
        if (ImGui.Button("Apply to Yourself"))
            this.ApplyToSelf(player!);
        ImGui.EndDisabled();

        ImGui.SameLine();
        var target = CurrentTarget();
        ImGui.BeginDisabled(!hasGear || !glamourerOk || target == null);
        if (ImGui.Button("Apply to Target"))
            this.ApplyToTarget(target!);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(target != null ? $"Apply to {target.Name.TextValue}." : "Target another character (player, NPC, or GPose actor) first.");

        ImGui.SameLine();
        ImGui.BeginDisabled(!glamourerOk || player == null);
        if (ImGui.Button("Revert to Game"))
            this.RevertToGame();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Revert the character to its actual state in the game.");

        var saveText = "Save as Glamourer Preset";
        var style = ImGui.GetStyle();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Preset Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(saveText).X - style.FramePadding.X * 2 - style.ItemSpacing.X);
        if (ImGui.InputText("##presetName", ref this.presetName, 128))
            this.presetNameEdited = true;
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasGear || !glamourerOk);
        if (ImGui.Button(saveText))
            this.SaveAsPreset(player);
        ImGui.EndDisabled();

        if (this.actionMessage != null)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextColored(this.actionFailed ? Red : Green, this.actionMessage);
            ImGui.PopTextWrapPos();
        }
    }

    private void OnInputChanged(PlayerInfo? player)
    {
        this.actionMessage = null;
        if (this.input.Trim().Length == 0)
        {
            this.result = null;
            return;
        }

        if (!this.plugin.Data.Ready)
            return;
        this.Resolve(player);
    }

    private void Resolve(PlayerInfo? player)
    {
        var parsed = GlamParser.Parse(this.input);
        this.result = Importer.Resolve(parsed, this.plugin.Data, player);
        this.resolvedFor = player;

        if (!this.presetNameEdited || this.presetName.Trim().Length == 0)
            this.RefreshPresetName();
    }

    /// <summary>The glam currently shown, for the settings window's name preview.</summary>
    internal ParsedGlam? CurrentGlam => this.result?.Parsed;

    /// <summary>Re-fills the preset name from the naming template (unless you've typed your own).</summary>
    internal void RefreshPresetName(bool force = false)
    {
        if (this.result == null || (this.presetNameEdited && !force && this.presetName.Trim().Length > 0))
            return;
        this.presetNameEdited = false;
        this.presetName = PresetName.Format(this.plugin.Config.PresetNameTemplate, this.result.Parsed, DateTime.Now, this.plugin.Data.JobAbbreviation, Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CharacterName : null);
    }

    private void ApplyToSelf(PlayerInfo player)
    {
        if (this.result == null || Plugin.ObjectTable.LocalPlayer is not { } me)
            return;

        var weapon = this.result.Pieces.FirstOrDefault(p => p.State == PieceState.Ok && p.Source.Slot == SlotKind.MainHand);
        var includeWeapon = weapon == null || weapon.UsableByCurrentJob;
        var design = Importer.BuildDesign(this.result, player, includeWeapon, this.plugin.Config.RestrictedGear);

        try
        {
            var ec = this.plugin.Glamourer.ApplyState(design, me.ObjectIndex);
            this.RefreshWorn(force: true);
            this.actionFailed = ec != GlamourerError.Success;
            if (ec == GlamourerError.Success)
                this.plugin.Recent.Add(this.result.Parsed, this.input);
            this.actionMessage = ec switch
            {
                GlamourerError.Success when !includeWeapon => $"Applied to you. Skipped the weapon ({weapon!.Source.ItemName}): your current job can't use it.",
                GlamourerError.Success => "Applied to you.",
                _ => $"Glamourer couldn't apply it ({ec}).",
            };
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ApplyState failed");
            this.actionFailed = true;
            this.actionMessage = "Glamourer couldn't apply it. See /xllog for details.";
        }
    }

    /// <summary>The character you're targeting (in GPose: the selected actor), if it's someone other than you.</summary>
    private static ICharacter? CurrentTarget()
    {
        var target = Plugin.ClientState.IsGPosing ? Plugin.TargetManager.GPoseTarget : Plugin.TargetManager.Target;
        if (target is not ICharacter character)
            return null;
        if (Plugin.ObjectTable.LocalPlayer is { } me && character.Address == me.Address)
            return null;
        return character;
    }

    /// <summary>
    /// Applies the glam to your target. Gender/race locks and the weapon are judged for the target, not for you:
    /// its gear is checked against its own body, and the weapon is skipped only if it's a player on another job.
    /// </summary>
    private void ApplyToTarget(ICharacter target)
    {
        if (this.result == null)
            return;

        var info = TargetInfo(target);
        var forTarget = Importer.Resolve(this.result.Parsed, this.plugin.Data, info);
        var weapon = forTarget.Pieces.FirstOrDefault(p => p.State == PieceState.Ok && p.Source.Slot == SlotKind.MainHand);
        var includeWeapon = weapon == null || weapon.UsableByCurrentJob;
        var design = Importer.BuildDesign(forTarget, info, includeWeapon, this.plugin.Config.RestrictedGear);
        var name = target.Name.TextValue;

        try
        {
            var ec = this.plugin.Glamourer.ApplyState(design, target.ObjectIndex);
            this.actionFailed = ec != GlamourerError.Success;
            if (ec == GlamourerError.Success)
                this.plugin.Recent.Add(this.result.Parsed, this.input);
            this.actionMessage = ec switch
            {
                GlamourerError.Success when !includeWeapon => $"Applied to {name}. Skipped the weapon ({weapon!.Source.ItemName}): their job can't use it.",
                GlamourerError.Success => $"Applied to {name}.",
                GlamourerError.ActorNotHuman => $"Glamourer can't dress {name}: it isn't a humanoid character.",
                _ => $"Glamourer couldn't apply it to {name} ({ec}).",
            };
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ApplyState on target failed");
            this.actionFailed = true;
            this.actionMessage = "Glamourer couldn't apply it. See /xllog for details.";
        }
    }

    /// <summary>The target's gender, race and (for players) job, read from the character itself.</summary>
    private static PlayerInfo TargetInfo(ICharacter c)
    {
        var custom = c.Customize;
        var race = custom.Length > 4 ? custom[(int)CustomizeIndex.Race] : (byte)0;
        var female = custom.Length > 4 && custom[(int)CustomizeIndex.Gender] == 1;
        var tribe = custom.Length > 4 ? custom[(int)CustomizeIndex.Tribe] : (byte)0;
        var job = string.Empty;
        if (c is IPlayerCharacter player &&
            Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English).TryGetRow(player.ClassJob.RowId, out var row))
            job = row.Abbreviation.ExtractText();
        return new PlayerInfo(female, race, tribe, job);
    }

    private void DrawRecentPopup(PlayerInfo? player)
    {
        if (!ImGui.BeginPopup("##recent"))
            return;

        ImGui.TextColored(Grey, "Recent imports");
        ImGui.Separator();
        foreach (var entry in this.plugin.Recent.Entries)
        {
            var label = entry.Creator is { } c ? $"{entry.Title}  by {c}" : entry.Title;
            if (ImGui.Selectable($"{label}##{entry.UsedAt.Ticks}"))
            {
                this.input = entry.Text;
                this.presetNameEdited = false;
                this.OnInputChanged(player);
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Last used {entry.UsedAt:yyyy-MM-dd HH:mm}");
        }

        ImGui.Separator();
        if (ImGui.Selectable("Clear list"))
        {
            this.plugin.Recent.Clear();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void RevertToGame()
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } me)
            return;
        try
        {
            var ec = this.plugin.Glamourer.RevertState(me.ObjectIndex);
            this.RefreshWorn(force: true);
            this.actionFailed = ec is not (GlamourerError.Success or GlamourerError.NothingDone);
            this.actionMessage = ec switch
            {
                GlamourerError.Success => "Reverted to your in-game look.",
                GlamourerError.NothingDone => "Nothing to revert: you already look like you do in-game.",
                _ => $"Glamourer couldn't revert ({ec}).",
            };
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "RevertState failed");
            this.actionFailed = true;
            this.actionMessage = "Glamourer couldn't revert. See /xllog for details.";
        }
    }

    private void SaveAsPreset(PlayerInfo? player)
    {
        if (this.result == null)
            return;

        var name = this.presetName.Trim().Length > 0 ? this.presetName.Trim() : $"GlamImport {DateTime.Now:yyyy-MM-dd}";
        var tags = this.PresetTags();
        var config = this.plugin.Config;
        var description = PresetName.Description(config.Description, config.DescriptionInfoTemplate, this.result.Parsed, DateTime.Now,
            this.plugin.Data.JobAbbreviation, Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.CharacterName : null);
        var preset = tags.Count > 0 || description.Length > 0 ? new PresetInfo(name, tags, GlamourerDefaults.Read(), description) : null;
        var design = Importer.BuildDesign(this.result, player, includeWeapon: true, this.plugin.Config.RestrictedGear, preset);
        try
        {
            var (ec, _) = this.plugin.Glamourer.AddDesign(design, name);
            this.actionFailed = ec != GlamourerError.Success;
            if (ec == GlamourerError.Success)
                this.plugin.Recent.Add(this.result.Parsed, this.input);
            this.actionMessage = ec == GlamourerError.Success
                ? tags.Count > 0 ? $"Saved as \"{name}\" in Glamourer, tagged {string.Join(", ", tags)}." : $"Saved as \"{name}\" in Glamourer."
                : $"Glamourer couldn't save it ({ec}).";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "AddDesign failed");
            this.actionFailed = true;
            this.actionMessage = "Glamourer couldn't save it. See /xllog for details.";
        }
    }

    /// <summary>Glamourer tags for a saved preset, per the settings: the glam's Eorzea Collection tags, plus "Eorzea Collection".</summary>
    private List<string> PresetTags()
    {
        var tags = new List<string>();
        var config = this.plugin.Config;
        if (!config.AddTags || this.result == null)
            return tags;
        if (config.AddCollectionTag)
            tags.Add("Eorzea Collection");
        foreach (var tag in this.result.Parsed.Tags ?? Array.Empty<string>())
        {
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }

        return tags;
    }

    /// <summary>Your character's gender, race and current job, or null when not logged in.</summary>
    private static PlayerInfo? ReadPlayer()
    {
        var ps = Plugin.PlayerState;
        if (!ps.IsLoaded)
            return null;

        var job = string.Empty;
        var jobs = Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English);
        if (jobs.TryGetRow(ps.ClassJob.RowId, out var row))
            job = row.Abbreviation.ExtractText();
        return new PlayerInfo(ps.Sex == Sex.Female, ps.Race.RowId, ps.Tribe.RowId, job);
    }
}
