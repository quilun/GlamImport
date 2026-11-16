using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamImport.Data;
using GlamImport.Glamourer;
using GlamImport.Windows;

namespace GlamImport;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/glamimport";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("GlamImport");
    private readonly ConfigWindow configWindow;

    internal Configuration Config { get; }
    internal MainWindow MainWindow { get; }

    internal GameData Data { get; } = new();
    internal GlamourerIpc Glamourer { get; }
    internal RecentImports Recent { get; private set; } = null!;

    public Plugin()
    {
        this.Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Config.Migrate();
        this.Glamourer = new GlamourerIpc(PluginInterface);
        this.Recent = new RecentImports(PluginInterface.ConfigDirectory.FullName);
        this.MainWindow = new MainWindow(this);
        this.configWindow = new ConfigWindow(this);
        this.windowSystem.AddWindow(this.MainWindow);
        this.windowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler(Command, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the GlamImport window. \"/glamimport config\" opens the settings.",
        });

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfig;

        Task.Run(() =>
        {
            try
            {
                this.Data.Load();
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to load item data");
            }
        });
    }

    internal void ToggleConfig() => this.configWindow.Toggle();

    /// <summary>"/glamimport" toggles the main window, "/glamimport config" the settings.</summary>
    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", System.StringComparison.OrdinalIgnoreCase))
            this.configWindow.Toggle();
        else
            this.MainWindow.Toggle();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfig;
        CommandManager.RemoveHandler(Command);
        this.windowSystem.RemoveAllWindows();
    }
}
