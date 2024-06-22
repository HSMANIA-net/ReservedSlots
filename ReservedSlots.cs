using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ReservedSlots;

public class ReservedQueueInfo
{
    public CCSPlayerController? PlayerToKick { get; set; }
    public CsTeam Team { get; set; }
    public CCSPlayerController? VipToSwitch { get; set; }
}

public class ReservedSlots : BasePlugin
{
    public override string ModuleName => "ReservedSlots";
    public override string ModuleAuthor => "unfortunate";
    public override string ModuleVersion => "1.0.4";
    public int MaxPlayers = 10;
    public Queue<ReservedQueueInfo> ReservedQueue = new Queue<ReservedQueueInfo>();

    public override void Load(bool hotReload)
    {
        ReservedQueue.Clear();
    }

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
            Logger.LogInformation($"[!slot] {player.PlayerName} does not have VIP access");
            return;
        }

        if (GetPlayersCount() <= MaxPlayers)
        {
            player.PrintToChat(Localizer["NotFull"]);
            Logger.LogInformation($"[!slot] {player.PlayerName} used at non-full server");
            return;
        }

        if (player.TeamNum != 1)
        {
            player.PrintToChat(Localizer["SpectatorsOnly"]);
            Logger.LogInformation($"[!slot] {player.PlayerName} is not spectator");
            return;
        }

        CCSPlayerController playerToKick = GetPlayerToKick(player);
        if (playerToKick == null)
        {
            player.PrintToChat(Localizer["ServerIsFull"]);
            return;
        }
        player.PrintToChat(Localizer["WillFreeSpace", playerToKick.PlayerName]);
        playerToKick.PrintToChat(Localizer["WillBeKicked"]);

        Logger.LogInformation($"[!slot] {player.PlayerName} will kick {playerToKick.PlayerName}");

        ReservedQueue.Enqueue(
            new ReservedQueueInfo
            {
                PlayerToKick = playerToKick,
                Team = (CsTeam)playerToKick.TeamNum,
                VipToSwitch = player
            }
        );
    }
    #endregion

    #region Events
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;

        if (
            player!.IsHLTV
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
                    player.ChangeTeam(CsTeam.Spectator);
                    CCSPlayerController playerToKick = GetPlayerToKick(player);

                    if (playerToKick == null)
                    {
                        player.PrintToChat(Localizer["ServerIsFull"]);
                        return;
                    }

                    player.PrintToChat(Localizer["WillFreeSpace", playerToKick.PlayerName]);
                    playerToKick.PrintToChat(Localizer["WillBeKicked"]);
                    Logger.LogInformation(
                        $"[Connect] {player.PlayerName} will kick {playerToKick.PlayerName}"
                    );

                    ReservedQueue.Enqueue(
                        new ReservedQueueInfo
                        {
                            PlayerToKick = playerToKick,
                            Team = (CsTeam)playerToKick.TeamNum,
                            VipToSwitch = player
                        }
                    );
                }
                else if (AdminManager.PlayerHasPermissions(player, "@css/ban"))
                {
                    player.ChangeTeam(CsTeam.Spectator);
                    player.PrintToChat(Localizer["SwitchedToSpec"]);
                    Logger.LogInformation($"[Connect] {player.PlayerName} switched to spectators");
                }
                else
                {
                    Server.ExecuteCommand($"kickid {player.UserId}");
                    Logger.LogInformation($"[Connect] {player.PlayerName} got kicked (NOT VIP)");
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        foreach (var reservedQueue in ReservedQueue.ToList())
        {
            var disconnectedPlayer = @event.Userid;

            if (disconnectedPlayer == reservedQueue.PlayerToKick)
            {
                ReservedQueue.Dequeue();
                reservedQueue.VipToSwitch!.ChangeTeam(reservedQueue.Team);
                Logger.LogInformation(
                    $"[Disconnect] {disconnectedPlayer!.PlayerName} was PlayerToKick"
                );
                Logger.LogInformation(
                    $"[Disconnect] {reservedQueue.VipToSwitch.PlayerName} has been switched to {(CsTeam)reservedQueue.Team}"
                );
            }
            else if (disconnectedPlayer == reservedQueue.VipToSwitch)
            {
                ReservedQueue.Dequeue();
                reservedQueue.PlayerToKick!.PrintToChat(
                    Localizer["Saved", reservedQueue.VipToSwitch!.PlayerName]
                );
                Logger.LogInformation(
                    $"[Disconnect] {reservedQueue.VipToSwitch.PlayerName} disconnected, saved {reservedQueue.PlayerToKick.PlayerName}"
                );
            }
            else if (disconnectedPlayer!.TeamNum > 1)
            {
                ReservedQueue.Dequeue();
                reservedQueue.VipToSwitch!.ChangeTeam((CsTeam)disconnectedPlayer.TeamNum);
                reservedQueue.PlayerToKick!.PrintToChat(
                    Localizer["Saved", disconnectedPlayer.PlayerName]
                );

                Logger.LogInformation(
                    $"[Disconnect] {disconnectedPlayer.PlayerName} disconnected, saved {reservedQueue.PlayerToKick.PlayerName}"
                );
                Logger.LogInformation(
                    $"[Disconnect] {reservedQueue.VipToSwitch.PlayerName} has been switched to {(CsTeam)disconnectedPlayer.Team}"
                );
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (ReservedQueue.Count < 1)
            return HookResult.Continue;

        AddTimer(
            1.0f,
            () =>
            {
                while (ReservedQueue.Count > 0)
                {
                    ReservedQueueInfo reservedQueue = ReservedQueue.Dequeue();
                    Server.ExecuteCommand($"kickid {reservedQueue.PlayerToKick!.UserId}");
                    Logger.LogInformation(
                        $"[OnRoundEnd] {reservedQueue.PlayerToKick.PlayerName} has been kicked"
                    );
                    reservedQueue.VipToSwitch!.ChangeTeam(reservedQueue.Team);
                    Logger.LogInformation(
                        $"[OnRoundEnd] {reservedQueue.VipToSwitch.PlayerName} has been switched to {(CsTeam)reservedQueue.Team}"
                    );
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
                && !IsInQueue(p)
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

    private bool IsInQueue(CCSPlayerController Player)
    {
        if (ReservedQueue.Count < 1)
            return false;

        foreach (var reservedQueue in ReservedQueue.ToList())
        {
            if (Player == reservedQueue.PlayerToKick)
            {
                return true;
            }
        }

        return false;
    }
    #endregion
}
