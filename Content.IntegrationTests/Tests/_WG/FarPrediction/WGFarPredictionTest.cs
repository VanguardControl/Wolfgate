#nullable enable
using System.Numerics;
using Content.Client._WG.FarPrediction;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._WG.FarPrediction;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Controllers;

namespace Content.IntegrationTests.Tests._WG.FarPrediction;

/// <summary>
/// The client steadies its own predicted walking on a grid far from the map origin, and only there, without
/// changing where the player ends up, walking through walls or undoing what other controllers do.
/// </summary>
public sealed class WGFarPredictionTest : MovementTest
{
    /// <summary>
    /// Moves an entity a little before every predicted solve, the way climbing moves a climber, and checks after the
    /// solve whether the move is still there.
    /// </summary>
    public sealed class NudgeTestController : VirtualController
    {
        public EntityUid Target;
        public float PerStep;
        public int Kept;
        public int Undone;

        private float? _nudgedTo;

        public override void UpdateBeforeSolve(bool prediction, float frameTime)
        {
            base.UpdateBeforeSolve(prediction, frameTime);
            _nudgedTo = null;

            if (!prediction || !Target.IsValid() || !TryComp(Target, out TransformComponent? xform))
                return;

            var nudged = xform.LocalPosition + new Vector2(0f, PerStep);
            TransformSystem.SetLocalPosition(Target, nudged, xform);
            _nudgedTo = nudged.Y;
        }

        public override void UpdateAfterSolve(bool prediction, float frameTime)
        {
            base.UpdateAfterSolve(prediction, frameTime);

            if (_nudgedTo is not { } nudgedTo || !TryComp(Target, out TransformComponent? xform))
                return;

            // The walker only moves along X, so Y is still where the nudge left it unless something undid it.
            if (MathF.Abs(xform.LocalPosition.Y - nudgedTo) < PerStep / 4f)
                Kept++;
            else
                Undone++;
        }
    }

    private static readonly Vector2 Deep = new(76_000f, 28_000f);

    private WGFarPredictionSystem Corrector => CEntMan.System<WGFarPredictionSystem>();

    private async Task PlaceGrid(Vector2 position, Angle rotation)
    {
        await Server.WaitPost(() =>
        {
            Transform.SetWorldPositionRotation(MapData.Grid, position, rotation);
        });
        await RunTicks(10);
    }

    private Vector2 ServerLocal() => SEntMan.GetComponent<TransformComponent>(SPlayer).LocalPosition;

    private Vector2 ClientLocal() => CEntMan.GetComponent<TransformComponent>(CPlayer).LocalPosition;

    [Test]
    public async Task WalkingNearTheOriginIsLeftAlone()
    {
        var before = Corrector.Corrected;

        await Move(DirectionFlag.East, 0.5f);

        Assert.That(Corrector.Corrected, Is.EqualTo(before));
    }

    [Test]
    public async Task WalkingInTheDeepIsCorrectedAndAgreesWithTheServer()
    {
        await PlaceGrid(Deep, Angle.FromDegrees(30));
        var before = Corrector.Corrected;
        var start = ServerLocal();

        await Move(DirectionFlag.East, 0.5f);
        await RunTicks(10);

        Assert.Multiple(() =>
        {
            Assert.That(Corrector.Corrected, Is.GreaterThan(before), "no predicted step was corrected");
            Assert.That((ServerLocal() - start).Length(), Is.GreaterThan(0.5f), "the player did not walk");
            Assert.That((ClientLocal() - ServerLocal()).Length(), Is.LessThan(0.01f), "client and server disagree");
        });
    }

    [Test]
    public async Task WallsStillStopAWalkerInTheDeep()
    {
        await PlaceGrid(Deep, Angle.FromDegrees(30));
        var start = ServerLocal();

        await Move(DirectionFlag.East, 3f);
        await RunTicks(10);

        // The wall stands three tiles along; a walker stops against its near face.
        Assert.Multiple(() =>
        {
            Assert.That((ClientLocal() - start).Length(), Is.InRange(1.5f, 2.6f), "the client walked through or short of the wall");
            Assert.That((ServerLocal() - start).Length(), Is.InRange(1.5f, 2.6f), "the server walked through or short of the wall");
            Assert.That((ClientLocal() - ServerLocal()).Length(), Is.LessThan(0.01f), "client and server disagree");
        });
    }

    [Test]
    public async Task SwitchedOffNothingIsCorrected()
    {
        await PlaceGrid(Deep, Angle.Zero);
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(WGFarPredictionCVars.Enabled, false));
        await RunTicks(5);
        var before = Corrector.Corrected;

        await Move(DirectionFlag.East, 0.5f);

        var after = Corrector.Corrected;
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(WGFarPredictionCVars.Enabled, true));
        await RunTicks(5);

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public async Task AnotherControllersOwnMoveIsKept()
    {
        await PlaceGrid(Deep, Angle.Zero);
        var nudge = CEntMan.System<NudgeTestController>();

        // Two millimetres a step is far inside what counts as rounding out here, so only the order of the two
        // systems keeps it: the corrector must note positions after the nudge, not before.
        await Client.WaitPost(() =>
        {
            nudge.Kept = 0;
            nudge.Undone = 0;
            nudge.PerStep = 0.002f;
            nudge.Target = CPlayer;
        });
        await Move(DirectionFlag.East, 0.5f);
        await Client.WaitPost(() => nudge.Target = EntityUid.Invalid);
        await RunTicks(5);

        Assert.Multiple(() =>
        {
            Assert.That(nudge.Kept, Is.GreaterThan(10), "the nudge never ran in prediction");
            Assert.That(nudge.Undone, Is.Zero, "the nudge was undone");
        });
    }
}
