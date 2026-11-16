using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Newtonsoft.Json.Linq;

namespace GlamImport.Glamourer;

/// <summary>Talks to Glamourer through its IPC. Labels and signatures match Glamourer.Api (ApiVersion.V2, ApplyState, AddDesign).</summary>
public sealed class GlamourerIpc
{
    // Glamourer.Api ApplyFlag
    private const ulong FlagOnce = 0x01;
    private const ulong FlagEquipment = 0x02;
    private const ulong FlagCustomization = 0x04;

    private readonly IDalamudPluginInterface pi;

    public GlamourerIpc(IDalamudPluginInterface pi) => this.pi = pi;

    /// <summary>The Glamourer API major version this plugin is written against.</summary>
    public const int SupportedMajor = 1;

    /// <summary>
    /// Whether Glamourer is there and offers everything this plugin uses. A different major version means Glamourer
    /// changed its API in an incompatible way; a missing function means it's too old (or renamed something).
    /// </summary>
    public (GlamourerStatus Status, string Message) CheckStatus()
    {
        if (this.ApiVersion() is not { } version)
            return (GlamourerStatus.Missing, "Glamourer isn't installed or enabled. It's needed to apply or save glams.");
        if (version.Major != SupportedMajor)
            return (GlamourerStatus.Unsupported,
                $"This version of Glamourer (API {version.Major}.{version.Minor}) isn't supported yet. This plugin needs API {SupportedMajor}.x; check for an update.");

        var missing = new List<string>();
        void Need(string label, bool present)
        {
            if (!present)
                missing.Add(label.Replace("Glamourer.", string.Empty));
        }

        Need("Glamourer.ApplyState", this.pi.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState").HasFunction);
        Need("Glamourer.AddDesign", this.pi.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign").HasFunction);
        Need("Glamourer.RevertState", this.pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState").HasFunction);
        Need("Glamourer.GetState", this.pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState").HasFunction);
        if (missing.Count > 0)
            return (GlamourerStatus.Unsupported,
                $"This version of Glamourer (API {version.Major}.{version.Minor}) is missing {string.Join(", ", missing)}. Try updating Glamourer.");

        return (GlamourerStatus.Ok, string.Empty);
    }

    /// <summary>Returns Glamourer's API version, or null if Glamourer isn't installed or enabled.</summary>
    public (int Major, int Minor)? ApiVersion()
    {
        try
        {
            return this.pi.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2").InvokeFunc();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the design's gear to a character, like a manual change in Glamourer (undone by Glamourer's revert).
    /// Only equipment is applied; customization in the design is ignored.
    /// </summary>
    public GlamourerError ApplyState(string base64Design, int objectIndex)
    {
        var ec = this.pi.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState")
            .InvokeFunc(base64Design, objectIndex, 0u, FlagEquipment | FlagOnce);
        return (GlamourerError)ec;
    }

    /// <summary>Same as Glamourer's "Revert to Game": puts the character back to its actual in-game state (gear and appearance).</summary>
    public GlamourerError RevertState(int objectIndex)
    {
        var ec = this.pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState")
            .InvokeFunc(objectIndex, 0u, FlagEquipment | FlagCustomization);
        return (GlamourerError)ec;
    }

    /// <summary>
    /// What the character is wearing right now, glamour included, as Glamourer sees it:
    /// the equipment slot keys ("Head", "RFinger"...) that aren't empty, and whether facewear is on.
    /// Null if Glamourer can't say (not installed, character not found).
    /// </summary>
    public (HashSet<string> Slots, bool Facewear)? GetWorn(int objectIndex)
    {
        try
        {
            var (ec, state) = this.pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState").InvokeFunc(objectIndex, 0u);
            if (ec != (int)GlamourerError.Success || state == null)
                return null;

            var slots = new HashSet<string>();
            if (state["Equipment"] is JObject equipment)
            {
                foreach (var (key, value) in equipment)
                {
                    if (IsWorn(value?["ItemId"]?.Value<ulong>() ?? 0))
                        slots.Add(key);
                }
            }

            var facewear = IsWorn(state["Bonus"]?["Glasses"]?["BonusId"]?.Value<ulong>() ?? 0);
            return (slots, facewear);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const ulong CustomFlag = 1ul << 48;

    /// <summary>
    /// Whether a Glamourer item id stands for something actually shown. Empty slots come as 0, as the special
    /// "Nothing"/smallclothes ids just below uint.MaxValue, or as a custom id with model 0 (how an empty facewear
    /// slot is reported).
    /// </summary>
    private static bool IsWorn(ulong id)
    {
        if (id == 0 || id is >= uint.MaxValue - 512 and <= uint.MaxValue)
            return false;
        if ((id & CustomFlag) != 0)
            return (id & 0xFFFF) != 0; // model id
        return true;
    }

    /// <summary>Saves the design as a new named design in Glamourer.</summary>
    public (GlamourerError Error, Guid Id) AddDesign(string base64Design, string name)
    {
        var (ec, id) = this.pi.GetIpcSubscriber<string, string, (int, Guid)>("Glamourer.AddDesign")
            .InvokeFunc(base64Design, name);
        return ((GlamourerError)ec, id);
    }
}

public enum GlamourerStatus
{
    Ok,
    Missing,
    Unsupported,
}

/// <summary>Same values as Glamourer.Api's GlamourerApiEc.</summary>
public enum GlamourerError
{
    Success = 0,
    NothingDone = 1,
    ActorNotFound = 2,
    ActorNotHuman = 3,
    DesignNotFound = 4,
    ItemInvalid = 5,
    InvalidKey = 6,
    InvalidState = 7,
    CouldNotParse = 8,
    UnknownError = int.MaxValue,
}
