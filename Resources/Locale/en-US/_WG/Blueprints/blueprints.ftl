wg-blueprint-locator-title = Artifact Blueprint Locator
wg-blueprint-locator-status-unlinked = No research server linked.
wg-blueprint-locator-status-ready = Ready to locate.
wg-blueprint-locator-status-hunting = Hunt in progress.
wg-blueprint-locator-status-cooldown = Recalibrating: {$minutes} min.
wg-blueprint-locator-count = {$known}/{$total} known
wg-blueprint-locator-search = Search patterns
wg-blueprint-locator-tier-all = All
wg-blueprint-locator-hide-known = Hide known
wg-blueprint-locator-none-shown = Nothing matches.
wg-blueprint-locator-pick-one = Pick a technology or relic to see what it unlocks.
    Use a shard on this console to decode it, then its Construct to unlock it.
wg-blueprint-locator-prints = Prints
wg-blueprint-locator-material = {$name} ×{$amount}
wg-blueprint-locator-detail-known = This server already knows this pattern.
wg-blueprint-locator-detail-picked = In the request.
wg-blueprint-locator-add = Add to request
wg-blueprint-locator-remove = Remove from request
wg-blueprint-locator-request = Request
wg-blueprint-locator-chip = {$name} ✕
wg-blueprint-locator-chip-tooltip = Remove from the request
wg-blueprint-locator-hunt-header = Active hunt
wg-blueprint-locator-hunt = [bold]{$site}[/bold] on {$world} (tier {$tier}): {$patterns}. Shards still out: {$remaining}.
wg-blueprint-locator-abandon = Abandon
wg-blueprint-locator-locate = Locate
wg-blueprint-locator-pick = Pick up to {$max} patterns of one tier.
wg-blueprint-locator-fee = Fee: { $points ->
    [0] {$spesos} spesos
   *[other] { $spesos ->
        [0] {$points} research points
       *[other] {$points} research points + {$spesos} spesos
    }
} ({$count}/{$max})
wg-blueprint-tier = { $tier ->
    [1] Tier I: Recovered
    [2] Tier II: Lost
   *[other] Tier III: Forgotten
}
wg-blueprint-tier-short = { $tier ->
    [1] I
    [2] II
   *[other] III
}
wg-blueprint-pattern-owned = {$name} (known)
wg-blueprint-tech-desc = Lost {$discipline} research. Recover its pattern to unlock the whole node.
lathe-category-wg-patterns = Patterns

wg-blueprint-locator-busy = A hunt is already running.
wg-blueprint-locator-cooldown = The locator is still recalibrating.
wg-blueprint-locator-unlinked = The locator isn't linked to a research server.
wg-blueprint-locator-invalid = Pick up to four unknown patterns of one tier.
wg-blueprint-locator-no-site = No site of that tier is free right now.
wg-blueprint-locator-prerequisites = Recover the technology's prerequisites first.
wg-blueprint-locator-no-funds = The server's research points and your bank can't cover the locate fee.
wg-blueprint-locator-started = Shards located at {$site} on {$world}.
wg-blueprint-locator-abandoned = Hunt abandoned.

wg-blueprint-shard-name = pattern shard ({$pattern})
wg-blueprint-shard-examine = It holds part of the [color=#d070ff]{$pattern}[/color] pattern, tier {$tier}.
wg-blueprint-construct-name = blueprint construct ({$pattern})
wg-blueprint-construct-examine = It holds the [color=#d070ff]{$pattern}[/color] pattern.
wg-blueprint-construct-owned = This ship's server already knows that pattern.
wg-blueprint-construct-unlocked = {$pattern} unlocked. Print it at the protolathe.

wg-blueprint-decode-unpowered = The console has no power.
wg-blueprint-decode-start = The console starts reading the shard.
wg-blueprint-decode-done = The {$pattern} pattern is sealed into a Construct.

wg-pattern-omnitool = Omnitool
wg-pattern-omnitool-desc = A drone's multi-mode tool. Every common tool in one.
wg-pattern-jetpack-black = Black jetpack
wg-pattern-jetpack-black-desc = A sleek jetpack that fits a suit's storage slot.
wg-pattern-thermal-glasses = Optical thermal scanner
wg-pattern-thermal-glasses-desc = Thermals in the shape of glasses. See the living through walls.
wg-pattern-hand-teleporter = Hand teleporter
wg-pattern-hand-teleporter-desc = Opens a linked pair of portals. Builder work, older than the anchors.

wg-pattern-admin-glasses = Administration glasses
wg-pattern-admin-glasses-desc = Sunglasses with flash immunity that also show everyone's job icon.
wg-pattern-armored-hardhat = Armored hard hat
wg-pattern-armored-hardhat-desc = A hard hat with a light and security helmet armor. Work and fight in one hat.
wg-pattern-knockback-stick = Knockback stick
wg-pattern-knockback-stick-desc = Barely hurts, but sends whoever you hit flying. Good for clearing a doorway.
wg-pattern-clockwork-shield = Clockwork shield
wg-pattern-clockwork-shield-desc = A brass shield that blocks brute damage better than a riot shield, but burns easily.
wg-pattern-rad-barrier = Radiation barrier projector
wg-pattern-rad-barrier-desc = Projects holo barriers that block radiation. Buys time during a reactor meltdown.
wg-pattern-radio-mic = Radio microphone
wg-pattern-radio-mic-desc = Talk into it and your voice goes out on the radio show channel to every radio tuned in.
wg-pattern-thungerst-goggles = Thungerst goggles
wg-pattern-thungerst-goggles-desc = Shows how hungry and thirsty everyone around you is. A cook's best friend.
wg-pattern-multiversal-hud = Multiversal HUD
wg-pattern-multiversal-hud-desc = Security, medical and syndicate icons in one HUD, with health bars for organics and synths.
wg-pattern-ironclad = Ironclad II armor
wg-pattern-ironclad-desc = Old pod wars plate and helmet. Halves most damage and stops you tripping mines.
wg-pattern-pirate-captain-hardsuit = Pirate captain's hardsuit
wg-pattern-pirate-captain-hardsuit-desc = A heavy armored hardsuit with strong heat and blast protection. Slow, but hard to kill.
wg-pattern-mirror-shield = Mirror shield
wg-pattern-mirror-shield-desc = Reflects almost every laser shot back. Brittle against everything else.
wg-pattern-hot-potato = Hot potato
wg-pattern-hot-potato-desc = A bomb that sticks to your hand once armed. Hit someone to pass it on before it blows.
wg-pattern-laser-lmg = EL-20 laser LMG
wg-pattern-laser-lmg-desc = A full-auto laser machine gun fed from a backpack battery. Print spare batteries too.
wg-pattern-covert-armor = Covert armor
wg-pattern-covert-armor-desc = Armor vest that is invisible when worn. Halves damage and soaks most of a blast.
wg-pattern-behonker-eye = Behonker eye
wg-pattern-behonker-eye-desc = A laser pistol that recharges on its own. Never needs a charger.
wg-pattern-wet-floor-mine = Wet floor sign
wg-pattern-wet-floor-mine-desc = Looks like a wet floor sign. Explodes when someone steps on it.
wg-pattern-experimental-rcd = Experimental RCD
wg-pattern-experimental-rcd-desc = An RCD that makes its own compressed matter. Build and tear down without ever refilling.
wg-pattern-hypereutactic-blade = Hypereutactic blade
wg-pattern-hypereutactic-blade-desc = A two-handed energy blade that hits harder and wider than a double esword, but slows you.
wg-pattern-minigun = Minigun
wg-pattern-minigun-desc = A thousand rounds at fifteen shots a second. Nothing else in the sector puts out this much lead.
wg-pattern-rocket-salvo = Multiple rocket launcher
wg-pattern-rocket-salvo-desc = A rocket launcher that fires full-auto. Empties a rack of rockets in seconds.
wg-pattern-power-sink = Power sink
wg-pattern-power-sink-desc = Wrench it to a high-voltage cable and it drains the whole grid until it explodes.
wg-pattern-healing-staff = Staff of healing
wg-pattern-healing-staff-desc = Each bolt fully heals whoever it hits. Recharges on its own.
wg-pattern-cell-revolver = Cell laser revolver
wg-pattern-cell-revolver-desc = A compact laser sidearm that runs on swappable power cells. Prints the gun and one cell.
wg-pattern-century-tacsuit = Century tacsuit
wg-pattern-century-tacsuit-desc = Combat hardsuit with a spall liner that stops bullet fragments lodging in the wearer.
wg-pattern-armored-eva = Armored EVA suit
wg-pattern-armored-eva-desc = A light EVA suit with a sealed layer against chemical burns and frostbite.
wg-pattern-breaching-anchor = Breaching anchor
wg-pattern-breaching-anchor-desc = A huge two-handed anchor that pries doors open and smashes structures when wielded.
wg-pattern-mining-pulsar = Mining pulsar
wg-pattern-mining-pulsar-desc = A ship-mounted mining gun that breaks up rock. Prints as a flatpack.
wg-pattern-cell-smg = Cell laser SMG
wg-pattern-cell-smg-desc = A rapid-fire laser SMG that runs on swappable power cells. Prints the gun and one cell.
wg-pattern-cell-sniper = Cell laser sniper
wg-pattern-cell-sniper-desc = A burst-fire laser sniper rifle that runs on swappable power cells. Prints the gun and one cell.
wg-pattern-ashstorm-mg = Ashstorm laser MG
wg-pattern-ashstorm-mg-desc = A wielded laser machine gun for sustained suppressive fire. Comes with its own cell.
wg-pattern-overcharged-cells = Overclocked laser cells
wg-pattern-overcharged-cells-desc = Cells that make the cell lasers hit harder and burn out faster. One each for revolver, SMG and sniper.
wg-pattern-mantis-blades = Mantis blade implants
wg-pattern-mantis-blades-desc = Autosurgeons that fit retractable arm blades. One for each arm.
wg-pattern-sandevistan = Sandevistan implant
wg-pattern-sandevistan-desc = An autosurgeon that swaps your heart for a reflex booster. Bursts of speed, then a long cooldown.
wg-pattern-centurion-tacsuit = Centurion tacsuit
wg-pattern-centurion-tacsuit-desc = Commander's hardsuit with bone plating and a blast liner. Tougher than the Century.
wg-pattern-director-suit = Director hardsuit
wg-pattern-director-suit-desc = Heavy hardsuit with ricochet plating. Light rounds glance off and radiation can't get in.
wg-pattern-elite-pirate-suit = Elite pirate hardsuit
wg-pattern-elite-pirate-suit-desc = Heavy pirate hardsuit with a limb lock that keeps its arms and legs working through wounds.
wg-pattern-emp-ammo = EMP ammo boxes
wg-pattern-emp-ammo-desc = Ammo that fires small EMP bursts. Good against synthetics and ship electronics. 4.6mm, .45 and 5.7mm boxes.
wg-pattern-rapid-hypo = Rapid hypospray
wg-pattern-rapid-hypo-desc = A bluespace hypospray that injects almost instantly, so a medic can dose a patient mid-fight.
wg-pattern-borg-arms-module = Borg arms module
wg-pattern-borg-arms-module-desc = A security cyborg module that gives an energy sword and a heavy pistol.
wg-pattern-heavy-pulse-cannon = Heavy pulse cannon
wg-pattern-heavy-pulse-cannon-desc = A shoulder-mounted pulse cannon that can blow holes in organics, or in a ship's hull on a heavy shot.
wg-pattern-xray-cells = X-ray laser cells
wg-pattern-xray-cells-desc = Cells that make the cell lasers fire x-rays. One each for revolver, SMG and sniper.
wg-pattern-pulse-cells = Pulse laser cells
wg-pattern-pulse-cells-desc = Cells that make the cell lasers fire pulse beams. One each for revolver, SMG and sniper.

wg-codex-tab = Codex
wg-relics-tab = Relics
wg-codex-status = {$server}: {$status}
wg-codex-points = Research points: [color=#fab325]{$points}[/color]. They pay for hunts before your bank.
wg-codex-arrays = { $arrays ->
    [0] No decoding arrays linked.
   *[other] Decoding arrays: {$arrays}. Decoding takes {$percent}% as long.
}
wg-codex-legend-known = Known
wg-codex-legend-huntable = Can be hunted
wg-codex-legend-hunting = Being hunted
wg-codex-legend-locked = Needs prerequisites
wg-codex-discipline = {$known}/{$total}
wg-codex-tier-line = {$discipline} · {$tier}
wg-codex-tier-inherited = Inherited Codex: every server starts with it.
wg-codex-tier-inherited-short = Inherited Codex
wg-codex-recovered-by = Recovered by a tier {$tier} pattern hunt.
wg-codex-inherited-desc = Common knowledge every lathe in the sector still holds.
wg-codex-state-inherited = Every research server starts with this.
wg-codex-state-hunting = Its shards are out there now. Bring them home and decode them here.
wg-codex-state-locked = Recover {$prerequisites} first.
wg-codex-state-huntable = Add it to a hunt request to locate its shards.
wg-codex-state-foreign = This server can't use it.
wg-codex-generic-unlock = • {$text}
wg-codex-pattern-hunting = {$name} (being hunted)
