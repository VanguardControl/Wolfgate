#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingTools;

[TestFixture]
public sealed class MappingToolsPipesTest
{
    private static Vector2i V(int x, int y) => new(x, y);

    private static List<Vector2i> Path(params Vector2i[] cells)
    {
        var path = new List<Vector2i>();
        foreach (var cell in cells)
        {
            Assert.That(MappingToolsPipes.Extend(path, cell, 4096), Is.True);
        }

        return path;
    }

    private static MappingPipePlan Plan(List<Vector2i> path, Dictionary<Vector2i, MappingExistingPipe> existing,
        bool autoJoin = true, bool replace = true)
    {
        return MappingToolsPipes.Plan(path, c => existing.TryGetValue(c, out var pipe) ? pipe : null, autoJoin, replace);
    }

    private static PipeDirection PlacedAt(MappingPipePlan plan, Vector2i cell)
    {
        return plan.Placed.Single(p => p.Cell == cell).Directions;
    }

    [Test]
    public void PathFollowsTheCursor()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 0), V(1, 0), V(2, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0) }),
                "A repeated cell adds nothing.");
            Assert.That(Path(V(0, 0), V(3, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0) }),
                "A jump along an axis is filled in.");
        });
    }

    [Test]
    public void DiagonalJumpsAreBridgedWithAnL()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 1)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(1, 1) }),
                "From one cell the L goes along X first.");
            Assert.That(Path(V(0, 0), V(0, 1), V(1, 2)), Is.EqualTo(new[] { V(0, 0), V(0, 1), V(0, 2), V(1, 2) }),
                "After a vertical step the L keeps going up first.");
            Assert.That(Path(V(0, 0), V(1, 0), V(3, -2)),
                Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(3, -1), V(3, -2) }));
        });

        var path = Path(V(0, 0), V(4, 3), V(-2, 5));
        Assert.That(path, Is.Unique);
        for (var i = 1; i < path.Count; i++)
        {
            Assert.That(MappingToolsPipes.Side(path[i - 1], path[i]), Is.Not.EqualTo(PipeDirection.None),
                $"Step {i} goes to a side neighbour.");
        }
    }

    [Test]
    public void ComingBackCutsThePath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Path(V(0, 0), V(1, 0), V(2, 0), V(1, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0) }));
            Assert.That(Path(V(0, 0), V(2, 0), V(2, 2), V(0, 2), V(0, 0)), Is.EqualTo(new[] { V(0, 0) }),
                "Closing a loop cuts it back to the start.");
        });
    }

    [Test]
    public void PathStopsAtTheCap()
    {
        var path = new List<Vector2i>();
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.Extend(path, V(0, 0), 3), Is.True);
            Assert.That(MappingToolsPipes.Extend(path, V(5, 0), 3), Is.False);
            Assert.That(path, Has.Count.EqualTo(3));
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(9, 0) }, 4), Is.Null);
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(int.MaxValue / 2, 0) }, 4096), Is.Null,
                "A huge jump is refused before it is walked.");
            Assert.That(MappingToolsPipes.Normalise(new[] { V(0, 0), V(1, 1) }, 4096),
                Is.EqualTo(new[] { V(0, 0), V(1, 0), V(1, 1) }));
        });
    }

    [Test]
    public void EverySetOfSidesHasAShape()
    {
        var expected = new Dictionary<PipeDirection, (PipeShape Shape, int Degrees)>
        {
            [PipeDirection.South] = (PipeShape.Half, 0),
            [PipeDirection.East] = (PipeShape.Half, 90),
            [PipeDirection.North] = (PipeShape.Half, 180),
            [PipeDirection.West] = (PipeShape.Half, 270),
            [PipeDirection.Longitudinal] = (PipeShape.Straight, 0),
            [PipeDirection.Lateral] = (PipeShape.Straight, 90),
            [PipeDirection.SWBend] = (PipeShape.Bend, 0),
            [PipeDirection.SEBend] = (PipeShape.Bend, 90),
            [PipeDirection.NEBend] = (PipeShape.Bend, 180),
            [PipeDirection.NWBend] = (PipeShape.Bend, 270),
            [PipeDirection.TSouth] = (PipeShape.TJunction, 0),
            [PipeDirection.TEast] = (PipeShape.TJunction, 90),
            [PipeDirection.TNorth] = (PipeShape.TJunction, 180),
            [PipeDirection.TWest] = (PipeShape.TJunction, 270),
            [PipeDirection.Fourway] = (PipeShape.Fourway, 0),
        };

        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.TryShape(PipeDirection.None, out _, out _), Is.False);
            for (var bits = 1; bits < 16; bits++)
            {
                var sides = (PipeDirection) bits;
                Assert.That(MappingToolsPipes.TryShape(sides, out var shape, out var rotation), Is.True, $"{sides}");
                Assert.That((shape, (int) Math.Round(rotation.Degrees)), Is.EqualTo(expected[sides]), $"{sides}");
                Assert.That(shape.ToBaseDirection().RotatePipeDirection(rotation.Theta), Is.EqualTo(sides),
                    $"{sides}: the turned pipe opens to exactly those sides.");
            }
        });
    }

    [Test]
    public void ShapesHavePrototypes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Half), Is.EqualTo("GasPipeHalf"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Straight), Is.EqualTo("GasPipeStraight"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Bend), Is.EqualTo("GasPipeBend"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.TJunction), Is.EqualTo("GasPipeTJunction"));
            Assert.That(MappingToolsPipes.PrototypeFor(PipeShape.Fourway), Is.EqualTo("GasPipeFourway"));
        });
    }

    [Test]
    public void StraightRunEndsInHalves()
    {
        var plan = Plan(Path(V(0, 0), V(3, 0)), new());
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed, Has.Count.EqualTo(4));
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(3, 0)), Is.EqualTo(PipeDirection.West));
            Assert.That(plan.Upgraded, Is.Empty);
            Assert.That(plan.Kept, Is.Zero);
        });
    }

    [Test]
    public void TurnsAreBends()
    {
        var plan = Plan(Path(V(0, 0), V(1, 0), V(1, 1)), new());
        Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.NWBend));
    }

    [Test]
    public void LoneCellNeedsSomethingToJoin()
    {
        var plan = Plan(Path(V(0, 0)), new());
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed, Is.Empty);
            Assert.That(plan.Kept, Is.EqualTo(1));
        });

        var vent = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 1)] = new(PipeDirection.South, false) };
        plan = Plan(Path(V(0, 0)), vent);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.North), "A click under a device facing it joins it.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(0, 1) }));
        });
    }

    [Test]
    public void PipesPointingAtThePathJoinIt()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe>
        {
            [V(1, 1)] = new(PipeDirection.South, true),
            [V(2, -1)] = new(PipeDirection.Lateral, true),
        };

        var plan = Plan(Path(V(0, 0), V(3, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.TNorth), "The half above points at it.");
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.Lateral), "A pipe running alongside stays apart.");
            Assert.That(plan.Upgraded, Is.Empty, "Only the ends upgrade pipes.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(1, 1) }));
        });

        plan = Plan(Path(V(0, 0), V(3, 0)), existing, autoJoin: false);
        Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Lateral), "Without auto-join nothing joins.");
    }

    [Test]
    public void EndsUpgradePlainPipesPastThem()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe>
        {
            [V(-1, 0)] = new(PipeDirection.Longitudinal, true),
            [V(4, 0)] = new(PipeDirection.TEast, true),
            [V(1, -1)] = new(PipeDirection.TWest, true),
        };

        var plan = Plan(Path(V(0, 0), V(3, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(PlacedAt(plan, V(3, 0)), Is.EqualTo(PipeDirection.Lateral));
            Assert.That(plan.Upgraded, Is.EquivalentTo(new[]
            {
                new MappingPipeCell(V(-1, 0), PipeDirection.TEast),
                new MappingPipeCell(V(4, 0), PipeDirection.Fourway),
            }), "Straight to T, T to four-way.");
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.TSouth), "The T below opens up into the run.");
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(-1, 0), V(1, -1), V(4, 0) }));
        });

        var bend = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 3)] = new(PipeDirection.NEBend, true) };
        plan = Plan(Path(V(0, 0), V(0, 2)), bend);
        Assert.That(plan.Upgraded.Single(), Is.EqualTo(new MappingPipeCell(V(0, 3), PipeDirection.TEast)),
            "A bend becomes a T.");

        var open = new Dictionary<Vector2i, MappingExistingPipe> { [V(0, 3)] = new(PipeDirection.SEBend, true) };
        plan = Plan(Path(V(0, 0), V(0, 2)), open);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Upgraded, Is.Empty, "A pipe already open towards the end joins as it is.");
            Assert.That(PlacedAt(plan, V(0, 2)), Is.EqualTo(PipeDirection.Longitudinal));
        });

        var device = new Dictionary<Vector2i, MappingExistingPipe> { [V(-1, 0)] = new(PipeDirection.Longitudinal, false) };
        plan = Plan(Path(V(0, 0), V(3, 0)), device);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Upgraded, Is.Empty, "A device isn't rebuilt.");
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
        });

        plan = Plan(Path(V(0, 0), V(3, 0)), existing, autoJoin: false);
        Assert.That(plan.Upgraded, Is.Empty, "Without auto-join nothing upgrades.");
    }

    [Test]
    public void PipesOnThePathMergeOrStay()
    {
        var existing = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Longitudinal, true) };

        var plan = Plan(Path(V(0, 0), V(2, 0)), existing);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(1, 0)), Is.EqualTo(PipeDirection.Fourway), "Replacing adds the old sides.");
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East));
            Assert.That(plan.Joined, Is.EqualTo(new[] { V(1, 0) }));
        });

        plan = Plan(Path(V(0, 0), V(2, 0)), existing, replace: false);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Placed, Is.Empty, "The kept straight doesn't face the cells beside it.");
            Assert.That(plan.Kept, Is.EqualTo(3));
        });

        var facing = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Lateral, true) };
        plan = Plan(Path(V(0, 0), V(2, 0)), facing, replace: false);
        Assert.Multiple(() =>
        {
            Assert.That(PlacedAt(plan, V(0, 0)), Is.EqualTo(PipeDirection.East), "A kept pipe that faces the path joins it.");
            Assert.That(PlacedAt(plan, V(2, 0)), Is.EqualTo(PipeDirection.West));
            Assert.That(plan.Kept, Is.EqualTo(1));
        });

        var device = new Dictionary<Vector2i, MappingExistingPipe> { [V(1, 0)] = new(PipeDirection.Lateral, false) };
        plan = Plan(Path(V(0, 0), V(2, 0)), device);
        Assert.That(plan.Placed.Select(p => p.Cell), Is.EqualTo(new[] { V(0, 0), V(2, 0) }),
            "A device on the path is never rebuilt, even when replacing.");
    }
}
