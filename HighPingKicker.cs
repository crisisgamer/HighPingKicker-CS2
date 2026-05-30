using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;

using HighPingKicker.Utils;
using HighPingKicker.Models;
using HighPingKicker.Configs;

namespace HighPingKicker;

[MinimumApiVersion(369)]
public class HighPingKickerPlugin : BasePlugin, IPluginConfig<BaseConfigs>
{
    public override string ModuleName => "High Ping Kicker";
    public override string ModuleVersion => "0.0.9";
    public override string ModuleAuthor => "conch (forked by luca.uy)";
    public override string ModuleDescription => "Kicks users with high ping";

    public BaseConfigs Config { get; set; } = new();
    public Dictionary<int, PlayerInfo> Slots = new();


    public void OnConfigParsed(BaseConfigs config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        if (hotReload) GetPlayers().ForEach(Reset);

        AddTimer(Config.CheckInterval, CheckPings, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
    }
    private List<CCSPlayerController> GetPlayers()
    {
        return Utilities.GetPlayers().FindAll(p => p is
        {
            IsValid: true,
            IsBot: false,
            IsHLTV: false,
            Connected: PlayerConnectedState.Connected
        });
    }

    private void OnMapStartHandler(string mapName)
    {
        AddTimer(Config.GracePeriod, () =>
        {
            AddTimer(Config.CheckInterval, CheckPings, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    [GameEventHandler]
    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid != null)
        {
            Reset(@event.Userid);
        }
        return HookResult.Continue;
    }

    public void Reset(CCSPlayerController player)
    {
        if (!Slots.TryGetValue(player.Slot, out PlayerInfo? playerInfo))
        {
            playerInfo = new();
            Slots.Add(player.Slot, playerInfo);
        }
        playerInfo.IsInGracePeriod = true;
        playerInfo.WarningsGiven = 0;
        playerInfo.IsWhitelisted = IsPlayerWhitelisted(player);
        playerInfo.Timer?.Kill();
        playerInfo.Timer = new Timer(Config.GracePeriod, () => playerInfo.IsInGracePeriod = false);
    }

    private bool IsPlayerWhitelisted(CCSPlayerController player)
    {
        if (player.SteamID == 0) return false;

        string steamId64 = player.SteamID.ToString();
        return Config.Whitelist.Contains(steamId64);
    }

    private void CheckPings()
    {
        Logger.LogDebug("Checking player's pings");
        GetPlayers().ForEach(CheckPing);
    }

    private void CheckPing(CCSPlayerController player)
    {
        if (Config.EnableDebug)
            Logger.LogInformation("Name: {name}, Ping: {ping}, SteamID: {steamid}, Slot: {slot}", player.PlayerName, player.Ping, player.SteamID, player.Slot);

        if (!Slots.TryGetValue(player.Slot, out var playerInfo))
        {
            if (Config.EnableDebug)
            {
                Logger.LogError("Player {player} ({steamid}) PlayerInfo slot not found.", player.PlayerName, player.SteamID);
                Logger.LogInformation("Existing PlayerInfo slots...");
                foreach (var slot in Slots)
                {
                    Logger.LogInformation("      {slot}. ", slot.Key);
                }
            }
            return;
        }

        if (playerInfo.IsImmune)
        {
            if (Config.EnableDebug && playerInfo.IsWhitelisted)
            {
                Logger.LogInformation("Player {name} is whitelisted, skipping ping check", player.PlayerName);
            }
            return;
        }

        if (player.Ping > Config.MaxPing)
            HandleExcessivePing(player, playerInfo);

    }

    public void HandleExcessivePing(CCSPlayerController player, PlayerInfo playerInfo)
    {
        playerInfo.WarningsGiven++;
        if (playerInfo.WarningsGiven > Config.MaxWarnings)
        {
            Server.ExecuteCommand($"kickid {player.UserId}");
            if (Config.ShowPublicKickMessage)
            {
                var kickMessage = ParseMessageTemplate(player, playerInfo, Config.KickMessage);
                Server.PrintToChatAll(kickMessage);
            }
        }
        else
        {
            if (Config.ShowWarnings)
            {
                var warningMessage = ParseMessageTemplate(player, playerInfo, Config.WarningMessage);
                player.PrintToChat(warningMessage);
            }
        }
    }

    public string ParseMessageTemplate(CCSPlayerController player, PlayerInfo playerInfo, string message)
    {
        return message
            .Replace("{NAME}", player.PlayerName)
            .Replace("{WARN}", playerInfo.WarningsGiven.ToString())
            .Replace("{MAXWARN}", Config.MaxWarnings.ToString())
            .Replace("{PING}", player.Ping.ToString())
            .Replace("{MAXPING}", Config.MaxPing.ToString());
    }

    public override void Unload(bool hotReload)
    {
        foreach (var slot in Slots)
        {
            slot.Value?.Timer?.Kill();
        }
    }
}