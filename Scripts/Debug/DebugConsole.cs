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
            if (
                ImGui.InputText(
                    "##cmd",
                    ref _input,
                    256,
                    ImGuiInputTextFlags.EnterReturnsTrue
                )
            )
            {
                RunCommand(_input);
                _input = "";
                _focusInput = true;
            }
        }
        ImGui.End();
    }

    private void RunCommand(string raw)
    {
        var cmd = raw.Trim();
        if (cmd.Length == 0)
            return;

        Log("> " + cmd);
        switch (cmd)
        {
            case "/events":
                _eventsOpen = true;
                Log("Opened the event log inspector.");
                break;
            case "/help":
                Log("Commands: /events, /help");
                break;
            default:
                Log($"Unknown command: {cmd}");
                break;
        }
    }

    private void Log(string line)
    {
        _output.Add(line);
        _scrollOutputToBottom = true;
    }

    /// <summary>
    /// Infinite-scrolling list of events.
    /// </summary>
    private unsafe void DrawEvents()
    {
        ImGui.SetNextWindowSize(new SysVec2(360, 480), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Event Log", ref _eventsOpen))
        {
            var events = EventSynchronizer.Instance?.Events;
            int count = events?.Count ?? 0;

            ImGui.TextUnformatted($"{count} events");
            ImGui.TextDisabled("amber = undo/redo   dim = undone   sN = source");
            ImGui.Separator();

            var undone =
                events != null ? UndoLog.ComputeUndone(events) : new HashSet<SnowportId>();

            if (ImGui.BeginChild("##eventlist") && count > 0)
            {
                var indexById = new Dictionary<SnowportId, int>(count);
                for (int j = 0; j < count; j++)
                    indexById[events[j].Id] = j;

                var amber = new SysVec4(1f, 0.78f, 0.28f, 1f);
                var gray = new SysVec4(0.55f, 0.55f, 0.55f, 1f);

                var clipper = new ImGuiListClipperPtr(
                    ImGuiNative.ImGuiListClipper_ImGuiListClipper()
                );
                clipper.Begin(count);
                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        var e = events[i];
                        int pushed = 0;
                        if (e.Action is UndoAction)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, amber);
                            pushed = 1;
                        }
                        else if (undone.Contains(e.Id))
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, gray);
                            pushed = 1;
                        }

                        string line = $"{i,5}  s{e.Id.source,-3} {Describe(e)}";
                        if (e.Action is UndoAction u)
                            line += indexById.TryGetValue(u.Target, out var ti)
                                ? $"  → #{ti}"
                                : "  → #?";
                        ImGui.TextUnformatted(line);

                        if (pushed > 0)
                            ImGui.PopStyleColor(pushed);
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
    /// A label for events. Either the action or the effects.
    /// </summary>
    private static string Describe(TableEvent e)
    {
        if (e.Action is UndoAction ua)
            return ua.Redo ? "Redo" : "Undo";

        if (e.Action != null)
            return Trim(e.Action.GetType().Name, "Action");

        if (e.Effects.Length == 0)
            return "(empty)";

        return string.Join(
            ", ",
            e.Effects
                .GroupBy(EffectLabel)
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
            ComponentEffect { State.Location: VisualComponentBase.ComponentLocation.Deleted } =>
                "Delete",
            ComponentEffect => "Upsert",
            UpdatePlayerEffect => "Player",
            UpdateSettingsEffect => "Settings",
            UpdateReplicatedEffect<Template> => "Template",
            UpdateReplicatedEffect<DataSet> => "DataSet",
            UpdateReplicatedEffect<Prototype> => "Prototype",
            UpdateReplicatedEffect<GameState> => "GameState",
            TableClearEffect => "Clear",
            _ => Trim(fx.GetType().Name, "Effect"),
        };

    private static string Trim(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
}
