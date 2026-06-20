using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace CS2ChatPause;

//* Current pause kind held by the controller
public enum PauseKind { None, Tactical, Technical }

public class PauseController
{
    //* Tactical pause duration in seconds
    public const int TacticalSeconds = 30;

    private readonly BasePlugin _plugin;

    //* Probe returning true once the match is actually frozen (waiting for resume)
    private readonly Func<bool> _isMatchPaused;

    public PauseKind Kind { get; private set; } = PauseKind.None;

    //* Side that requested the active tactical pause (None for technical)
    private CsTeam _pausedTeam = CsTeam.None;

    //* True once the pause has actually engaged and the HUD/countdown are live
    private bool _active;

    private int _remaining;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _countdown;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _watcher;

    public PauseController(BasePlugin plugin, Func<bool> isMatchPaused)
    {
        _plugin = plugin;
        _isMatchPaused = isMatchPaused;
    }

    public bool IsPaused => Kind != PauseKind.None;

    // Requests a tactical pause; the countdown starts only once the match
    // actually freezes (next round if issued mid-round)
    public void StartTactical(CsTeam team)
    {
        Kind = PauseKind.Tactical;
        _pausedTeam = team;
        _remaining = TacticalSeconds;
        _active = false;

        Server.ExecuteCommand("mp_pause_match");
        StartHud();
        BeginWatch();
    }

    // Requests an admin technical pause with no time limit
    public void StartTechnical()
    {
        Kind = PauseKind.Technical;
        _pausedTeam = CsTeam.None;
        _remaining = 0;
        _active = false;

        Server.ExecuteCommand("mp_pause_match");
        StartHud();
        BeginWatch();
    }

    // Polls for the real pause to engage, then activates HUD countdown
    private void BeginWatch()
    {
        _watcher = _plugin.AddTimer(0.25f, () =>
        {
            if (_active || Kind == PauseKind.None) return;
            if (!_isMatchPaused()) return;

            _active = true;
            if (Kind == PauseKind.Tactical) StartCountdown();
        }, TimerFlags.REPEAT);
    }

    // Starts the per-second tactical countdown that auto-resumes at zero
    private void StartCountdown()
    {
        _countdown = _plugin.AddTimer(1.0f, () =>
        {
            _remaining--;
            if (_remaining <= 0)
                Server.NextFrame(Resume);
        }, TimerFlags.REPEAT);
    }

    // Unpauses the match and clears all pause state and timers
    public void Resume()
    {
        //* Guard against double-resume (manual .un racing the auto countdown)
        if (Kind == PauseKind.None) return;

        Server.ExecuteCommand("mp_unpause_match");
        Kind = PauseKind.None;
        _pausedTeam = CsTeam.None;
        _remaining = 0;
        _active = false;

        _countdown?.Kill();
        _countdown = null;
        _hudTimer?.Kill();
        _hudTimer = null;
        _watcher?.Kill();
        _watcher = null;
    }

    // Kills timers without touching match state (used on plugin unload)
    public void StopTimers()
    {
        _countdown?.Kill();
        _countdown = null;
        _hudTimer?.Kill();
        _hudTimer = null;
        _watcher?.Kill();
        _watcher = null;
    }

    // Starts the repeating HUD refresh that shows current pause status
    private void StartHud()
    {
        DrawHud();
        _hudTimer = _plugin.AddTimer(1.0f, DrawHud, TimerFlags.REPEAT);
    }

    // Renders the center status HUD plus the separate countdown alert box
    private void DrawHud()
    {
        string html = BuildHudText();
        string? alert = BuildAlertText();
        foreach (var p in Utilities.GetPlayers())
        {
            if (p is not { IsValid: true, IsBot: false }) continue;

            p.PrintToCenterHtml(html);
            //* Countdown lives in its own alert box, separate from the status HUD
            if (alert != null) p.PrintToCenterAlert(alert);
        }
    }

    // Builds the colored HUD status string for the current pause kind / phase
    private string BuildHudText()
    {
        //* Pending: pause requested mid-round, not yet frozen
        if (!_active)
            return "<font color='#ffffff'>已申请暂停</font>" +
                   "<br><font color='#ffffff'>将在下回合开始时生效</font>";

        return "<font color='#ffffff'>比赛已暂停</font>";
    }

    // Builds the separate alert-box line; null when nothing extra to show
    private string? BuildAlertText()
    {
        if (!_active || Kind != PauseKind.Tactical) return null;
        return $"暂停时间剩余 {_remaining}秒，输入 .un 解除暂停";
    }
}


