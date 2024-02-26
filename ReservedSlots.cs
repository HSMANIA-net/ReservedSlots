using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

namespace ReservedSlots;

public class ReservedSlots : BasePlugin
{
    public override string ModuleName => "ReservedSlots";
    public override string ModuleAuthor => "unfortunate";
    public override string ModuleVersion => "1.0.1";

    public int MaxPlayers = 10;

    #region Commands
    [RequiresPermissions("@css/ban")]
    [ConsoleCommand("css_slot", "Frees place for admin with active VIP")]
    public void OnSlotCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null)
            return;
        
        if (!AdminManager.PlayerHasPermissions(player, "@css/vip"))
        {
            player.PrintToChat(Localizer["VipIsNeeded"]);
        }
        
        if (GetPlayersCount() <= MaxPlayers) {
            player.PrintToChat(Localizer["NotFull"]);
            return;
        }

        if (player.TeamNum != 1) {
            player.PrintToChat(Localizer["SpectatorsOnly"]);
            return;
        }

        var kickedPlayer = GetPlayerToKick(player);
        if (kickedPlayer != null)
        {
            Server.ExecuteCommand($"kickid {kickedPlayer.UserId}");
        }
    }
    #endregion

    #region Events
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (
            player.IsHLTV
            || player == null
            || !player.IsValid
            || player.Connected != PlayerConnectedState.PlayerConnected
            || player.SteamID.ToString().Length != 17
        )
            return HookResult.Continue;

        if (GetPlayersCount() <= MaxPlayers)
            return HookResult.Continue;

        AddTimer(
            1.0f,
            () =>
            {
                if (
                    AdminManager.PlayerHasPermissions(player, "@css/vip")
                    && !AdminManager.PlayerHasPermissions(player, "@css/ban")
                )
                {
                    var kickedPlayer = GetPlayerToKick(player);
                    if (kickedPlayer != null)
                    {
                        Server.ExecuteCommand($"kickid {kickedPlayer.UserId}");
                    }
                }
                else if (AdminManager.PlayerHasPermissions(player, "@css/ban"))
                {
                    player.ChangeTeam(CsTeam.Spectator);
                    player.PrintToChat(Localizer["SwitchedToSpec"]);
                }
                else
                {
                    Server.ExecuteCommand($"kickid {player.UserId}");
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE
        );

        return HookResult.Continue;
    }
    #endregion

    #region Functions
    private CCSPlayerController GetPlayerToKick(CCSPlayerController client)
    {
        var allPlayers = Utilities.GetPlayers();
        var playersList = allPlayers
            .Where(p =>
                p.IsValid
                && !p.IsHLTV
                && p.Connected == PlayerConnectedState.PlayerConnected
                && p.SteamID.ToString().Length == 17
                && p != client
                && !AdminManager.PlayerHasPermissions(p, "@css/ban")
                && !AdminManager.PlayerHasPermissions(p, "@css/vip")
            )
            .Select(player => (player, (int)player.Ping, player.Score))
            .ToList();

        CCSPlayerController player = null!;

        playersList = playersList.OrderBy(x => Guid.NewGuid()).ToList();
        player = playersList.FirstOrDefault().Item1;

        return player;
    }

    private static int GetPlayersCount()
    {
        return Utilities
            .GetPlayers()
            .Where(p =>
                p.IsValid
                && !p.IsHLTV
                && !p.IsBot
                && p.Connected == PlayerConnectedState.PlayerConnected
                && p.SteamID.ToString().Length == 17
            )
            .Count();
    }
    #endregion
}
