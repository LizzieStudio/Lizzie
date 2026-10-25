using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lizzie.Replication.Machinery;

namespace Lizzie.Tests;

/// <summary>An atom for sessions to write. Faces are unique, so every write can be told apart.</summary>
public sealed record TestAtom : Atom
{
    public required SnowTag Id { get; init; }

    public int Face { get; init; }
}

/// <summary>A singleton atom for sessions to write, like the project settings.</summary>
public sealed record TestValue : Atom
{
    public string Text { get; init; }
}

/// <summary>Whose changes a scope takes.</summary>
public enum ScopeKind
{
    Mine,
    Others,
    Anyone,
}

/// <summary>
/// An undo scope, as a view or command would give one: whose changes it takes, and which atoms,
/// by the bits of <paramref name="Mask"/>. A mask of 0 takes every atom, the singleton included.
/// </summary>
public sealed record TestScope(ScopeKind Kind, int Mask)
{
    public TimelineUndo.Scope For(byte me) =>
        (author, events) =>
            Kind switch
            {
                ScopeKind.Mine => author == me,
                ScopeKind.Others => author != me,
                _ => true,
            }
            && events.Any(e =>
                e.Writes.Concat(e.Unsets)
                    .Any(atom => Mask == 0 || atom is TestAtom r && (Mask & (1 << (r.Id - 1))) != 0)
            );

    public override string ToString() => Mask == 0 ? $"{Kind}" : $"{Kind} atoms {Mask}";
}

/// <summary>Something a player does, played out by <see cref="UndoSession"/>.</summary>
public abstract record Op
{
    /// <summary>
    /// An action writing atoms <paramref name="First"/> and <paramref name="Second"/> (0 for none)
    /// and <paramref name="Values"/> writes to the singleton. It joins the player's gesture, if one is going.
    /// </summary>
    public sealed record Act(byte Source, int First, int Second, int Values) : Op;

    /// <summary>Starts a gesture, closing one left open first, as the synchronizer does.</summary>
    public sealed record Start(byte Source, int Atom) : Op;

    /// <summary>Ends the player's gesture with a drop writing <paramref name="Atom"/>, or with a close-only event for 0.</summary>
    public sealed record End(byte Source, int Atom) : Op;

    /// <summary>Reversals Undo or Redo. The expected implementation decides the flag.</summary>
    public sealed record Reversal(byte Source, bool Redo, TestScope Scope) : Op;

    /// <summary>Cancels the player's gesture, if one is going.</summary>
    public sealed record Cancel(byte Source) : Op;

    /// <summary>
    /// A whole gesture that's canceled: its first event, <paramref name="Moves"/> more, and the cancel,
    /// all but the cancel writing <paramref name="Atom"/>. Closes a gesture left open first.
    /// </summary>
    public sealed record CanceledGesture(byte Source, int Atom, int Moves) : Op;

    /// <summary>The player leaves, and the host cancels their open gestures.</summary>
    public sealed record Leave(byte Source) : Op;

    /// <summary>
    /// A reversal no Undo or Redo would write, reversing whatever <paramref name="Pick"/> lands on:
    /// a unit, an event that isn't one, or a missing id.
    /// </summary>
    public sealed record RawFlag(byte Source, int Pick, bool ByRedo) : Op;

    /// <summary>
    /// A whole gesture: its first event, <paramref name="Moves"/> more, and the drop that closes it,
    /// all writing <paramref name="Atom"/>. Closes a gesture left open first.
    /// </summary>
    public sealed record Gesture(byte Source, int Atom, int Moves) : Op;

    /// <summary>An action that arrives late, with an id <paramref name="Back"/> steps older than the newest.</summary>
    public sealed record Late(byte Source, int Atom, int Back) : Op;

    /// <summary>
    /// Reversals Undo or Redo before other players' events among the newest <paramref name="Back"/>
    /// have arrived, so the reversal is decided without them, as when players press at the same time.
    /// It sorts after everything the player has seen, and after the oldest <paramref name="After"/>
    /// of the events they haven't, as when those players' clocks were behind.
    /// </summary>
    public sealed record Concurrent(byte Source, bool Redo, TestScope Scope, int Back, int After)
        : Op;
}

/// <summary>
/// A timeline built by playing out operations, the way the game would build it.
/// </summary>
public sealed class UndoSession
{
    public const byte Host = Snowport.HostSource;
    public const int AtomCount = 4;
    public static readonly byte[] Sources = [1, 2, 3, 4];

    public readonly Timeline Timeline = new();

    // decides undos, redos and which gestures a leaving player left open
    private readonly IUndoApi _driver;

    // each player's open gesture
    private readonly Dictionary<byte, SnowportId> _open = new();

    // Events take even clock values, so late events can take the odd ones between.
    private long _clock;

    // the events the operation being applied has recorded
    private readonly List<TimelineEvent> _recorded = new();

    public UndoSession(IUndoApi driver) => _driver = driver;

    /// <summary>
    /// Plays out <paramref name="ops"/>, with <paramref name="driver"/> deciding what reversals and departures write.
    /// <paramref name="before"/> sees the session before each operation.
    /// </summary>
    public static UndoSession Play(
        IEnumerable<Op> ops,
        IUndoApi driver,
        Action<UndoSession, Op> before = null
    )
    {
        var session = new UndoSession(driver);
        foreach (var op in ops)
        {
            before?.Invoke(session, op);
            session.Apply(op);
        }
        return session;
    }

    /// <summary>Plays out one operation, and returns the events it recorded, oldest first.</summary>
    public IReadOnlyList<TimelineEvent> Apply(Op op)
    {
        _recorded.Clear();
        switch (op)
        {
            case Op.Act a:
                Record(
                    new TimelineEvent
                    {
                        Id = NextId(a.Source),
                        Group = _open.GetValueOrDefault(a.Source),
                    },
                    a.First,
                    a.Second,
                    a.Values
                );
                break;

            case Op.Gesture g:
                CloseOpen(g.Source);
                var head = NextId(g.Source);
                Record(new TimelineEvent { Id = head, Group = head }, g.Atom);
                for (int i = 0; i < g.Moves; i++)
                    Record(new TimelineEvent { Id = NextId(g.Source), Group = head }, g.Atom);
                Record(
                    new TimelineEvent
                    {
                        Id = NextId(g.Source),
                        Group = head,
                        End = GroupEnd.Closed,
                    },
                    g.Atom
                );
                break;

            case Op.CanceledGesture c:
                CloseOpen(c.Source);
                var canceledHead = NextId(c.Source);
                Record(new TimelineEvent { Id = canceledHead, Group = canceledHead }, c.Atom);
                for (int i = 0; i < c.Moves; i++)
                    Record(
                        new TimelineEvent { Id = NextId(c.Source), Group = canceledHead },
                        c.Atom
                    );
                Record(CancelOnly(canceledHead, c.Source));
                break;

            case Op.Cancel c when _open.Remove(c.Source, out var canceled):
                Record(CancelOnly(canceled, c.Source));
                break;

            case Op.Start s:
                CloseOpen(s.Source);
                var first = NextId(s.Source);
                Record(new TimelineEvent { Id = first, Group = first }, s.Atom);
                _open[s.Source] = first;
                break;

            case Op.End e when _open.Remove(e.Source, out var group):
                Record(
                    new TimelineEvent
                    {
                        Id = NextId(e.Source),
                        Group = group,
                        End = GroupEnd.Closed,
                    },
                    e.Atom
                );
                break;

            case Op.Reversal p:
                var flag = Decide(Timeline, p.Source, p.Redo, p.Scope);
                if (flag != null)
                    Record(new TimelineEvent { Id = NextId(p.Source), Undo = flag });
                break;

            case Op.Concurrent c:
                var unseen = Timeline
                    .Keys.Skip(Math.Max(0, Timeline.Count - c.Back))
                    .Where(key => key.source != c.Source)
                    .ToList();
                var seen = new Timeline();
                foreach (var (key, known) in Timeline)
                    if (!unseen.Contains(key))
                        seen.Add(key, known);

                var concurrent = Decide(seen, c.Source, c.Redo, c.Scope);
                if (concurrent == null)
                    break;
                // HLC: newer than everything the player has seen, and than the oldest After they haven't.
                var after = seen.Count > 0 ? seen.GetAt(seen.Count - 1).Key : SnowportId.Empty;
                foreach (var key in unseen.Take(c.After))
                    if (key.CompareTo(after) > 0)
                        after = key;
                var reversalId = Id(after.logicClock + 1, c.Source);
                if (Timeline.ContainsKey(reversalId))
                    break;
                Record(new TimelineEvent { Id = reversalId, Undo = concurrent });
                // Everything after is newer, as the player's clock has moved on too.
                while (_clock < reversalId.logicClock)
                    _clock += 2;
                break;

            case Op.Leave l when l.Source != Host:
                foreach (var open in _driver.OpenGroups(Timeline, l.Source))
                    Record(CancelOnly(open, Host));
                _open.Remove(l.Source);
                break;

            case Op.RawFlag r when Timeline.Count > 0:
                var picked = Timeline.GetAt(r.Pick / 4 % Timeline.Count).Value;
                var reverses = (r.Pick % 4) switch
                {
                    0 => picked.Id,
                    1 => Id(_clock + 1, 7), // never used: sources are 0 to 4
                    _ => picked.Unit,
                };
                Record(
                    new TimelineEvent
                    {
                        Id = NextId(r.Source),
                        Undo = new UndoFlag { Reverses = reverses, ByRedo = r.ByRedo },
                    }
                );
                break;

            case Op.Late l when 2 * l.Back - 1 < _clock:
                var id = Id(_clock - (2 * l.Back - 1), l.Source);
                if (Timeline.ContainsKey(id))
                    break;
                // It joins the player's gesture only if it's newer than the gesture's first event.
                var joins =
                    _open.TryGetValue(l.Source, out var gesture) && gesture.CompareTo(id) < 0;
                Record(
                    new TimelineEvent { Id = id, Group = joins ? gesture : SnowportId.Empty },
                    l.Atom
                );
                break;
        }
        return _recorded.ToArray();
    }

    private UndoFlag Decide(Timeline timeline, byte source, bool redo, TestScope scope) =>
        redo
            ? _driver.Redo(timeline, source, scope.For(source))
            : _driver.Undo(timeline, source, scope.For(source));

    private void CloseOpen(byte source)
    {
        if (_open.Remove(source, out var group))
            Record(CloseOnly(group, source));
    }

    private TimelineEvent CloseOnly(SnowportId group, byte source) =>
        new()
        {
            Id = NextId(source),
            Group = group,
            End = GroupEnd.Closed,
        };

    private TimelineEvent CancelOnly(SnowportId group, byte source) =>
        new()
        {
            Id = NextId(source),
            Group = group,
            End = GroupEnd.Canceled,
        };

    private SnowportId NextId(byte source)
    {
        _clock += 2;
        return Id(_clock, source);
    }

    private static SnowportId Id(long clock, byte source) => new(clock, source);

    /// <summary>
    /// Records the event in id order, with the given atoms and values written.
    /// Every write gets a unique face or value, so the one that wins can be told apart.
    /// </summary>
    private void Record(TimelineEvent e, int first = 0, int second = 0, int values = 0)
    {
        var atoms = new List<Atom>();
        foreach (var atom in new[] { first, second }.Where(r => r != 0))
            atoms.Add(new TestAtom { Id = atom, Face = (int)(long)e.Id * 10 + atoms.Count });
        for (int i = 0; i < values; i++)
            atoms.Add(new TestValue { Text = $"{e.Id}.{atoms.Count}" });
        if (atoms.Count > 0)
            e.Writes = atoms.ToArray();

        int at = Timeline.Count;
        while (at > 0 && Timeline.GetAt(at - 1).Key.CompareTo(e.Id) > 0)
            at--;
        Timeline.Insert(at, e.Id, e);
        _recorded.Add(e);
    }

    #region Descriptions for failures

    /// <summary>An event's name in <see cref="Dump"/>: its place in the timeline.</summary>
    public string Name(SnowportId id) =>
        Timeline.IndexOf(id) is var i and >= 0 ? $"#{i}" : $"?{id}";

    public string Describe(UndoFlag flag) =>
        flag == null
            ? "nothing"
            : $"{(flag.ByRedo ? "redo" : "undo")} reversing {Name(flag.Reverses)}"
                + (
                    flag.Also is { Length: > 0 } also
                        ? $" and {string.Join(", ", also.Order().Select(Name))}"
                        : ""
                );

    /// <summary>The timeline, one event per line, oldest first.</summary>
    public string Dump() =>
        string.Join(
            "\n",
            Timeline.Values.Select(e =>
            {
                string group = e.Group == SnowportId.Empty ? "" : $" in {Name(e.Group)}";
                string close =
                    e.End == GroupEnd.Canceled ? " cancel"
                    : e.End == GroupEnd.Closed ? " close"
                    : "";
                string what = e.Undo is { } f
                    ? Describe(f)
                    : string.Join(
                        " ",
                        e.Writes.Select(atom =>
                            atom switch
                            {
                                TestAtom r => $"r{r.Id}={r.Face}",
                                TestValue v => $"v={v.Text}",
                                _ => atom.GetType().Name,
                            }
                        )
                    );
                return $"  {Name(e.Id)} s{e.Id.source}{group}{close}: {what}";
            })
        );

    #endregion
}

/// <summary>Generators for random sessions.</summary>
public static class UndoSessions
{
    private static readonly Gen<byte> Source = Gen.Byte[1, 4];
    private static readonly Gen<int> Atom = Gen.Int[1, UndoSession.AtomCount];
    private static readonly Gen<int> MaybeAtom = Gen.Int[0, UndoSession.AtomCount];

    public static readonly Gen<TestScope> Scope = Gen.Select(
        Gen.OneOfConst(ScopeKind.Mine, ScopeKind.Others, ScopeKind.Anyone),
        Gen.Int[0, 15],
        (kind, mask) => new TestScope(kind, mask)
    );

    private static readonly Gen<Op> Acts = Gen.Select(
        Source,
        MaybeAtom,
        MaybeAtom,
        Gen.Int[0, 2],
        (s, first, second, values) => (Op)new Op.Act(s, first, second, values)
    );

    private static readonly Gen<Op> Starts = Gen.Select(
        Source,
        Atom,
        (s, atom) => (Op)new Op.Start(s, atom)
    );

    private static readonly Gen<Op> Ends = Gen.Select(
        Source,
        MaybeAtom,
        (s, atom) => (Op)new Op.End(s, atom)
    );

    private static readonly Gen<Op> Reversals = Gen.Select(
        Source,
        Gen.Bool,
        Scope,
        (s, redo, scope) => (Op)new Op.Reversal(s, redo, scope)
    );

    private static readonly Gen<Op> Leaves = Gen.Byte[2, 4].Select(s => (Op)new Op.Leave(s));

    private static readonly Gen<Op> Cancels = Source.Select(s => (Op)new Op.Cancel(s));

    private static readonly Gen<Op> CanceledGestures = Gen.Select(
        Source,
        Atom,
        Gen.Int[0, 2],
        (s, atom, moves) => (Op)new Op.CanceledGesture(s, atom, moves)
    );

    private static readonly Gen<Op> RawFlags = Gen.Select(
        Source,
        Gen.Int[0, 1000],
        Gen.Bool,
        (s, pick, byRedo) => (Op)new Op.RawFlag(s, pick, byRedo)
    );

    private static readonly Gen<Op> Gestures = Gen.Select(
        Source,
        Atom,
        Gen.Int[0, 2],
        (s, atom, moves) => (Op)new Op.Gesture(s, atom, moves)
    );

    private static readonly Gen<Op> Lates = Gen.Select(
        Source,
        Atom,
        Gen.Int[1, 5],
        (s, atom, back) => (Op)new Op.Late(s, atom, back)
    );

    private static readonly Gen<Op> Concurrents = Gen.Select(
        Source,
        Gen.Bool,
        Scope,
        Gen.Int[1, 4],
        Gen.Int[0, 4],
        (s, redo, scope, back, after) => (Op)new Op.Concurrent(s, redo, scope, back, after)
    );

    /// <summary>Sessions with everything players can do.</summary>
    public static readonly Gen<Op[]> WellFormed = Gen.Frequency(
        (30, Acts),
        (5, Gestures),
        (10, Starts),
        (10, Ends),
        (4, Cancels),
        (3, CanceledGestures),
        (30, Reversals),
        (10, Concurrents),
        (3, Leaves),
        (7, Lates)
    ).Array[0, 30];

    /// <summary>Sessions with everything players can do, and reversals no Undo or Redo would write.</summary>
    public static readonly Gen<Op[]> All = Gen.Frequency(
        (30, Acts),
        (5, Gestures),
        (10, Starts),
        (10, Ends),
        (4, Cancels),
        (3, CanceledGestures),
        (30, Reversals),
        (10, Concurrents),
        (3, Leaves),
        (5, RawFlags),
        (7, Lates)
    ).Array[0, 30];

    private static readonly Gen<byte> Players = Gen.Byte[1, 3];
    private static readonly Gen<TestScope> WholeScope = Gen.OneOfConst(
        new TestScope(ScopeKind.Mine, 0),
        new TestScope(ScopeKind.Others, 0),
        new TestScope(ScopeKind.Anyone, 0)
    );

    /// <summary>
    /// Three players acting and pressing, often at the same time, on few atoms,
    /// so their reversals often land on the same things.
    /// </summary>
    public static readonly Gen<Op[]> Simultaneous = Gen.Frequency(
        (20, Gen.Select(Players, Gen.Int[1, 2], (s, r) => (Op)new Op.Act(s, r, 0, 0))),
        (
            30,
            Gen.Select(
                Players,
                Gen.Bool,
                WholeScope,
                (s, redo, scope) => (Op)new Op.Reversal(s, redo, scope)
            )
        ),
        (
            30,
            Gen.Select(
                Players,
                Gen.Bool,
                WholeScope,
                Gen.Int[1, 3],
                Gen.Int[0, 3],
                (s, redo, scope, back, after) => (Op)new Op.Concurrent(s, redo, scope, back, after)
            )
        )
    ).Array[0, 16];

    /// <summary>Sessions that never undo anything.</summary>
    public static readonly Gen<Op[]> WithoutFlags = Gen.Frequency(
        (30, Acts),
        (10, Starts),
        (10, Ends),
        (7, Lates)
    ).Array[0, 30];

    /// <summary>Sessions without gestures.</summary>
    public static readonly Gen<Op[]> Ungrouped = Gen.Frequency(
        (30, Acts),
        (30, Reversals),
        (7, Lates)
    ).Array[0, 30];

    /// <summary>The player single-player sessions are about.</summary>
    public const byte Me = 1;

    private static readonly Gen<Op> MyActs = Atom.Select(r => (Op)new Op.Act(Me, r, 0, 0));
    private static readonly Gen<Op> OthersActs = Atom.Select(r => (Op)new Op.Act(2, r, 0, 0));
    private static readonly Gen<Op> MyGestures = Gen.Select(
        Atom,
        Gen.Int[0, 2],
        (atom, moves) => (Op)new Op.Gesture(Me, atom, moves)
    );
    private static readonly Gen<Op> MyCanceledGestures = Gen.Select(
        Atom,
        Gen.Int[0, 2],
        (atom, moves) => (Op)new Op.CanceledGesture(Me, atom, moves)
    );
    private static readonly Gen<Op> MyUndos = Gen.Const(
        (Op)new Op.Reversal(Me, false, new TestScope(ScopeKind.Mine, 0))
    );
    private static readonly Gen<Op> MyRedos = Gen.Const(
        (Op)new Op.Reversal(Me, true, new TestScope(ScopeKind.Mine, 0))
    );

    /// <summary>
    /// One player's actions, gestures (some canceled) and Undo reversals in their own scope,
    /// among another player's actions they can't undo.
    /// </summary>
    public static readonly Gen<Op[]> SinglePlayerUndos = Gen.Frequency(
        (30, MyActs),
        (10, MyGestures),
        (5, MyCanceledGestures),
        (10, OthersActs),
        (30, MyUndos)
    ).Array[0, 40];

    /// <summary>The same, with Redo reversals too.</summary>
    public static readonly Gen<Op[]> SinglePlayer = Gen.Frequency(
        (30, MyActs),
        (10, MyGestures),
        (5, MyCanceledGestures),
        (10, OthersActs),
        (30, MyUndos),
        (25, MyRedos)
    ).Array[0, 40];

    public static string Print(Op[] ops) =>
        ops.Length == 0 ? "(no operations)" : "\n" + string.Join("\n", ops.Select(op => $"  {op}"));
}
