using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Lizzie.Replication.Machinery;
using Xunit;

namespace Lizzie.Tests;

public class TimelineUndoTests
{
    private readonly ITestOutputHelper _output;

    public TimelineUndoTests(ITestOutputHelper output) => _output = output;

    private const byte Local = 1;

    private static SnowportId GetId(int clock, byte source) => new(clock, source);

    private static TimelineEvent GetTableEvent(SnowportId id)
    {
        return new TimelineEvent { Id = id, Writes = [new Prototype { Id = 1 }] };
    }

    private static TimelineEvent GetUndoRedoEvent(
        SnowportId id,
        SnowportId target,
        bool redo = false
    )
    {
        return new TimelineEvent
        {
            Id = id,
            Undo = new UndoFlag { Reverses = target, ByRedo = redo },
        };
    }

    private static Timeline GetTimeline(byte[] sources, int count = 1)
    {
        var timeline = new Timeline();
        for (int i = 0; i < sources.Length; i++)
        {
            for (int l = 0; l < count; l++)
            {
                var id = GetId(i + 1, sources[i]);
                timeline.Add(
                    id,
                    new TimelineEvent { Id = id, Writes = [new Selection { Id = i + 1 }] }
                );
            }
        }
        return timeline;
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
                var timeline = new Timeline();
                foreach (var item in p)
                {
                    var (source, what, pick) = item;
                    var id = GetId(idIndex++, source);
                    switch (what)
                    {
                        case "event":
                            events.Add(id);
                            timeline.Add(id, GetTableEvent(id));
                            break;
                        case "undo":
                            if (events.Count > 0)
                            {
                                var target = events[pick % events.Count];
                                timeline.Add(id, GetUndoRedoEvent(id, target));
                            }
                            break;
                        case "redo":
                            if (events.Count > 0)
                            {
                                var target = events[pick % events.Count];
                                timeline.Add(id, GetUndoRedoEvent(id, target, true));
                            }
                            break;
                    }
                }

                var lastId = GetId(idIndex++, Local);
                var lastEvent = GetTableEvent(lastId);
                timeline.Add(lastId, lastEvent);

                _output.WriteLine($"The timeline has {timeline.Count} many events");

                Assert.Equal(
                    lastId,
                    TimelineUndo.Undo(timeline, Local, (author, _) => author == Local)?.Reverses
                );
            });
    }

    [Fact]
    public void FirstTest()
    {
        Timeline timeline = new();
        TimelineUndo.Undo(timeline, 0, (_, _) => true);
        Assert.Equal("Hello, world!", "Hello, " + "world!");
    }
}
