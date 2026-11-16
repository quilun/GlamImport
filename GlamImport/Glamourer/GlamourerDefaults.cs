using System;
using System.IO;
using System.Text.Json;

namespace GlamImport.Glamourer;

/// <summary>
/// Glamourer's settings for new designs (Settings → Design Defaults). Presets saved with tags take a path in Glamourer
/// that skips these, so the plugin copies them into the design itself. "Lock new designs" is applied by Glamourer either way.
/// </summary>
public sealed record GlamourerDefaults(bool AlwaysForceRedrawing, bool ResetAdvancedDyes, bool ShowQuickDesignBar, bool ResetTemporarySettings)
{
    /// <summary>Glamourer's own defaults, used if its settings file can't be read.</summary>
    public static readonly GlamourerDefaults BuiltIn = new(false, false, true, false);

    /// <summary>Reads them from Glamourer's settings file (read-only), next to this plugin's own settings file.</summary>
    public static GlamourerDefaults Read()
    {
        try
        {
            var dir = Plugin.PluginInterface.ConfigFile.Directory;
            var file = dir == null ? null : Path.Combine(dir.FullName, "Glamourer.json");
            if (file == null || !File.Exists(file))
                return BuiltIn;

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("DefaultDesignSettings", out var d) || d.ValueKind != JsonValueKind.Object)
                return BuiltIn;

            bool Get(string name, bool fallback)
                => d.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

            return new GlamourerDefaults(
                Get("AlwaysForceRedrawing", BuiltIn.AlwaysForceRedrawing),
                Get("ResetAdvancedDyes", BuiltIn.ResetAdvancedDyes),
                Get("ShowQuickDesignBar", BuiltIn.ShowQuickDesignBar),
                Get("ResetTemporarySettings", BuiltIn.ResetTemporarySettings));
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't read Glamourer's design defaults, using its built-in defaults");
            return BuiltIn;
        }
    }
}
