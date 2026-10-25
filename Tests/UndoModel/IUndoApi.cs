using System;
using System.Collections.Generic;
using System.Linq;
using Lizzie.Replication.Machinery;

namespace Lizzie.Tests;

/// <summary>
/// The undo API, so the same properties can check any implementation against another.
/// </summary>
public interface IUndoApi
{
    string Name { get; }

    UndoFlag Undo(Timeline timeline, byte me, TimelineUndo.Scope scope);

    UndoFlag Redo(Timeline timeline, byte me, TimelineUndo.Scope scope);

    List<SnowportId> OpenGroups(Timeline timeline, byte source);

    IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e);

    IEnumerable<TimelineEvent> InEffect(Timeline timeline);

    /// <summary>Whether the event's history entry is out of effect: for an action, whether it's undone.</summary>
    bool IsUndone(Timeline timeline, TimelineEvent e) => !InEffect(timeline).Contains(e);
}

/// <summary>The game's implementation, in Scripts/Events/TimelineUndo.cs.</summary>
public sealed class CurrentUndo : IUndoApi
{
    public string Name => "current";

    public UndoFlag Undo(Timeline timeline, byte me, TimelineUndo.Scope scope) =>
        TimelineUndo.Undo(timeline, me, scope);

    public UndoFlag Redo(Timeline timeline, byte me, TimelineUndo.Scope scope) =>
        TimelineUndo.Redo(timeline, me, scope);

    public List<SnowportId> OpenGroups(Timeline timeline, byte source) =>
        TimelineUndo.OpenGroups(timeline, source);

    public IEnumerable<TimelineEvent> Changes(Timeline timeline, TimelineEvent e) =>
        TimelineUndo.Changes(timeline, e);

    public IEnumerable<TimelineEvent> InEffect(Timeline timeline) =>
        TimelineUndo.InEffect(timeline);
}
