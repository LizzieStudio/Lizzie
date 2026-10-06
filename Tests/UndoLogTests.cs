using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lizzie.Replication.Machinery;
using Xunit;

public class UndoLogTests
{
    private readonly ITestOutputHelper _output;

    public UndoLogTests(ITestOutputHelper output) => _output = output;

    private const byte Local = 1;

    private static SnowportId GetId(int clock, byte source) => new(clock, source);

    private static TableEvent GetTableEvent(SnowportId id)
    {
        return new TableEvent { Id = id, Records = [new Prototype { Id = 1 }] };
    }

    private static TableEvent GetUndoRedoEvent(SnowportId id, SnowportId target, bool redo = false)
    {
        return new TableEvent
        {
            Id = id,
            Undo = new UndoFlag { Reverses = target, ByRedo = redo },
        };
    }

    private static EventLog GetEventLog(byte[] sources, int count = 1)
    {
        var log = new EventLog();
        for (int i = 0; i < sources.Length; i++)
        {
            for (int l = 0; l < count; l++)
            {
                var id = GetId(i + 1, sources[i]);
                log.Add(id, new TableEvent { Id = id, Records = [new Selection { Id = i + 1 }] });
            }
        }
        return log;
    }

    [Fact]
    public void UndoTargetsNewestOwnEvent()
    {
        Gen.Select(Gen.Byte[1, 3], Gen.OneOfConst("event", "undo", "redo"), Gen.Int[0, 100])
            .Array[0, 20]
            .Sample(p =>
            {
                var events = new List<SnowportId>();
                int idIndex = 0;
                var log = new EventLog();
                foreach (var item in p)
                {
                    var (source, what, pick) = item;
                    var id = GetId(idIndex++, source);
                    switch (what)
                    {
                        case "event":
                            events.Add(id);
                            log.Add(id, GetTableEvent(id));
                            break;
                        case "undo":
                            if (events.Count > 0)
                            {
                                var target = events[pick % events.Count];
                                log.Add(id, GetUndoRedoEvent(id, target));
                            }
                            break;
                        case "redo":
                            if (events.Count > 0)
                            {
                                var target = events[pick % events.Count];
                                log.Add(id, GetUndoRedoEvent(id, target, true));
                            }
                            break;
                    }
                }

                var lastId = GetId(idIndex++, Local);
                var lastEvent = GetTableEvent(lastId);
                log.Add(lastId, lastEvent);

                _output.WriteLine($"The log has {log.Count} many events");

                Assert.Equal(
                    lastId,
                    UndoLog.Undo(log, Local, (author, _) => author == Local)?.Reverses
                );
            });
    }

    [Fact]
    public void FirstTest()
    {
        EventLog log = new();
        UndoLog.Undo(log, 0, (_, _) => true);
        Assert.Equal("Hello, world!", "Hello, " + "world!");
    }
}
