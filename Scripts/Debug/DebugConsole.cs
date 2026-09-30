using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ImGuiNET;
using SysVec2 = System.Numerics.Vector2;
using SysVec4 = System.Numerics.Vector4;

/// <summary>
/// In-game Dear ImGui debug console.
/// Toggle with the backtick (`) key.
/// Open the events panel with <c>/events</c>.
/// </summary>
public partial class DebugConsole : Node
{
    private bool _consoleOpen;
    private bool _eventsOpen;
    private bool _prevToggleDown;
    private bool _focusInput;
    private bool _scrollOutputToBottom;
    private string _input = "";
    private readonly List<string> _output = new() { "Type /help for commands." };

    public override void _Ready()
    {
        // Keep the console usable even if the game tree is paused.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        bool toggleDown = Input.IsKeyPressed(Key.Quoteleft);
        if (toggleDown && !_prevToggleDown)
        {
            _consoleOpen = !_consoleOpen;
            if (_consoleOpen)
                _focusInput = true;
        }
        _prevToggleDown = toggleDown;

        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        var io = ImGui.GetIO();
        ReleaseIfPhantom(io, ImGuiMouseButton.Left, MouseButton.Left);
        ReleaseIfPhantom(io, ImGuiMouseButton.Right, MouseButton.Right);
        ReleaseIfPhantom(io, ImGuiMouseButton.Middle, MouseButton.Middle);

        if (_consoleOpen)
            DrawConsole();
        if (_eventsOpen)
            DrawEvents();
    }

    private void DrawConsole()
    {
        string submitted = null;

        ImGui.SetNextWindowSize(new SysVec2(520, 320), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Console", ref _consoleOpen))
        {
            float footer = ImGui.GetFrameHeightWithSpacing();
            if (ImGui.BeginChild("##output", new SysVec2(0, -footer)))
            {
                foreach (var line in _output)
                    ImGui.TextUnformatted(line);

                if (_scrollOutputToBottom)
                {
                    ImGui.SetScrollHereY(1.0f);
                    _scrollOutputToBottom = false;
                }
            }
            ImGui.EndChild();

            ImGui.Separator();

            if (_focusInput)
            {
                ImGui.SetKeyboardFocusHere();
                _focusInput = false;
            }

            _input = _input.Replace("`", "");

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##cmd", ref _input, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                submitted = _input;
                _input = "";
                _focusInput = true;
            }
        }
        ImGui.End();

        // Commands run outside the window, so one that throws can't leave it unended.
        if (submitted != null)
            RunCommand(submitted);
    }

    private void RunCommand(string raw)
    {
        var cmd = raw.Trim();
        if (cmd.Length == 0)
            return;

        Log("> " + cmd);
        try
        {
            ExecuteCommand(cmd);
        }
        catch (Exception e)
        {
            Log($"Failed: {e.Message}");
            GD.PushError($"Console command '{cmd}' failed: {e}");
        }
    }

    private void ExecuteCommand(string cmd)
    {
        if (cmd.StartsWith("/spam"))
        {
            var arg = cmd["/spam".Length..].Trim();
            if (int.TryParse(arg.Length == 0 ? "1000" : arg, out int count) && count > 0)
                Spam(count);
            else
                Log("Usage: /spam [count]");
            return;
        }

        switch (cmd)
        {
            case "/events":
                _eventsOpen = true;
                Log("Opened the event log inspector.");
                break;
            case "/help":
                Log("Commands: /events, /spam [count], /help");
                break;
            default:
                Log($"Unknown command: {cmd}");
                break;
        }
    }

    /// <summary>
    /// Fills the event log with undoable no-op events for performance testing.
    /// Each rewrites a component with its current state.
    /// </summary>
    private void Spam(int count)
    {
        var sync = EventSynchronizer.Instance;
        var states = ProjectService
            .Instance?.Components.Records.Values.Where(s => !s.Deleted)
            .Select(s => s with { Transition = Transition.None })
            .ToArray();
        if (sync == null || states == null || states.Length == 0)
        {
            Log("Spam needs at least one component on the table.");
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        // bulk loading sends one change notification at the end instead of one per event
        sync.BulkLoading = true;
        try
        {
            for (int i = 0; i < count; i++)
                sync.Submit(TableEvent.Now([Effect.Upsert(states[i % states.Length])]));
        }
        finally
        {
            ProjectService.Instance.EndBulkLoad();
        }

        Log(
            $"Added {count} events in {watch.ElapsedMilliseconds} ms ({sync.EventLog.Count} total)."
        );
    }

    private void Log(string line)
    {
        _output.Add(line);
        _scrollOutputToBottom = true;
    }

    /// <summary>
    /// The most recent <see cref="DebugTimings"/>, newest first.
    /// </summary>
    private static void DrawTimings()
    {
        if (!ImGui.CollapsingHeader("Timings"))
            return;

        if (DebugTimings.Recent.Count == 0)
            ImGui.TextDisabled("Nothing timed yet.");

        foreach (var t in DebugTimings.Recent.Reverse())
            ImGui.TextUnformatted(
                $"{t.Label, -6} {t.Milliseconds, 9:F2} ms   at {t.EventCount} events"
            );

        ImGui.Separator();
    }

    /// <summary>
    /// Infinite-scrolling list of events, newest first.
    /// Only the rows on screen are read from the log.
    /// </summary>
    private unsafe void DrawEvents()
    {
        ImGui.SetNextWindowSize(new SysVec2(360, 480), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Event Log", ref _eventsOpen))
        {
            var eventLog = EventSynchronizer.Instance?.EventLog;
            int count = eventLog?.Count ?? 0;

            ImGui.TextUnformatted($"{count} events");
            ImGui.TextDisabled("amber = undo/redo   sN = source");
            ImGui.Separator();

            DrawTimings();

            if (ImGui.BeginChild("##eventlist") && count > 0)
            {
                var amber = new SysVec4(1f, 0.78f, 0.28f, 1f);

                var clipper = new ImGuiListClipperPtr(
                    ImGuiNative.ImGuiListClipper_ImGuiListClipper()
                );
                clipper.Begin(count);
                while (clipper.Step())
                {
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        int i = count - 1 - row;
                        var e = eventLog.GetAt(i).Value;
                        bool colored = e.Undo != null;
                        if (colored)
                            ImGui.PushStyleColor(ImGuiCol.Text, amber);

                        string line = $"{i, 5}  s{e.Id.source, -3} {Describe(e)}";
                        if (e.Undo is { } u)
                        {
                            int target = eventLog.IndexOf(u.Reverses);
                            line += target >= 0 ? $"  → #{target}" : "  → #?";
                            // other presses it retires with it
                            if (u.Also != null)
                                foreach (var also in u.Also)
                                    line += eventLog.IndexOf(also) is var at and >= 0
                                        ? $", #{at}"
                                        : ", #?";
                        }
                        ImGui.TextUnformatted(line);

                        if (colored)
                            ImGui.PopStyleColor();
                    }
                }
                clipper.End();
                clipper.Destroy();
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }

    /// <summary>
    /// A label for events. Either the command that made it or the effects.
    /// </summary>
    private static string Describe(TableEvent e)
    {
        if (e.Command is { } command)
            return command.Id;

        if (e.Undo is { } u)
            return u.ByRedo ? "Redo" : "Undo";

        if (e.Effects.Length == 0)
            return e.Close ? "Close" : "(empty)";

        return string.Join(
            ", ",
            e.Effects.GroupBy(EffectLabel)
                .Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key)
        );
    }

    /// <summary>
    /// Bug fix for right-click context menu.
    /// </summary>
    private static void ReleaseIfPhantom(
        ImGuiIOPtr io,
        ImGuiMouseButton imguiButton,
        MouseButton godotButton
    )
    {
        if (io.MouseDown[(int)imguiButton] && !Input.IsMouseButtonPressed(godotButton))
            io.AddMouseButtonEvent((int)imguiButton, false);
    }

    private static string EffectLabel(Effect fx) =>
        fx switch
        {
            UpdateReplicatedEffect<ComponentState> { Payload.Deleted: true } => "Delete",
            UpdateReplicatedEffect<ComponentState> => "Upsert",
            SetReplicatedValueEffect<ProjectGameSettings> => "Settings",
            SetReplicatedValueEffect<ActiveGameStateRef> => "ActiveGameState",
            UpdateReplicatedEffect<Template> => "Template",
            UpdateReplicatedEffect<DataSet> => "DataSet",
            UpdateReplicatedEffect<DataRow> => "DataRow",
            UpdateReplicatedEffect<Prototype> => "Prototype",
            UpdateReplicatedEffect<GameState> => "GameState",
            _ => Trim(fx.GetType().Name, "Effect"),
        };

    private static string Trim(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
}
