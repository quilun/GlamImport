using System;
using Dalamud.Configuration;

namespace GlamImport;

/// <summary>What to do with gear your character can't normally wear (gender- or race-locked).</summary>
public enum RestrictedGearMode
{
    /// <summary>Apply it anyway; Glamourer's replacement gear setting decides between a stand-in and a broken piece.</summary>
    LetGlamourerDecide,

    /// <summary>Skip it and keep what you're wearing in that slot.</summary>
    KeepCurrent,

    /// <summary>Skip it and leave the slot empty.</summary>
    EquipNothing,
}

/// <summary>What goes into a saved preset's Glamourer description.</summary>
public enum DescriptionMode
{
    Off,
    CreatorTextOnly,
    CreatorTextAndInfo,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const string DefaultPresetName = "Eorzea Collection Import/{name} ({creator})";

    /// <summary>The default before version 2, without the folder.</summary>
    private const string OldDefaultPresetName = "{name} ({creator})";

    public const string DefaultDescriptionInfo =
        "{name} by {creator}\nMade on {model} · Intended for {job}\nImported from Eorzea Collection on {date}";

    public int Version { get; set; } = 2;

    public RestrictedGearMode RestrictedGear { get; set; } = RestrictedGearMode.LetGlamourerDecide;

    public string PresetNameTemplate { get; set; } = DefaultPresetName;

    /// <summary>Add the glam's Eorzea Collection tags (Cute, Cosplay...) to saved presets as Glamourer tags.</summary>
    public bool AddTags { get; set; } = true;

    /// <summary>Also add an "Eorzea Collection" tag, so t:Eorzea Collection lists every import.</summary>
    public bool AddCollectionTag { get; set; } = true;

    public DescriptionMode Description { get; set; } = DescriptionMode.CreatorTextAndInfo;

    /// <summary>The info block under the creator's text. Lines with a missing value are left out.</summary>
    public string DescriptionInfoTemplate { get; set; } = DefaultDescriptionInfo;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>Updates settings saved by older versions. Version 2: the default preset name gained a folder;
    /// anyone still on the old default gets the new one, custom templates are kept.</summary>
    public void Migrate()
    {
        if (this.Version >= 2)
            return;
        if (this.PresetNameTemplate == OldDefaultPresetName)
            this.PresetNameTemplate = DefaultPresetName;
        this.Version = 2;
        this.Save();
    }
}
