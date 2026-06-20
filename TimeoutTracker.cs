using CounterStrikeSharp.API.Modules.Utils;

namespace CS2ChatPause;


// Tracks remaining tactical timeouts by stable logical team
public class TimeoutTracker
{
    //* Default tactical timeouts per team in regulation
    public const int RegulationTimeouts = 3;

    //* Tactical timeouts granted to each team per overtime period
    public const int OvertimeTimeouts = 1;

    //* Timeouts of the team that started the match on CT
    private int _startCtTimeouts;

    //* Timeouts of the team that started the match on T
    private int _startTTimeouts;

    //* Last seen overtime index (0 = regulation); used to detect new overtimes
    private int _lastOvertime;

    //* True when physical sides are currently swapped from their match-start orientation
    private bool _sidesSwapped;

    // Resets the ledger for a brand new match
    public void ResetForMatch()
    {
        _startCtTimeouts = RegulationTimeouts;
        _startTTimeouts = RegulationTimeouts;
        _lastOvertime = 0;
        _sidesSwapped = false;
    }

    // True when the given physical side currently holds the team that started on CT
    private bool HoldsStartCtTeam(CsTeam team) => (team == CsTeam.CounterTerrorist) != _sidesSwapped;

    // Returns remaining timeouts for the given physical side
    public int Remaining(CsTeam team) => HoldsStartCtTeam(team) ? _startCtTimeouts : _startTTimeouts;

    // True when the given physical side still has at least one tactical timeout left
    public bool HasTimeout(CsTeam team) => Remaining(team) > 0;

    // Consumes one timeout for the physical side; returns false when none remain
    public bool TryConsume(CsTeam team)
    {
        if (!HasTimeout(team)) return false;

        if (HoldsStartCtTeam(team)) _startCtTimeouts--;
        else _startTTimeouts--;
        return true;
    }

    // Reconciles ledger with the current round state once per round start
    public void SyncRoundState(int roundsPlayed, int maxRounds, int overtimePlaying, int otMaxRounds)
    {
        //! New overtime period: refill both teams to one
        if (overtimePlaying > _lastOvertime)
        {
            _lastOvertime = overtimePlaying;
            _startCtTimeouts = OvertimeTimeouts;
            _startTTimeouts = OvertimeTimeouts;
        }

        //* Stateless mapping: recompute swap state from engine data every round so it self-corrects
        _sidesSwapped = IsSecondHalf(roundsPlayed, maxRounds, overtimePlaying, otMaxRounds);
    }

    private static bool IsSecondHalf(int roundsPlayed, int maxRounds, int overtimePlaying, int otMaxRounds)
    {
        if (overtimePlaying <= 0)
        {
            int half = maxRounds / 2;
            return half > 0 && roundsPlayed >= half;
        }

        //* Rounds consumed before the current overtime started
        int beforeOt = maxRounds + (overtimePlaying - 1) * otMaxRounds;
        int intoOt = roundsPlayed - beforeOt;
        int otHalf = otMaxRounds / 2;
        return otHalf > 0 && intoOt >= otHalf;
    }
}
