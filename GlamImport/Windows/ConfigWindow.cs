using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using GlamImport.Parsing;

namespace GlamImport.Windows;

public sealed class ConfigWindow : Window
{
    private static readonly Vector4 Grey = new(0.6f, 0.6f, 0.6f, 1f);

    /// <summary>Made-up glam for the name preview. Always used, so the example never shows real glam, creator or character names.</summary>
    private static readonly ParsedGlam Sample = new("Sample Glam", "Creator Name", Array.Empty<ParsedPiece>(), true, "Monk", "Female", "Au Ra Female",
        new[] { "Cute", "Adventurer" }, "Continue your adventure in style! Dye the coat in any earthy tone to match your weapon.");

    private const string SampleCharacter = "Your Character";

    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("GlamImport Settings###GlamImportConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(520, 380);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 280), MaximumSize = new Vector2(1200, 1200) };
    }

    public override void Draw()
    {
        var config = this.plugin.Config;
        ImGui.PushTextWrapPos(0f);

        ImGui.TextUnformatted("Gender- or Race-Locked Gear");
        this.ModeOption(RestrictedGearMode.LetGlamourerDecide, "Let Glamourer decide");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Applies it anyway. Glamourer's \"Use Replacement Gear for Gear Unavailable to Your Race or Gender\" " +
                                   "setting decides whether you get a stand-in or a broken piece.");
        this.ModeOption(RestrictedGearMode.KeepCurrent, "Keep current");
        this.ModeOption(RestrictedGearMode.EquipNothing, "Equip nothing");

        Section("Glamourer Preset Tags");
        var addTags = config.AddTags;
        if (ImGui.Checkbox("Import tags from Eorzea Collection", ref addTags))
        {
            config.AddTags = addTags;
            config.Save();
        }

        ImGui.BeginDisabled(!config.AddTags);
        var addCollection = config.AddCollectionTag;
        if (ImGui.Checkbox("Add an \"Eorzea Collection\" tag", ref addCollection))
        {
            config.AddCollectionTag = addCollection;
            config.Save();
        }

        ImGui.EndDisabled();

        Section("Preset Descriptions");
        this.DrawDescriptionSettings();

        Section("Preset Names");
        var template = config.PresetNameTemplate;
        var resetText = "Reset";
        var style = ImGui.GetStyle();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - ImGui.CalcTextSize(resetText).X - style.FramePadding.X * 2 - style.ItemSpacing.X * 2);
        if (ImGui.InputText("##template", ref template, 200))
        {
            config.PresetNameTemplate = template;
            config.Save();
            this.plugin.MainWindow.RefreshPresetName();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(TagList +
                                   "\n\nTags without a value are left out, along with their empty brackets." +
                                   "\n\nA / makes a Glamourer folder, so write dates with dots or dashes ({DD}.{MM}.{YY}).");
        ImGui.SameLine();
        ImGui.BeginDisabled(config.PresetNameTemplate == Configuration.DefaultPresetName);
        if (ImGui.Button(resetText))
        {
            config.PresetNameTemplate = Configuration.DefaultPresetName;
            config.Save();
            this.plugin.MainWindow.RefreshPresetName();
        }

        ImGui.EndDisabled();
        ImGui.TextColored(Grey, PresetName.Format(config.PresetNameTemplate, Sample, DateTime.Now, this.plugin.Data.JobAbbreviation, SampleCharacter));

        ImGui.PopTextWrapPos();
    }

    private static string TagList
        => "Tags:\n" + string.Join("\n", PresetName.Tags.Select(t => $"{t.Tag}  {t.Meaning}"));

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextUnformatted(title);
    }

    private void DrawDescriptionSettings()
    {
        var config = this.plugin.Config;
        foreach (var (mode, label) in new[]
                 {
                     (DescriptionMode.CreatorTextAndInfo, "Creator's description + info"),
                     (DescriptionMode.CreatorTextOnly, "Creator's description only"),
                     (DescriptionMode.Off, "No description"),
                 })
        {
            if (ImGui.RadioButton(label, config.Description == mode))
            {
                config.Description = mode;
                config.Save();
            }
        }

        if (config.Description != DescriptionMode.CreatorTextAndInfo)
            return;

        // Template box with the tag list beside it; below: Preview (hover) on the left, Reset on the right.
        var info = config.DescriptionInfoTemplate;
        var style = ImGui.GetStyle();
        var boxHeight = ImGui.GetTextLineHeight() * 3 + style.FramePadding.Y * 2;
        if (ImGui.InputTextMultiline("##infoTemplate", ref info, 1000,
                new Vector2(ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight() - style.ItemSpacing.X, boxHeight)))
        {
            config.DescriptionInfoTemplate = info;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(TagList + "\n\nA line is left out if one of its tags has no value.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Grey, "Preview");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(PresetName.Description(config.Description, config.DescriptionInfoTemplate, Sample, DateTime.Now,
                this.plugin.Data.JobAbbreviation, SampleCharacter));

        var resetWidth = ImGui.CalcTextSize("Reset").X + style.FramePadding.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - resetWidth);
        ImGui.BeginDisabled(config.DescriptionInfoTemplate == Configuration.DefaultDescriptionInfo);
        if (ImGui.Button("Reset##info"))
        {
            config.DescriptionInfoTemplate = Configuration.DefaultDescriptionInfo;
            config.Save();
        }

        ImGui.EndDisabled();
    }

    private void ModeOption(RestrictedGearMode mode, string label)
    {
        var config = this.plugin.Config;
        if (ImGui.RadioButton(label, config.RestrictedGear == mode))
        {
            config.RestrictedGear = mode;
            config.Save();
        }
    }
}
