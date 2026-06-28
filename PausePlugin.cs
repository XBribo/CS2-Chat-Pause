using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace CS2ChatPause;

/**
 * Chat-driven match pause plugin: .p/.pause for a 30s tactical timeout
 * (3 per team, +1 each overtime), .tech for an unlimited admin technical
 * pause, and .un / .untech to resume.
 */
public class PausePlugin : BasePlugin
{
    public override string ModuleName => "CS2 Chat Pause";
    public override string ModuleVersion => "0.1.3";
    public override string ModuleAuthor => "XBribo(๑•.•๑)";

    //* Admin permission required for technical pause commands
    private const string TechPermission = "@css/generic";

    private readonly TimeoutTracker _tracker = new();
    private PauseController _pause = null!;

    public override void Load(bool hotReload)
    {
        _pause = new PauseController(this, IsMatchPaused);
        _tracker.ResetForMatch();

        AddCommandListener("say", OnSay);
        AddCommandListener("say_team", OnSay);

        // Reliable per-map reset: fires on every new map / match reload
        RegisterListener<Listeners.OnMapStart>(_ => _tracker.ResetForMatch());

        // Sync timeout ledger with halftime / overtime transitions
        RegisterEventHandler<EventRoundAnnounceMatchStart>(OnMatchStart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    public override void Unload(bool hotReload)
    {
        _pause.StopTimers();
    }

    // Resets timeout counts to 3 per team when a fresh match begins
    private HookResult OnMatchStart(EventRoundAnnounceMatchStart @event, GameEventInfo info)
    {
        _tracker.ResetForMatch();
        return HookResult.Continue;
    }

    // Each round start, reconcile the ledger with halftime / overtime state
    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        var rules = GetGameRules();
        if (rules == null) return HookResult.Continue;

        int maxRounds = ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24;
        int otMax = ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 6;

        _tracker.SyncRoundState(rules.TotalRoundsPlayed, maxRounds, rules.OvertimePlaying, otMax);
        return HookResult.Continue;
    }

    // Finds the live CCSGameRules instance, or null if not available yet
    private static CCSGameRules? GetGameRules()
    {
        var proxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return proxy?.GameRules;
    }

    // True only once the pause actually freezes the match at a round's freeze time
    private bool IsMatchPaused()
    {
        var rules = GetGameRules();
        if (rules == null) return false;

        //! MatchWaitingForResume flips true the instant mp_pause_match is issued (still mid-round);
        //! the real freeze only lands during a round's freeze period, so require both
        return rules.MatchWaitingForResume && rules.FreezePeriod;
    }

    private HookResult OnSay(CCSPlayerController? player, CommandInfo info)
    {
        if (player is not { IsValid: true }) return HookResult.Continue;

        string raw = info.GetArg(1).Trim();
        if (raw.Length < 2 || (raw[0] != '.' && raw[0] != '!')) return HookResult.Continue;

        switch (raw[1..].Trim().ToLowerInvariant())
        {
            case "p":
            case "pause":
                HandlePause(player);
                return HookResult.Handled;
            case "un":
            case "unpause":
                HandleUnpause(player);
                return HookResult.Handled;
            case "tech":
                HandleTech(player);
                return HookResult.Handled;
            case "untech":
                HandleUntech(player);
                return HookResult.Handled;
            default:
                return HookResult.Continue;
        }
    }

    // .p / .pause: start a 30s tactical pause for the caller's team
    private void HandlePause(CCSPlayerController player)
    {
        var team = (CsTeam)player.TeamNum;
        if (team != CsTeam.CounterTerrorist && team != CsTeam.Terrorist)
        {
            player.PrintToChat($"{Tag}只有在场玩家才能申请暂停");
            return;
        }

        if (_pause.IsPaused)
        {
            player.PrintToChat($"{Tag}当前已处于暂停状态");
            return;
        }

        if (!_tracker.HasTimeout(team))
        {
            player.PrintToChat($"{Tag}暂停次数已用完");
            return;
        }

        _tracker.TryConsume(team);
        _pause.StartTactical(team);

        //* Name colored by side: T yellow, CT blue; remaining count in green
        char nameColor = team == CsTeam.Terrorist ? ChatColors.Yellow : ChatColors.Blue;
        Server.PrintToChatAll($"{Tag}{nameColor}{player.PlayerName}{ChatColors.Default} 暂停了比赛，暂停剩余 {ChatColors.Green}{_tracker.Remaining(team)}{ChatColors.Default} 次。");
    }

    // .un / .unpause: immediately resume a tactical pause
    private void HandleUnpause(CCSPlayerController player)
    {
        if (_pause.Kind != PauseKind.Tactical)
        {
            string hint = _pause.Kind == PauseKind.Technical ? "当前是技术暂停,请用 .untech 解除" : "当前没有暂停";
            player.PrintToChat($"{Tag}{hint}");
            return;
        }

        _pause.Resume();
        Server.PrintToChatAll($"{Tag}暂停已解除");
    }

    // .tech: start an unlimited technical pause (admins only)
    private void HandleTech(CCSPlayerController player)
    {
        if (!AdminManager.PlayerHasPermissions(player, TechPermission))
        {
            player.PrintToChat($"{Tag}只有管理员才能使用技术暂停");
            return;
        }

        if (_pause.IsPaused)
        {
            player.PrintToChat($"{Tag}当前已处于暂停状态");
            return;
        }

        _pause.StartTechnical();
        Server.PrintToChatAll($"{Tag}管理员发起技术暂停");
    }

    // .untech: resume from a technical pause (admins only)
    private void HandleUntech(CCSPlayerController player)
    {
        if (!AdminManager.PlayerHasPermissions(player, TechPermission))
        {
            player.PrintToChat($"{Tag}只有管理员才能解除技术暂停");
            return;
        }

        if (_pause.Kind != PauseKind.Technical)
        {
            player.PrintToChat($"{Tag}当前没有技术暂停");
            return;
        }

        _pause.Resume();
        Server.PrintToChatAll($"{Tag}技术暂停已解除");
    }

    //* Chat prefix shown before every plugin message
    private static string Tag => $" [{ChatColors.Green}Pause{ChatColors.Default}] ";
}




