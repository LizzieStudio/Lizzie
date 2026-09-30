using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// Undo and redo as one player sees them, the way Emacs defines them, knowing nothing about logs,
/// flags or groups. It's the second model the implementations are checked against: where the naive
/// model restates the rules, this one says what the rules must add up to.
/// </summary>
/// <remarks>
/// Every state the player's work has been in is kept, oldest first. A run of reversals moves a pointer
/// back and forth over that history as it stood when the run began, without adding to it. The player's
/// next change ends the run: the states the run walked back through and didn't redo are added to the end,
/// in the order it walked through them, and then the new state. So a later run can walk back through them
/// too, and none of the back and forth that redo cancelled is kept.
/// </remarks>
public sealed class TextbookUndo<TChange>
{
    private readonly List<ImmutableHashSet<TChange>> _history = [ImmutableHashSet<TChange>.Empty];

    // the end of history when the run of reversals began, or -1 outside a run
    private int _runEnd = -1;

    // where the run has got to
    private int _at;

    /// <summary>Which of the player's changes are in effect.</summary>
    public ImmutableHashSet<TChange> State => _runEnd < 0 ? _history[^1] : _history[_at];

    /// <summary>The player makes a change, which ends any run of reversals.</summary>
    public void Change(TChange change)
    {
        var state = State;
        if (_runEnd >= 0)
            for (int k = _runEnd - 1; k >= _at; k--)
                _history.Add(_history[k]);

        _history.Add(state.Add(change));
        _runEnd = -1;
    }

    /// <summary>One step back through history. False if the run is at the start.</summary>
    public bool Undo()
    {
        if (_runEnd < 0)
            _runEnd = _at = _history.Count - 1;
        if (_at == 0)
            return false;

        _at--;
        return true;
    }

    /// <summary>One step forward again. False outside a run, or at its end.</summary>
    public bool Redo()
    {
        if (_runEnd < 0 || _at == _runEnd)
            return false;

        _at++;
        return true;
    }
}
