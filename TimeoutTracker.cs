using CounterStrikeSharp.API.Modules.Utils;

namespace CS2ChatPause;


// Tracks remaining tactical timeouts per side, swaps counts on half change, and resets to one on each overtime period.
public class TimeoutTracker
{
    //* Default tactical timeouts per team in regulation
    public const int RegulationTimeouts = 3;

    //* Tactical timeouts granted to each team per overtime period
    public const int OvertimeTimeouts = 1;

    private int _ctTimeouts;
    private int _tTimeouts;

    //* Last seen overtime index (0 = regulation); used to detect new overtimes
    private int _lastOvertime = -1;

    //* Last seen half parity; used to detect side swaps at halftime
    private bool _initialized;
    private bool _secondHalfParity;

    // Resets the ledger for a brand new match
    public void ResetForMatch()
    {
        _ctTimeouts = RegulationTimeouts;
        _tTimeouts = RegulationTimeouts;
        _lastOvertime = 0;
        _initialized = false;
    }

    // Returns remaining timeouts for the given side
    public int Remaining(CsTeam team)
    {
        return team == CsTeam.CounterTerrorist ? _ctTimeouts : _tTimeouts;
    }

    // True when the given side still has at least one tactical timeout left
    public bool HasTimeout(CsTeam team) => Remaining(team) > 0;

    // Consumes one timeout for the side; returns false when none remain
    public bool TryConsume(CsTeam team)
    {
        if (!HasTimeout(team)) return false;

        if (team == CsTeam.CounterTerrorist) _ctTimeouts--;
        else _tTimeouts--;
        return true;
    }

    // Swaps the two side counters so counts follow teams across a side switch
    private void SwapSides()
    {
        (_ctTimeouts, _tTimeouts) = (_tTimeouts, _ctTimeouts);
    }

    // Reconciles ledger with the current round state once per round start
    public void SyncRoundState(int roundsPlayed, int maxRounds, int overtimePlaying, int otMaxRounds)
    {
        //! New overtime period: reset both teams to one and skip swap logic
        if (overtimePlaying > _lastOvertime)
        {
            _lastOvertime = overtimePlaying;
            _ctTimeouts = OvertimeTimeouts;
            _tTimeouts = OvertimeTimeouts;
            _initialized = true;
            _secondHalfParity = IsSecondHalf(roundsPlayed, maxRounds, overtimePlaying, otMaxRounds);
            return;
        }

        bool secondHalf = IsSecondHalf(roundsPlayed, maxRounds, overtimePlaying, otMaxRounds);

        //* First sync of this phase just records parity without swapping
        if (!_initialized)
        {
            _initialized = true;
            _secondHalfParity = secondHalf;
            return;
        }

        // Parity flipped means a side switch happened; move counts with the teams
        if (secondHalf != _secondHalfParity)
        {
            SwapSides();
            _secondHalfParity = secondHalf;
        }
    }

    /**
     * Determines whether the current round belongs to the second half of its
     * phase. In regulation the swap is at maxRounds/2; in overtime at otMax/2.
     */
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


