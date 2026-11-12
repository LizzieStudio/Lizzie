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

    private static readonly Source Local = new(1);

    private static SnowStamp GetId(int clock, Source source) => new(clock, source);

    private static TimelineEvent GetTableEvent(SnowStamp id)
    {
        return new TimelineEvent { Id = id, Writes = [new Prototype { Id = new(1) }] };
    }

    private static TimelineEvent GetUndoRedoEvent(SnowStamp id, SnowStamp target, bool redo = false)
    {
        return new TimelineEvent
        {
            Id = id,
            Undo = new UndoFlag { Reverses = target, ByRedo = redo },
        };
    }

    [Fact]
    public void UndoTargetsNewestOwnEvent()
    {
        Gen.Select(
                Gen.Int[1, 3].Select(s => new Source(s)),
                Gen.OneOfConst("event", "undo", "redo"),
                Gen.Int[0, 100]
            )
            .Array[0, 20]
            .Sample(p =>
            {
                var events = new List<SnowStamp>();
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
}
