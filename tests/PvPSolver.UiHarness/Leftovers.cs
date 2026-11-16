// Renders and exercises the four windows of the plugin that are not the new settings window: the first-start tutorial
// (the real window class), the two HUD windows (real CtrlWindow base, stand-in bodies) and the legacy settings window
// (its shell and header with a stand-in body: the real class is about 4400 lines of game and plugin calls).
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using LalaUiKit;
using Lala.UiRender;
using RotationSolver.Basic;
using RotationSolver.Basic.Configuration;
using RotationSolver.UI;
using RotationSolver.UI.Pvp;

namespace RotationSolver.Basic
{
    /// <summary>Only the one call the tutorial window makes.</summary>
    internal static class DataCenter
    {
        public static bool PlayerAvailable() => true;
    }
}

namespace RotationSolver.Basic.Configuration
{
    internal partial class Configs
    {
        public bool TutorialDone { get; set; }
        public Vector4 ControlWindowLockBg { get; set; } = new(0, 0, 0, 0.55f);
        public Vector4 ControlWindowUnlockBg { get; set; } = new(0, 0, 0, 0.75f);
    }
}

namespace PvPSolver.UiHarness
{
    internal static class Leftovers
    {
        private static readonly byte[] DarkBackground = [20, 20, 24];
        private static readonly byte[] GreyBackground = [112, 116, 124];

        // ---- one live ImGui context that frames can be stepped through -------------------------------------------

        private sealed class Session : IDisposable
        {
            private readonly ImGuiContextPtr _context;
            private readonly Vector2 _size;
            public RenderLook Look { get; }

            public Session(RenderOptions options, Vector2 size)
            {
                _size = size;
                _context = ImGui.CreateContext();
                var io = ImGui.GetIO();
                io.DisplaySize = size;
                io.DisplayFramebufferScale = Vector2.One;
                io.DeltaTime = 1f / 60f;
                io.MousePos = new Vector2(-10000, -10000);
                io.ConfigWindowsMoveFromTitleBarOnly = true;
                Look = UiLook.Apply(options);
            }

            public void Frame(Action draw, Vector2? mouse = null, bool down = false)
            {
                var io = ImGui.GetIO();
                io.MousePos = mouse ?? new Vector2(-10000, -10000);
                io.MouseDown[0] = down;
                ImGui.NewFrame();
                draw();
                ImGui.Render();
            }

            public byte[] Pixels(byte[] background)
            {
                var width = (int)_size.X;
                var height = (int)_size.Y;
                var pixels = new byte[width * height * 4];
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i] = background[0];
                    pixels[i + 1] = background[1];
                    pixels[i + 2] = background[2];
                    pixels[i + 3] = 255;
                }

                var data = ImGui.GetDrawData();
                if (!data.Valid || data.TotalVtxCount == 0)
                    throw new InvalidOperationException("The frame produced no draw data.");
                _ = CpuRasterizer.Render(data, pixels, width, height, Look.Textures);
                return pixels;
            }

            public void Save(string path, byte[] background)
            {
                PngWriter.Write(path, Pixels(background), (int)_size.X, (int)_size.Y);
            }

            public void Dispose() => ImGui.DestroyContext(_context);
        }

        private static Vector2 _lastPos;
        private static Vector2 _lastSize;

        private static void Show(Window window, Vector2 pos, Vector2? size, bool fixedSize, bool fixedPos)
        {
            ImGui.SetNextWindowPos(pos, fixedPos ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
            if (size is { } s)
                ImGui.SetNextWindowSize(s, fixedSize ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
            var open = true;
            window.PreDraw();
            ImGui.Begin(window.WindowName, ref open, window.Flags | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
            window.Draw();
            _lastPos = ImGui.GetWindowPos();
            _lastSize = ImGui.GetWindowSize();
            ImGui.End();
            window.PostDraw();
        }

        private static void Settle(Session s, Action draw, int frames = 3, Vector2? mouse = null)
        {
            for (var i = 0; i < frames; i++)
                s.Frame(draw, mouse);
        }

        // ---- entry point ----------------------------------------------------------------------------------------

        public static void Run(RenderOptions options, string outputDirectory)
        {
            RenderTutorial(options, outputDirectory);
            RenderHud(options, outputDirectory);
            RenderLegacy(options, outputDirectory);
            Checks(options);
        }

        private static string Out(string directory, string name, Vector2 size) =>
            Path.Combine(directory, $"{name}-{(int)size.X}x{(int)size.Y}.png");

        // ---- tutorial -------------------------------------------------------------------------------------------

        private static FirstStartTutorialWindow NewTutorial(int step)
        {
            Service.Config = new Configs();
            var window = new FirstStartTutorialWindow();
            typeof(FirstStartTutorialWindow).GetField("_stepIndex", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, step);
            return window;
        }

        private static int TutorialSteps() =>
            ((Array)typeof(FirstStartTutorialWindow).GetField("Steps", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Length;

        private static void RenderTutorial(RenderOptions options, string directory)
        {
            var count = TutorialSteps();
            foreach (var (label, step) in new[] { ("first", 0), ("middle", count / 2), ("last", count - 1) })
            {
                var window = NewTutorial(step);
                var size = window.Size ?? throw new InvalidOperationException("The tutorial declares no Size.");
                using var s = new Session(options, size);
                Action draw = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw);
                var path = Out(directory, $"tutorial-{label}-step{step + 1}of{count}", size);
                s.Save(path, DarkBackground);
                Console.WriteLine($"{Path.GetFileName(path)}: tutorial step {step + 1} of {count}");
            }
        }

        // ---- HUD windows ----------------------------------------------------------------------------------------

        private static void Icon(ImDrawListPtr draw, Vector2 pos, float size, Vector4 color, string label, string? cooldown)
        {
            draw.AddRectFilled(pos, pos + new Vector2(size), Theme.Pack(color with { W = 1f }), 5f);
            draw.AddRect(pos, pos + new Vector2(size), Theme.Pack(new Vector4(0, 0, 0, 0.6f)), 5f);
            var scale = size >= 40 ? 1.15f : 0.9f;
            var text = ImGui.CalcTextSize(label) * scale;
            draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, pos + ((new Vector2(size) - text) / 2f), Theme.Pack(new Vector4(1, 1, 1, 0.95f)), label);
            if (cooldown is not null)
            {
                draw.AddRectFilled(pos, pos + new Vector2(size), Theme.Pack(new Vector4(0, 0, 0, 0.55f)), 5f);
                var cd = ImGui.CalcTextSize(cooldown);
                draw.AddText(pos + ((new Vector2(size) - cd) / 2f), Theme.Pack(Theme.TextPrimary), cooldown);
            }
        }

        private static readonly Vector4[] Palette =
        [
            new(0.80f, 0.34f, 0.42f, 1), new(0.34f, 0.58f, 0.86f, 1), new(0.86f, 0.66f, 0.28f, 1),
            new(0.46f, 0.72f, 0.46f, 1), new(0.62f, 0.46f, 0.80f, 1), new(0.40f, 0.72f, 0.74f, 1),
        ];

        /// <summary>Stand-in body of the control window: the next action pair and the three mode buttons (same arrangement and sizes as the real one).</summary>
        private sealed class StandInControl : CtrlWindow
        {
            public StandInControl() : base("ControlWindow")
            {
                Size = new Vector2(570f, 300f);
                SizeCondition = ImGuiCond.FirstUseEver;
            }

            protected override void DrawContent()
            {
                var draw = ImGui.GetWindowDrawList();
                var origin = ImGui.GetCursorScreenPos();
                Icon(draw, origin, 60, Palette[0], "Next", null);
                Icon(draw, origin + new Vector2(68, 8), 45, Palette[1], "oGCD", "2.1");
                ImGui.Dummy(new Vector2(121, 62));
                ImGui.Spacing();
                var row = ImGui.GetCursorScreenPos();
                string[] modes = ["Auto", "Man", "Off"];
                for (var i = 0; i < 3; i++)
                    Icon(draw, row + new Vector2(i * 48, 0), 40, Palette[2 + i], modes[i], null);
                ImGui.Dummy(new Vector2(144, 42));
                ImGui.Spacing();
                ImGui.TextUnformatted("Area attacks: Full");
                ImGui.TextUnformatted("Target: Highest pressure");
            }
        }

        /// <summary>Stand-in body of the cooldown window: grouped icon grids with cooldown numbers (same widgets as the real one).</summary>
        private sealed class StandInCooldown : CtrlWindow
        {
            public StandInCooldown() : base("CooldownWindow") { }

            protected override void DrawContent()
            {
                var draw = ImGui.GetWindowDrawList();
                foreach (var group in new[] { "Attacks", "Defense" })
                {
                    Widgets.Secondary(group);
                    var origin = ImGui.GetCursorScreenPos();
                    for (var i = 0; i < 5; i++)
                        Icon(draw, origin + new Vector2(i * 36, 0), 30, Palette[(i + group.Length) % Palette.Length], (i + 1).ToString(), i % 3 == 1 ? "8" : null);
                    ImGui.Dummy(new Vector2(5 * 36, 34));
                    ImGui.NewLine();
                }
            }
        }

        private static void RenderHud(RenderOptions options, string directory)
        {
            var canvas = new Vector2(640, 420);
            foreach (var (name, locked, cooldown) in new[]
                     {
                         ("hud-control-unlocked", false, false), ("hud-control-locked", true, false),
                         ("hud-cooldown-unlocked", false, true), ("hud-cooldown-locked", true, true),
                     })
            {
                Service.Config = new Configs();
                Service.Config.IsControlWindowLock.Value = locked;
                CtrlWindow window = cooldown ? new StandInCooldown() : new StandInControl();
                using var s = new Session(options, canvas);
                var size = cooldown ? new Vector2(260, 232) : new Vector2(570, 300);
                Action draw = () => Show(window, new Vector2(30, 30), size, false, false);
                Settle(s, draw);
                var path = Out(directory, name, canvas);
                s.Save(path, GreyBackground);
                Console.WriteLine($"{Path.GetFileName(path)}: {(cooldown ? "cooldown" : "control")} window, {(locked ? "locked" : "unlocked")}");
            }
        }

        // ---- legacy settings window (shell with a stand-in body) -----------------------------------------------

        private static readonly string[] LegacyTabs = ["Main", "Job", "Actions", "List", "Basic", "UI", "Auto", "Target", "Extra", "Debug"];

        private sealed class StandInLegacy : Window
        {
            public StandInLegacy() : base("PvP Solver v1.0.0.0###rsrConfigWindow", ImGuiWindowFlags.NoScrollbar, false)
            {
                SizeCondition = ImGuiCond.FirstUseEver;
                Size = new Vector2(740f, 490f);
                SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(250, 300), MaximumSize = new Vector2(5000, 5000) };
                RespectCloseHotkey = true;
                Flags |= ImGuiWindowFlags.NoTitleBar;
            }

            public bool ResetRequested;

            public override void Draw()
            {
                _ = PvpLegacyHeader.Draw(this, ref ResetRequested);
                Theme.PushFrame();
                try
                {
                    using var table = Dalamud.Interface.Utility.Raii.ImRaii.Table("Rotation Config Table", 2, ImGuiTableFlags.Resizable);
                    if (!table)
                        return;
                    ImGui.TableSetupColumn("side", ImGuiTableColumnFlags.WidthFixed, 100);
                    _ = ImGui.TableNextColumn();
                    foreach (var tab in LegacyTabs)
                        _ = ImGui.Selectable(tab, tab == "Main", ImGuiSelectableFlags.None, new Vector2(0, 28));
                    _ = ImGui.TableNextColumn();
                    Widgets.Title("Main");
                    Widgets.Secondary("Useful information and macro list.");
                    ImGui.Spacing();
                    ImGui.TextWrapped("Compatibility: no incompatible plugins are enabled.");
                    ImGui.TextWrapped("Macros: /pvpsolver Auto, /pvpsolver Manual and /pvpsolver Off change the mode from chat.");
                    ImGui.Spacing();
                    _ = ImGui.Button("Open First Start Tutorial");
                    ImGui.SameLine();
                    _ = ImGui.Button("Support");
                }
                finally
                {
                    Theme.PopFrame();
                }
            }
        }

        private static void RenderLegacy(RenderOptions options, string directory)
        {
            foreach (var (name, size) in new[] { ("legacy-main", new Vector2(740, 490)), ("legacy-main-narrow", new Vector2(380, 440)) })
            {
                var window = new StandInLegacy();
                using var s = new Session(options, size);
                Action draw = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw);
                var path = Out(directory, name, size);
                s.Save(path, DarkBackground);
                Console.WriteLine($"{Path.GetFileName(path)}: legacy settings window, Main tab");
            }
        }

        // ---- interaction checks --------------------------------------------------------------------------------

        private static int _passed;

        private static void Expect(bool condition, string what)
        {
            if (!condition)
                throw new InvalidOperationException("Interaction check failed: " + what);
            _passed++;
            Console.WriteLine("  ok: " + what);
        }

        private static void Click(Session s, Action draw, Vector2 at)
        {
            s.Frame(draw, at);
            s.Frame(draw, at);
            s.Frame(draw, at, true);
            s.Frame(draw, at, true);
            s.Frame(draw, at);
            s.Frame(draw, at);
        }

        private static void Drag(Session s, Action draw, Vector2 from, Vector2 to)
        {
            s.Frame(draw, from);
            s.Frame(draw, from);
            s.Frame(draw, from, true);
            s.Frame(draw, from, true);
            for (var i = 1; i <= 4; i++)
                s.Frame(draw, Vector2.Lerp(from, to, i / 4f), true);
            s.Frame(draw, to);
            s.Frame(draw, to);
        }

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

        private static void Checks(RenderOptions options)
        {
            Console.WriteLine("Leftover windows, interaction checks:");
            CheckTutorial(options);
            CheckHud(options);
            CheckLegacy(options);
            Console.WriteLine($"Leftover windows: {_passed} interaction checks passed.");
        }

        private static void CheckTutorial(RenderOptions options)
        {
            var count = TutorialSteps();

            // Close: the drawn close button ends the tutorial and (through OnClose, as the window system calls it) marks it done.
            var window = NewTutorial(0);
            var size = window.Size!.Value;
            Expect(!Service.Config.TutorialDone, "the tutorial starts not done");

            window = NewTutorial(0);
            window.IsOpen = true;
            using (var s = new Session(options, size))
            {
                Action draw = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw);
                Click(s, draw, new Vector2(size.X - 24 - 18, 34));
                Expect(!window.IsOpen, "the drawn close button closes the tutorial window");
                window.OnClose();
                Expect(Service.Config.TutorialDone, "closing marks the tutorial done (OnClose)");

                // Next and Back.
                Service.Config.TutorialDone = false;
                window = NewTutorial(0);
                window.IsOpen = true;
                Action draw2 = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw2);
                var next = new Vector2(size.X - 24 - 48, size.Y - 30);
                var back = new Vector2(size.X - 24 - 96 - 8 - 48, size.Y - 30);
                Click(s, draw2, back);
                Expect(Field<int>(window, "_stepIndex") == 0, "Back is disabled on the first step");
                Click(s, draw2, next);
                Expect(Field<int>(window, "_stepIndex") == 1, "Next goes to step 2");
                Click(s, draw2, back);
                Expect(Field<int>(window, "_stepIndex") == 0, "Back returns to step 1");
                Expect(!Service.Config.TutorialDone, "moving between steps does not mark the tutorial done");
            }

            // Finish on the last step.
            window = NewTutorial(count - 1);
            window.IsOpen = true;
            using (var s = new Session(options, size))
            {
                Action draw = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw);
                Click(s, draw, new Vector2(size.X - 24 - 48, size.Y - 30));
                Expect(!window.IsOpen && Service.Config.TutorialDone, "Finish on the last step marks the tutorial done and closes it");

                // Copy a macro: the Copy button of the first macro box sits at the card's right edge; find its row by sweeping.
                Service.Config.TutorialDone = false;
                window = NewTutorial(count - 1);
                window.IsOpen = true;
                Action draw3 = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw3);
                Svc.Toasts.Shown.Clear();
                var copiedAt = -1f;
                for (var y = 110f; y < size.Y - 70 && Svc.Toasts.Shown.Count == 0; y += 4)
                {
                    Click(s, draw3, new Vector2(size.X - 24 - 16 - 8 - 36, y));
                    if (Svc.Toasts.Shown.Count > 0)
                        copiedAt = y;
                }

                Expect(Svc.Toasts.Shown.Count == 1 && Svc.Toasts.Shown[0] == "Macro copied to clipboard.", "Copy on a macro shows the copied message");
                string? clip = null;
                try { clip = ImGui.GetClipboardText(); } catch { }
                var macros = (string[])typeof(FirstStartTutorialWindow).GetField("StarterMacros", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                if (clip is not null && clip.Length > 0)
                    Expect(macros.Contains(clip), "the clipboard holds the starter macro that was copied");
                else
                    Console.WriteLine("  note: the clipboard could not be read back on this host; the copied message was checked instead");
                Console.WriteLine($"  (copy button found at y={copiedAt})");
            }

            // A step button opens the PvP settings window on the matching tab (never the legacy window).
            foreach (var stepIndex in new[] { 4, 2 })
            {
                var settings = new PvpSettingsWindow();
                window = NewTutorial(stepIndex);
                window.IsOpen = true;
                using var s = new Session(options, size);
                Action draw = () => Show(window, Vector2.Zero, size, true, true);
                Settle(s, draw);
                var wanted = ((Array)typeof(FirstStartTutorialWindow).GetField("Steps", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).GetValue(stepIndex)!;
                var tab = (string)wanted.GetType().GetProperty("Tab")!.GetValue(wanted)!;
                var pressed = false;
                for (var y = 110f; y < size.Y - 70 && !pressed; y += 4)
                {
                    Click(s, draw, new Vector2(24 + 16 + 60, y));
                    pressed = settings.IsOpen;
                }

                Expect(pressed, $"the step button opens the settings window (step {stepIndex + 1}, {tab})");
                settings.OnOpen();
                Expect(Field<object>(Field<object>(settings, "_shell"), "_currentPage") is string page && page == tab, $"the settings window opens on the {tab} tab");
            }

            // Already open: the tab changes and the window stays open.
            {
                var settings = new PvpSettingsWindow { IsOpen = true };
                settings.OnOpen();
                PvpSettingsWindow.OpenOn("Display");
                Expect(settings.IsOpen && (string)Field<object>(Field<object>(settings, "_shell"), "_currentPage") == "Display", "an open settings window switches to the tab and stays open");
            }
        }

        private static byte[] Sample(Session s, Vector2 at, int width)
        {
            var px = s.Pixels(GreyBackground);
            var i = (((int)at.Y * width) + (int)at.X) * 4;
            return [px[i], px[i + 1], px[i + 2]];
        }

        private static void CheckHud(RenderOptions options)
        {
            var canvas = new Vector2(640, 420);

            // Unlocked: the strip moves the window (the harness mirrors the game's setting that windows move from the title bar only).
            Service.Config = new Configs();
            var control = new StandInControl();
            using (var s = new Session(options, canvas))
            {
                Action draw = () => Show(control, new Vector2(30, 30), new Vector2(570, 300), false, false);
                Settle(s, draw);
                Expect(Math.Abs(Sample(s, new Vector2(400, 200), 640)[0] - 28) <= 1, "unlocked background alpha is kept (0.75 over the canvas)");
                var before = _lastPos;
                Drag(s, draw, new Vector2(30 + 285, 34), new Vector2(30 + 285 + 40, 34 + 30));
                Expect(Vector2.Distance(_lastPos, before + new Vector2(40, 30)) < 1.5f, $"dragging the strip moves the unlocked window by the drag ({before} to {_lastPos})");
            }

            // Locked: no strip, no move, alpha kept.
            Service.Config = new Configs();
            Service.Config.IsControlWindowLock.Value = true;
            var locked = new StandInControl();
            using (var s = new Session(options, canvas))
            {
                Action draw = () => Show(locked, new Vector2(30, 30), new Vector2(570, 300), false, false);
                Settle(s, draw);
                Expect(Math.Abs(Sample(s, new Vector2(400, 200), 640)[0] - 50) <= 2, "locked background alpha is kept (0.55 over the canvas)");
                var before = _lastPos;
                Drag(s, draw, new Vector2(30 + 285, 34), new Vector2(30 + 285 + 40, 34 + 30));
                Expect(Vector2.Distance(_lastPos, before) < 0.5f, "a locked window does not move");
                Expect((locked.Flags & ImGuiWindowFlags.NoTitleBar) != 0 && (locked.Flags & ImGuiWindowFlags.NoMove) != 0, "a locked window has no title bar and stays fixed");
            }

            // The cooldown window shares the base.
            Service.Config = new Configs();
            var cooldown = new StandInCooldown();
            using (var s = new Session(options, canvas))
            {
                Action draw = () => Show(cooldown, new Vector2(30, 30), new Vector2(260, 232), false, false);
                Settle(s, draw);
                var before = _lastPos;
                Drag(s, draw, new Vector2(30 + 130, 34), new Vector2(30 + 130 + 25, 34 + 15));
                Expect(Vector2.Distance(_lastPos, before + new Vector2(25, 15)) < 1.5f, "the cooldown window moves by its strip too");
            }

            // The border is one pixel in the suite's border colour along the top edge, over a more transparent background.
            Service.Config = new Configs();
            var edge = new StandInCooldown();
            using (var s = new Session(options, canvas))
            {
                Action draw = () => Show(edge, new Vector2(30, 30), new Vector2(260, 232), false, false);
                Settle(s, draw);
                var line = Sample(s, new Vector2(80, 30), 640);
                var above = Sample(s, new Vector2(80, 29), 640);
                var below = Sample(s, new Vector2(80, 31), 640);
                Expect(Math.Abs(line[0] - 0x33) <= 3 && Math.Abs(line[1] - 0x36) <= 3 && above[0] == GreyBackground[0] && below[0] < 40,
                    $"a 1 px border in the suite's border colour runs along the top edge ({line[0]},{line[1]},{line[2]})");
            }
        }

        private static void CheckLegacy(RenderOptions options)
        {
            var canvas = new Vector2(900, 640);
            var window = new StandInLegacy { IsOpen = true };
            Expect((window.Flags & ImGuiWindowFlags.NoTitleBar) != 0 && (window.Flags & ImGuiWindowFlags.NoResize) == 0, "the legacy window has no native title bar and stays resizable");
            using var s = new Session(options, canvas);
            Action draw = () => Show(window, Vector2.Zero, new Vector2(740, 490), false, false);
            Settle(s, draw);

            // Reset (the control that used to sit in the title bar) reaches the old reset popup flag.
            var width = _lastSize.X;
            Click(s, draw, new Vector2(width - 24 - 36 - 8 - 40, 34));
            Expect(window.ResetRequested, "the Reset control sets the old reset-popup request");

            // Resize by the corner grip: the band follows the new width and the close button moves with it.
            Drag(s, draw, new Vector2(_lastSize.X - 3, _lastSize.Y - 3), new Vector2(_lastSize.X + 97, _lastSize.Y + 57));
            Expect(Math.Abs(_lastSize.X - 840) < 3 && Math.Abs(_lastSize.Y - 550) < 3, $"the legacy window resizes by its corner ({_lastSize.X}x{_lastSize.Y})");
            Click(s, draw, new Vector2(_lastSize.X - 24 - 18, 34));
            Expect(!window.IsOpen, "the drawn close button closes the legacy window after a resize");
        }
    }
}
