using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lizzie.Replication.Machinery;

/// <summary>A record for sessions to write. Faces are unique, so every write can be told apart.</summary>
public sealed record TestRecord : Replicated
{
    public int Face { get; init; }
}

/// <summary>A singleton record for sessions to write, like the project settings.</summary>
[Singleton]
public sealed record TestValue : Replicated
{
    /// <summary>Its id, apart from the <see cref="TestRecord"/>s'.</summary>
    public const int SingletonId = 100;

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
/// An undo scope, as a view or command would give one: whose changes it takes, and which records,
/// by the bits of <paramref name="Mask"/>. A mask of 0 takes every record, the singleton included.
/// </summary>
public sealed record TestScope(ScopeKind Kind, int Mask)
{
    public Func<byte, Replicated, bool> For(byte me) =>
        (author, record) =>
            Kind switch
            {
                ScopeKind.Mine => author == me,
                ScopeKind.Others => author != me,
                _ => true,
            } && (Mask == 0 || record is TestRecord && (Mask & (1 << (record.Id - 1))) != 0);

    public override string ToString() => Mask == 0 ? $"{Kind}" : $"{Kind} records {Mask}";
}

/// <summary>Something a player does, played out by <see cref="UndoSession"/>.</summary>
public abstract record Op
{
    /// <summary>
    /// An action writing records <paramref name="First"/> and <paramref name="Second"/> (0 for none)
    /// and <paramref name="Values"/> writes to the singleton. It joins the player's gesture, if one is going.
    /// </summary>
    public sealed record Act(byte Source, int First, int Second, int Values) : Op;

    /// <summary>Starts a gesture, closing one left open first, as the synchronizer does.</summary>
    public sealed record Start(byte Source, int Record) : Op;

    /// <summary>Ends the player's gesture with a drop writing <paramref name="Record"/>, or with a close-only event for 0.</summary>
    public sealed record End(byte Source, int Record) : Op;

    /// <summary>Reversals Undo or Redo. The expected implementation decides the flag.</summary>
    public sealed record Reversal(byte Source, bool Redo, TestScope Scope) : Op;

    /// <summary>The player leaves, and the host closes and undoes their open gestures.</summary>
    public sealed record Leave(byte Source) : Op;

    /// <summary>
    /// A reversal no Undo or Redo would write, reversing whatever <paramref name="Pick"/> lands on:
    /// a unit, an event that isn't one, or a missing id.
    /// </summary>
    public sealed record RawFlag(byte Source, int Pick, bool ByRedo) : Op;

    /// <summary>
    /// A whole gesture: its first event, <paramref name="Moves"/> more, and the drop that closes it,
    /// all writing <paramref name="Record"/>. Closes a gesture left open first.
    /// </summary>
    public sealed record Gesture(byte Source, int Record, int Moves) : Op;

    /// <summary>An action that arrives late, with an id <paramref name="Back"/> steps older than the newest.</summary>
    public sealed record Late(byte Source, int Record, int Back) : Op;

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
/// A log built by playing out operations, the way the game would build it.
/// </summary>
public sealed class UndoSession
{
    public const byte Host = Snowport.HostSource;
    public const int RecordCount = 4;
    public static readonly byte[] Sources = [1, 2, 3, 4];

    public readonly EventLog Log = new();

    // decides undos, redos and which gestures a leaving player left open
    private readonly IUndoApi _driver;

    // each player's open gesture
    private readonly Dictionary<byte, SnowportId> _open = new();

    // Events take even clock values, so late events can take the odd ones between.
    private long _clock;

    // the events the operation being applied has recorded
    private readonly List<TableEvent> _recorded = new();

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
    public IReadOnlyList<TableEvent> Apply(Op op)
    {
        _recorded.Clear();
        switch (op)
        {
            case Op.Act a:
                Record(
                    new TableEvent
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
                Record(new TableEvent { Id = head, Group = head }, g.Record);
                for (int i = 0; i < g.Moves; i++)
                    Record(new TableEvent { Id = NextId(g.Source), Group = head }, g.Record);
                Record(
                    new TableEvent
                    {
                        Id = NextId(g.Source),
                        Group = head,
                        Close = true,
                    },
                    g.Record
                );
                break;

            case Op.Start s:
                CloseOpen(s.Source);
                var first = NextId(s.Source);
                Record(new TableEvent { Id = first, Group = first }, s.Record);
                _open[s.Source] = first;
                break;

            case Op.End e when _open.Remove(e.Source, out var group):
                Record(
                    new TableEvent
                    {
                        Id = NextId(e.Source),
                        Group = group,
                        Close = true,
                    },
                    e.Record
                );
                break;

            case Op.Reversal p:
                var flag = Decide(Log, p.Source, p.Redo, p.Scope);
                if (flag != null)
                    Record(new TableEvent { Id = NextId(p.Source), Undo = flag });
                break;

            case Op.Concurrent c:
                var unseen = Log
                    .Keys.Skip(Math.Max(0, Log.Count - c.Back))
                    .Where(key => key.source != c.Source)
                    .ToList();
                var seen = new EventLog();
                foreach (var (key, known) in Log)
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
                if (Log.ContainsKey(reversalId))
                    break;
                Record(new TableEvent { Id = reversalId, Undo = concurrent });
                // Everything after is newer, as the player's clock has moved on too.
                while (_clock < reversalId.logicClock)
                    _clock += 2;
                break;

            case Op.Leave l when l.Source != Host:
                foreach (var open in _driver.OpenGroups(Log, l.Source))
                {
                    Record(CloseOnly(open, Host));
                    Record(
                        new TableEvent
                        {
                            Id = NextId(Snowport.AdminSource),
                            Undo = new UndoFlag { Reverses = open },
                        }
                    );
                }
                _open.Remove(l.Source);
                break;

            case Op.RawFlag r when Log.Count > 0:
                var picked = Log.GetAt(r.Pick / 4 % Log.Count).Value;
                var reverses = (r.Pick % 4) switch
                {
                    0 => picked.Id,
                    1 => Id(_clock + 1, 7), // never used: sources are 0 to 4
                    _ => picked.Unit,
                };
                Record(
                    new TableEvent
                    {
                        Id = NextId(r.Source),
                        Undo = new UndoFlag { Reverses = reverses, ByRedo = r.ByRedo },
                    }
                );
                break;

            case Op.Late l when 2 * l.Back - 1 < _clock:
                var id = Id(_clock - (2 * l.Back - 1), l.Source);
                if (Log.ContainsKey(id))
                    break;
                // It joins the player's gesture only if it's newer than the gesture's first event.
                var joins =
                    _open.TryGetValue(l.Source, out var gesture) && gesture.CompareTo(id) < 0;
                Record(
                    new TableEvent { Id = id, Group = joins ? gesture : SnowportId.Empty },
                    l.Record
                );
                break;
        }
        return _recorded.ToArray();
    }

    private UndoFlag Decide(EventLog log, byte source, bool redo, TestScope scope) =>
        redo
            ? _driver.Redo(log, source, scope.For(source))
            : _driver.Undo(log, source, scope.For(source));

    private void CloseOpen(byte source)
    {
        if (_open.Remove(source, out var group))
            Record(CloseOnly(group, source));
    }

    private TableEvent CloseOnly(SnowportId group, byte source) =>
        new()
        {
            Id = NextId(source),
            Group = group,
            Close = true,
        };

    private SnowportId NextId(byte source)
    {
        _clock += 2;
        return Id(_clock, source);
    }

    private static SnowportId Id(long clock, byte source) => new(clock, source);

    /// <summary>
    /// Records the event in id order, with the given records and values written.
    /// Every write gets a unique face or value, so the one that wins can be told apart.
    /// </summary>
    private void Record(TableEvent e, int first = 0, int second = 0, int values = 0)
    {
        var records = new List<Replicated>();
        foreach (var record in new[] { first, second }.Where(r => r != 0))
            records.Add(
                new TestRecord { Id = record, Face = (int)(long)e.Id * 10 + records.Count }
            );
        for (int i = 0; i < values; i++)
            records.Add(
                new TestValue { Id = TestValue.SingletonId, Text = $"{e.Id}.{records.Count}" }
            );
        if (records.Count > 0)
            e.Records = records.ToArray();

        int at = Log.Count;
        while (at > 0 && Log.GetAt(at - 1).Key.CompareTo(e.Id) > 0)
            at--;
        Log.Insert(at, e.Id, e);
        _recorded.Add(e);
    }

    #region Descriptions for failures

    /// <summary>An event's name in <see cref="Dump"/>: its place in the log.</summary>
    public string Name(SnowportId id) => Log.IndexOf(id) is var i and >= 0 ? $"#{i}" : $"?{id}";

    public string Describe(UndoFlag flag) =>
        flag == null
            ? "nothing"
            : $"{(flag.ByRedo ? "redo" : "undo")} reversing {Name(flag.Reverses)}"
                + (
                    flag.Also is { Length: > 0 } also
                        ? $" and {string.Join(", ", also.Order().Select(Name))}"
                        : ""
                );

    /// <summary>The log, one event per line, oldest first.</summary>
    public string Dump() =>
        string.Join(
            "\n",
            Log.Values.Select(e =>
            {
                string group = e.Group == SnowportId.Empty ? "" : $" in {Name(e.Group)}";
                string close = e.Close ? " close" : "";
                string what = e.Undo is { } f
                    ? Describe(f)
                    : string.Join(
                        " ",
                        e.Records.Select(record =>
                            record switch
                            {
                                TestRecord r => $"r{r.Id}={r.Face}",
                                TestValue v => $"v={v.Text}",
                                _ => record.GetType().Name,
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
    private static readonly Gen<int> Record = Gen.Int[1, UndoSession.RecordCount];
    private static readonly Gen<int> MaybeRecord = Gen.Int[0, UndoSession.RecordCount];

    public static readonly Gen<TestScope> Scope = Gen.Select(
        Gen.OneOfConst(ScopeKind.Mine, ScopeKind.Others, ScopeKind.Anyone),
        Gen.Int[0, 15],
        (kind, mask) => new TestScope(kind, mask)
    );

    private static readonly Gen<Op> Acts = Gen.Select(
        Source,
        MaybeRecord,
        MaybeRecord,
        Gen.Int[0, 2],
        (s, first, second, values) => (Op)new Op.Act(s, first, second, values)
    );

    private static readonly Gen<Op> Starts = Gen.Select(
        Source,
        Record,
        (s, record) => (Op)new Op.Start(s, record)
    );

    private static readonly Gen<Op> Ends = Gen.Select(
        Source,
        MaybeRecord,
        (s, record) => (Op)new Op.End(s, record)
    );

    private static readonly Gen<Op> Reversals = Gen.Select(
        Source,
        Gen.Bool,
        Scope,
        (s, redo, scope) => (Op)new Op.Reversal(s, redo, scope)
    );

    private static readonly Gen<Op> Leaves = Gen.Byte[2, 4].Select(s => (Op)new Op.Leave(s));

    private static readonly Gen<Op> RawFlags = Gen.Select(
        Source,
        Gen.Int[0, 1000],
        Gen.Bool,
        (s, pick, byRedo) => (Op)new Op.RawFlag(s, pick, byRedo)
    );

    private static readonly Gen<Op> Gestures = Gen.Select(
        Source,
        Record,
        Gen.Int[0, 2],
        (s, record, moves) => (Op)new Op.Gesture(s, record, moves)
    );

    private static readonly Gen<Op> Lates = Gen.Select(
        Source,
        Record,
        Gen.Int[1, 5],
        (s, record, back) => (Op)new Op.Late(s, record, back)
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
    /// Three players acting and pressing, often at the same time, on few records,
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

    private static readonly Gen<Op> MyActs = Record.Select(r => (Op)new Op.Act(Me, r, 0, 0));
    private static readonly Gen<Op> OthersActs = Record.Select(r => (Op)new Op.Act(2, r, 0, 0));
    private static readonly Gen<Op> MyGestures = Gen.Select(
        Record,
        Gen.Int[0, 2],
        (record, moves) => (Op)new Op.Gesture(Me, record, moves)
    );
    private static readonly Gen<Op> MyUndos = Gen.Const(
        (Op)new Op.Reversal(Me, false, new TestScope(ScopeKind.Mine, 0))
    );
    private static readonly Gen<Op> MyRedos = Gen.Const(
        (Op)new Op.Reversal(Me, true, new TestScope(ScopeKind.Mine, 0))
    );

    /// <summary>
    /// One player's actions, gestures and Undo reversals in their own scope,
    /// among another player's actions they can't undo.
    /// </summary>
    public static readonly Gen<Op[]> SinglePlayerUndos = Gen.Frequency(
        (30, MyActs),
        (10, MyGestures),
        (10, OthersActs),
        (30, MyUndos)
    ).Array[0, 40];

    /// <summary>The same, with Redo reversals too.</summary>
    public static readonly Gen<Op[]> SinglePlayer = Gen.Frequency(
        (30, MyActs),
        (10, MyGestures),
        (10, OthersActs),
        (30, MyUndos),
        (25, MyRedos)
    ).Array[0, 40];

    public static string Print(Op[] ops) =>
        ops.Length == 0 ? "(no operations)" : "\n" + string.Join("\n", ops.Select(op => $"  {op}"));
}
