using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Forensics;
using Content.Server.Popups;
using Content.Server.Stunnable;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.IdentityManagement;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.StatusEffect;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Priv.Medical
{
    public sealed partial class PissSystem : EntitySystem
    {
        [Dependency] private IPrototypeManager _proto = default!;
        [Dependency] private AudioSystem _audio = default!;
        [Dependency] private BodySystem _body = default!;
        [Dependency] private HungerSystem _hunger = default!;
        [Dependency] private PopupSystem _popup = default!;
        [Dependency] private PuddleSystem _puddle = default!;
        [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
        [Dependency] private StunSystem _stun = default!;
        [Dependency] private ThirstSystem _thirst = default!;
        [Dependency] private ForensicsSystem _forensics = default!;
        [Dependency] private BloodstreamSystem _bloodstream = default!;

        private static readonly ProtoId<SoundCollectionPrototype> PissCollection = "Vomit";

        private readonly SoundSpecifier _pissSound = new SoundCollectionSpecifier(PissCollection,
            AudioParams.Default.WithVariation(0.2f).WithVolume(-4f));

        /// <summary>
        /// Make an entity piss, if they have a stomach.
        /// </summary>
        public void Piss(EntityUid uid, float thirstAdded = -10f)
        {
            // Main requirement: You have a stomach
            // WOLFGATE: no body, no stomach; a hairball hitting a bodiless mob logged a resolve error
            if (!HasComp<Content.Shared.Body.Components.BodyComponent>(uid))
                return;

            var stomachList = _body.GetBodyOrganEntityComps<StomachComponent>(uid);
            if (stomachList.Count == 0)
                return;

             // Pissing makes you thirstier
            if (TryComp<ThirstComponent>(uid, out var thirst))
                _thirst.ModifyThirst(uid, thirst, thirstAdded);

            // It fully empties the stomach, this amount from the chem stream is relatively small
            var solutionSize = (MathF.Abs(thirstAdded)) * 2;
            // Apply a bit of slowdown
            if (TryComp<StatusEffectsComponent>(uid, out var status))
                _stun.TrySlowdown(uid, TimeSpan.FromSeconds(5), true, 0.9f, 0.9f, status);

            // TODO: Need decals
            var solution = new Solution();

            // Adds a tiny amount of the chem stream from earlier along with vomit
            if (TryComp<BloodstreamComponent>(uid, out var bloodStream))
            {
                const float chemMultiplier = 0.1f;

                var pissAmount = solutionSize;

                // Takes 10% of the chemicals removed from the chem stream
                if (_solutionContainer.ResolveSolution(uid, bloodStream.ChemicalSolutionName, ref bloodStream.ChemicalSolution))
                {
                    var pissChemstreamAmount = _solutionContainer.SplitSolution(bloodStream.ChemicalSolution.Value, pissAmount);
                    pissChemstreamAmount.ScaleSolution(chemMultiplier);
                    solution.AddSolution(pissChemstreamAmount, _proto);

                    pissAmount -= (float)pissChemstreamAmount.Volume;
                }

                // Makes a vomit solution the size of 90% of the chemicals removed from the chemstream
                solution.AddReagent(new ReagentId("Urine", _bloodstream.GetEntityBloodData(uid)), pissAmount);
            }

            if (_puddle.TrySpillAt(uid, solution, out var puddle, false))
            {
                _forensics.TransferDna(puddle, uid, false);
            }

            // Force sound to play as spill doesn't work if solution is empty.
            _audio.PlayPvs(_pissSound, uid);
        }
    }
}
