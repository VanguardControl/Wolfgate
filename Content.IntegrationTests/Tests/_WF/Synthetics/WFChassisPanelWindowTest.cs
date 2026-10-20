#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Synthetics;
using Content.Client._WF.Wolfmed.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Synthetics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>
/// The chassis panel window on the client, fed hand-built states: row counts, localized text, which buttons are
/// live, the confirm-by-second-click controls, and where the bay overlay draws.
/// </summary>
[TestFixture]
public sealed class WFChassisPanelWindowTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly string[] UnresolvedPrefixes = { "wf-chassis-", "wolfmed-synthetic-" };

    private static NetEntity Net(int id) => new(id);

    private static WFChassisPartEntry Part(TargetBodyPart slot, string name, int id, Action<WFChassisPartEntry>? edit = null)
    {
        var entry = new WFChassisPartEntry
        {
            Part = Net(id),
            Slot = slot,
            Name = name,
            Attached = true,
            Working = true,
            Integrity = 0.8f,
            Pain = 0.1f,
            CanDetach = slot != TargetBodyPart.Head && slot != TargetBodyPart.Torso,
        };
        edit?.Invoke(entry);
        return entry;
    }

    /// <summary>Ten parts: two arms, two hands, two legs, a head and torso, a detached leg and an isolated foot.</summary>
    private static WFChassisPanelState State(bool bayOpen = false, bool overclocked = false, float power = 0.7f)
    {
        var state = new WFChassisPanelState
        {
            BayOpen = bayOpen,
            Overclocked = overclocked,
            Power = power,
            Fluid = 0.9f,
            Sensors = 0.6f,
            CoreTemperature = 330f,
            Status = "wolfmed-synthetic-line-servo",
        };

        state.Parts.Add(Part(TargetBodyPart.Head, "wolfmed-synthetic-part-head", 1));
        state.Parts.Add(Part(TargetBodyPart.Torso, "wolfmed-synthetic-part-torso", 2));
        state.Parts.Add(Part(TargetBodyPart.LeftArm, "wolfmed-synthetic-part-left-arm", 3,
            e => e.Faults.Add("wolfmed-synthetic-line-servo")));
        state.Parts.Add(Part(TargetBodyPart.RightArm, "wolfmed-synthetic-part-right-arm", 4));
        state.Parts.Add(Part(TargetBodyPart.LeftHand, "wolfmed-synthetic-part-left-hand", 5));
        state.Parts.Add(Part(TargetBodyPart.RightHand, "wolfmed-synthetic-part-right-hand", 6,
            e => e.Working = false));
        state.Parts.Add(Part(TargetBodyPart.LeftLeg, "wolfmed-synthetic-part-left-leg", 7));
        state.Parts.Add(new WFChassisPartEntry
        {
            Slot = TargetBodyPart.RightLeg,
            Name = "wolfmed-synthetic-part-right-leg",
        });
        state.Parts.Add(Part(TargetBodyPart.LeftFoot, "wolfmed-synthetic-part-left-foot", 9,
            e => { e.Isolated = true; e.Working = false; }));
        state.Parts.Add(Part(TargetBodyPart.RightFoot, "wolfmed-synthetic-part-right-foot", 10,
            e => { e.Loose = true; e.Faults.Add("wolfmed-synthetic-line-breach"); e.Faults.Add("wolfmed-synthetic-line-dent"); }));

        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(20), Slot = "eyes", Holder = TargetBodyPart.Head, Name = "optic sensor",
            Health = 0.8f, Band = "wf-chassis-band-normal", CanDisconnect = true,
        });
        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(21), Slot = "posbrain", Holder = TargetBodyPart.Head, Name = "positronic brain",
            Health = 0.4f, Band = "wf-chassis-band-impaired", Vital = true, CanDisconnect = true,
        });
        state.Organs.Add(new WFChassisOrganEntry { Slot = "pump", Holder = TargetBodyPart.Torso });
        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(23), Slot = "battery", Holder = TargetBodyPart.Torso, Name = "power cell",
            Health = 0.6f, Band = "wf-chassis-band-charge",
        });
        return state;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }

    private static IEnumerable<string> Texts(Control root)
    {
        foreach (var control in Descendants(root))
        {
            switch (control)
            {
                case Label { Text: { } label }:
                    yield return label;
                    break;
                case Button { Text: { } button }:
                    yield return button;
                    break;
            }
        }
    }

    private static void AssertResolved(WFChassisPanelWindow window)
    {
        foreach (var text in Texts(window))
        {
            foreach (var prefix in UnresolvedPrefixes)
                Assert.That(text, Does.Not.Contain(prefix), $"'{text}' is a loc key that did not resolve.");
        }
    }

    /// <summary>Row counts, text and which buttons are live.</summary>
    [Test]
    public async Task RowsTextAndButtonsTest()
    {
        WFChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OpenCentered();
            window.Update(State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            Assert.That(panel.Parts, Has.Count.EqualTo(10));
            Assert.That(panel.Organs, Has.Count.EqualTo(4));
            AssertResolved(panel);

            Assert.Multiple(() =>
            {
                string[] states =
                {
                    "OK", "OK", "OK", "OK", "OK", "OFFLINE", "OK", "DETACHED", "ISOLATED", "LOOSE",
                };
                for (var i = 0; i < 10; i++)
                    Assert.That(panel.Parts[i].State.Text, Is.EqualTo(states[i]), $"part {i} state tag");

                Assert.That(panel.Parts[0].Name.Text, Is.EqualTo("HEAD"));
                Assert.That(panel.Parts[2].Faults.Text, Is.EqualTo("SERVO DAMAGE"));
                Assert.That(panel.Parts[9].Faults.Text, Is.EqualTo("FLUID LEAK, PANEL DEFORMED"));
                Assert.That(panel.Parts[2].Integrity.Text, Does.Contain("80%"));
                Assert.That(panel.Parts[2].Load.Text, Is.EqualTo("10%"));
                Assert.That(panel.Parts[7].Integrity.Text, Is.EqualTo("--"), "a detached part has no integrity.");

                // ISOLATE / RESTORE only on arm, hand, leg and foot kinds, and only while attached.
                bool[] isolate = { false, false, true, true, true, true, true, false, true, true };
                // DETACH follows CanDetach.
                bool[] detach = { false, false, true, true, true, true, true, false, true, true };
                for (var i = 0; i < 10; i++)
                {
                    Assert.That(!panel.Parts[i].Isolate.Disabled, Is.EqualTo(isolate[i]), $"part {i} isolate enabled");
                    Assert.That(!panel.Parts[i].Detach.Disabled, Is.EqualTo(detach[i]), $"part {i} detach enabled");
                }

                Assert.That(panel.Parts[2].Isolate.Text, Is.EqualTo("ISOLATE"));
                Assert.That(panel.Parts[8].Isolate.Text, Is.EqualTo("RESTORE"));

                Assert.That(panel.Organs[0].Slot.Text, Is.EqualTo("OPTICS"));
                Assert.That(panel.Organs[0].Name.Text, Is.EqualTo("OPTIC SENSOR"));
                Assert.That(panel.Organs[0].Band.Text, Is.EqualTo("NOMINAL"));
                Assert.That(panel.Organs[1].Slot.Text, Is.EqualTo("POSITRONIC CORE"));
                Assert.That(panel.Organs[1].Vital.Text, Is.EqualTo("VITAL"));
                Assert.That(panel.Organs[1].Band.Text, Is.EqualTo("IMPAIRED"));
                Assert.That(panel.Organs[2].Slot.Text, Is.EqualTo("COOLANT PUMP"));
                Assert.That(panel.Organs[2].Name.Text, Is.EqualTo("EMPTY"));
                Assert.That(panel.Organs[2].Disconnect.Disabled, Is.True, "nothing to disconnect in an empty slot.");
                Assert.That(panel.Organs[3].Slot.Text, Is.EqualTo("POWER CELL"));
                Assert.That(panel.Organs[3].Band.Text, Is.EqualTo("CHARGE"));
                Assert.That(panel.Organs[3].Health.Text, Does.Contain("60%"));
                Assert.That(panel.Organs[0].Disconnect.Disabled, Is.False);
                Assert.That(panel.Organs[3].Disconnect.Disabled, Is.True, "CanDisconnect is false while the bay is shut.");

                var gauges = Texts(panel).ToList();
                Assert.That(gauges, Has.Some.StartWith("POWER "));
                Assert.That(gauges, Has.Some.StartWith("FLUID "));
                Assert.That(gauges, Has.Some.StartWith("SENSOR "));
                Assert.That(gauges, Has.Some.EqualTo("CORE 330 K"));
                Assert.That(gauges, Has.Some.EqualTo("SERVO DAMAGE"), "the status line.");
                Assert.That(panel.BayButton is Button { Text: "OPEN BAY" });
            });

            // No cell, no core temperature: those two header gauges go away.
            var noCell = State(power: -1f);
            noCell.CoreTemperature = -1f;
            panel.Update(noCell);
            var shown = Descendants(panel).OfType<Label>().Where(l => l.Visible).Select(l => l.Text ?? string.Empty).ToList();
            Assert.That(shown.Any(text => text.StartsWith("POWER [")), Is.False, "no cell, no POWER gauge.");
            Assert.That(shown.Any(text => text.StartsWith("CORE")), Is.False, "no core temperature, no CORE gauge.");
            Assert.That(shown.Any(text => text.StartsWith("FLUID [")), Is.True);
            panel.Update(State(bayOpen: true));
            Assert.That(panel.Parts, Has.Count.EqualTo(10), "the same count updates in place.");
            Assert.That(((Button) panel.BayButton).Text, Is.EqualTo("CLOSE BAY"));
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>
    /// A changed number of entries rebuilds the rows, and a slot with no loc key shows its raw id. In-place updates
    /// keep the same controls so they do not flicker.
    /// </summary>
    [Test]
    public async Task RowsUpdateInPlaceTest()
    {
        await Client.WaitAssertion(() =>
        {
            using var panel = new WFChassisPanelWindow();
            panel.Update(State());
            var rows = panel.Parts.ToArray();
            var buttons = panel.Parts.Select(r => r.Isolate).ToArray();

            var next = State();
            next.Parts[2].Isolated = true;
            panel.Update(next);
            Assert.That(panel.Parts, Is.EqualTo(rows), "the same count must reuse the rows.");
            Assert.That(panel.Parts.Select(r => r.Isolate), Is.EqualTo(buttons));
            Assert.That(panel.Parts[2].Isolate.Text, Is.EqualTo("RESTORE"));

            next.Organs.Add(new WFChassisOrganEntry { Organ = Net(30), Slot = "custom_slot", Name = "odd part" });
            next.Parts.RemoveAt(9);
            panel.Update(next);
            Assert.That(panel.Parts, Has.Count.EqualTo(9));
            Assert.That(panel.Organs, Has.Count.EqualTo(5));
            Assert.That(panel.Organs[4].Slot.Text, Is.EqualTo("custom_slot"), "an unknown slot falls back to its id.");
            Assert.That(panel.Organs[4].Health.Text, Is.EqualTo("--"), "no health, no gauge.");
            AssertResolved(panel);
        });
    }

    /// <summary>Every control sends its message; the dangerous ones need a second click on the same control.</summary>
    [Test]
    public async Task ControlsSendMessagesTest()
    {
        WFChassisPanelWindow? window = null;
        var isolates = new List<(NetEntity, bool)>();
        var detaches = new List<NetEntity>();
        var disconnects = new List<NetEntity>();
        var bay = 0;
        var reboots = 0;
        var overclocks = new List<bool>();

        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OnIsolate += (part, isolate) => isolates.Add((part, isolate));
            window.OnDetach += detaches.Add;
            window.OnDisconnect += disconnects.Add;
            window.OnToggleBay += () => bay++;
            window.OnReboot += () => reboots++;
            window.OnOverclock += overclocks.Add;
            window.OpenCentered();
            window.Update(State(bayOpen: true));
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;

            WFButtonTestInput.Click(panel.Parts[2].Isolate);
            WFButtonTestInput.Click(panel.Parts[8].Isolate);
            WFButtonTestInput.Click(panel.Parts[0].Isolate);
            Assert.That(isolates, Is.EqualTo(new[] { (Net(3), true), (Net(9), false) }),
                "isolate and restore are sent; a disabled head button sends nothing.");

            WFButtonTestInput.Click(panel.Parts[3].Detach);
            WFButtonTestInput.Click(panel.Parts[1].Detach);
            Assert.That(detaches, Is.EqualTo(new[] { Net(4) }));

            // A plain component goes at once; the core asks twice.
            WFButtonTestInput.Click(panel.Organs[0].Disconnect);
            Assert.That(disconnects, Is.EqualTo(new[] { Net(20) }));

            var core = panel.Organs[1].Disconnect;
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(1), "the first click on a vital component only arms it.");
            Assert.That(core.Text, Is.EqualTo("CONFIRM"));

            // Another confirm control takes the arming away.
            WFButtonTestInput.Click(panel.RebootControl);
            Assert.That(reboots, Is.EqualTo(0));
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "arming reboot disarms the component.");
            Assert.That(((Button) panel.RebootControl).Text, Is.EqualTo("CONFIRM"));
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(1), "a second click after the arming moved only re-arms it.");

            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Is.EqualTo(new[] { Net(20), Net(21) }));
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "confirming disarms.");

            WFButtonTestInput.Click(panel.RebootControl);
            WFButtonTestInput.Click(panel.RebootControl);
            Assert.That(reboots, Is.EqualTo(1));

            WFButtonTestInput.Click(panel.BayButton);
            Assert.That(bay, Is.EqualTo(1));

            WFButtonTestInput.Click(panel.OverclockControl);
            Assert.That(overclocks, Is.EqualTo(new[] { true }));
            Assert.That(panel.OverclockControl.Pressed, Is.True);
            panel.Update(State(bayOpen: true, overclocked: false));
            Assert.That(panel.OverclockControl.Pressed, Is.False, "the toggle follows the server's state.");

            // Arm the core again and let the window lapse.
            WFButtonTestInput.Click(core);
            Assert.That(core.Text, Is.EqualTo("CONFIRM"));
        });

        await Task.Delay(TimeSpan.FromSeconds(WFChassisPanelWindow.ConfirmSeconds + 0.3));
        await RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var core = window!.Organs[1].Disconnect;
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "the arming lapses after the confirm window.");
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(2), "a late second click only re-arms.");
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>The bay strip sits in the gap above the hotbar and clear of every box the readout draws.</summary>
    [Test]
    public void BayStripClearsTheReadoutTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (width, height) in new[] { (1920, 1080), (1280, 720), (1600, 900) })
            {
                var screen = new UIBox2(0f, 0f, width, height);
                var strip = WFChassisBayLayout.Strip(screen);
                Assert.That(WolfmedSyntheticHudLayout.Contains(screen, strip), Is.True, $"strip inside {width}x{height}");

                foreach (var (name, box) in WolfmedSyntheticHudLayout.Reserved(screen))
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(strip, box), Is.False, $"strip overlaps {name} at {width}x{height}");

                foreach (var ui in new[] { 1f, 1.5f, 2f })
                {
                    var scale = WolfmedSyntheticHudLayout.FitScale(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    foreach (var (name, box) in WolfmedSyntheticHudLayout.DrawnBoxes(screen, scale,
                                 WolfmedSyntheticHudLayout.MaxSystemGauges, WolfmedSyntheticHudLayout.MaxAdviceRows, 8))
                        Assert.That(WolfmedSyntheticHudLayout.Overlaps(strip, box), Is.False,
                            $"strip overlaps the readout's {name} at {width}x{height}, ui scale {ui}");

                    var tag = WFChassisBayLayout.Tag(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    Assert.That(WolfmedSyntheticHudLayout.Contains(screen, tag), Is.True, $"tag inside {width}x{height}, ui scale {ui}");
                }
            }
        });
    }
}
