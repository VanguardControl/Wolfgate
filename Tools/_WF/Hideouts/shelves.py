"""Appends new decoration shelves to the blueprint catalogue from the game's own prototypes."""
import os, re

root = 'Resources/Prototypes'
ents = {}
for d, _, fs in os.walk(root):
    for f in fs:
        if not f.endswith('.yml'):
            continue
        try:
            text = open(os.path.join(d, f), encoding='utf-8-sig').read()
        except Exception:
            continue
        for block in re.split(r'\n(?=- type: )', text):
            if not block.lstrip().startswith('- type: entity'):
                continue
            m = re.search(r'^\s+id: (\S+)', block, re.M)
            if not m:
                continue
            ents[m.group(1)] = (bool(re.search(r'^\s+abstract: true', block, re.M)),
                                bool(re.search(r'HideSpawnMenu|noSpawn: true', block)))

tiles = set()
for d, _, fs in os.walk(root):
    if 'Tiles' not in d.replace('\\', '/').split('/'):
        continue
    for f in fs:
        if f.endswith('.yml'):
            tiles |= set(re.findall(r'^  id: (\w+)', open(os.path.join(d, f), encoding='utf-8-sig').read(), re.M))

path = 'Resources/Prototypes/_WF/Hideouts/blueprint_catalogue.yml'
raw = open(path, encoding='utf-8', newline='').read()
crlf = '\r\n' in raw
text = raw.replace('\r\n', '\n')
MARKER = '# More shelves, listed by Tools/_WF/Hideouts/shelves.py from the prototypes that match its patterns.'
# Only what is listed by hand counts as taken: the generated half is rewritten whole.
if MARKER in text:
    text = text[:text.index(MARKER)].rstrip() + chr(10)
used = set(re.findall(r'^\s+- (\w+)\s*$', text, re.M))
for f in ('blueprint.yml', 'safes.yml'):
    t = open('Resources/Prototypes/_WF/Hideouts/' + f, encoding='utf-8-sig').read()
    used |= set(re.findall(r'^\s+entity: (\w+)', t, re.M)) | set(re.findall(r'^\s+- (\w+)\s*$', t, re.M))

SKIP = re.compile(r'Random|Spawner|Debug|Filled|Broken|Unfinished|Cursed|Xeno|Web|Meat|Reaper|Abductor|Ritual|Frame|Flatpack|Circuit|Folded|Steal|McCargo|Dirty|Golden|Nuke|Syndi|Revolution')

def pick(pattern):
    rx = re.compile(pattern)
    out = sorted(i for i, (ab, hide) in ents.items() if rx.search(i) and not ab and not hide and i not in used and not SKIP.search(i))
    used.update(out)
    return out

# id, group, order, price, colour, under, pattern
SHELVES = [
    ('WFBlueprintShelfSeatingMore', 'wf-blueprint-group-seating', 100, 250, '#A07850', False,
     r'^Chair(BeachLounge|BigLog|Black|Brass|Barber|WoodFancy|PilotSeat|MushWood)|^N14Chair'),
    ('WFBlueprintShelfTables', 'wf-blueprint-group-tables', 104, 300, '#8C7A5C', False,
     r'^Table(Black|Brass|CounterRetro|Fancy|PlasmaGlass|ReinforcedBlack|ReinforcedGlass|UraniumGlass)|^Shelf(Bar|Glass|Kitchen|Metal|Wood|RGlass|RMetal$|RWood$)|^RackBlack$'),
    ('WFBlueprintShelfBedroom', 'wf-blueprint-group-bedroom', 106, 300, '#7C8CA8', False,
     r'^N14Bed(Wood|WoodBunk)?$|^DogBed$|^Mattress$|^(Upright)?PianoInstrument$|^ChurchOrganInstrument$'),
    ('WFBlueprintShelfBathroom', 'wf-blueprint-group-bathroom', 108, 400, '#9CC4D8', False,
     r'^Sink(Wide|Stemless)?$|^ToiletEmpty$'),
    ('WFBlueprintShelfPlantsMore', 'wf-blueprint-group-plants', 110, 200, '#5E9A4E', False,
     r'^PottedPlantAlt\d|^FloraPalmTree|^FloraTreeChristmas|^FloraAshtree'),
    ('WFBlueprintShelfRocks', 'wf-blueprint-group-rocks', 112, 400, '#7FB4C8', False,
     r'^Crystal(Blue|Cyan|Green|Grey|Orange|Pink)$|^FloraRockSolid$|^FloraObelisk|^Flora(Grey)?Stalagmite'),
    ('WFBlueprintShelfCurtains', 'wf-blueprint-group-banners', 140, 200, '#B05A5A', False,
     r'^Curtains[A-Z]\w+$|^Banner(NGC|Goblin01|SHM|DrakeIndustries)$|^FlagNfsd$'),
    ('WFBlueprintShelfWallFittings', 'wf-blueprint-group-wall-art', 150, 150, '#C9B27A', True,
     r'^Mirror$|^WallClock$'),
    ('WFBlueprintShelfPosters', 'wf-blueprint-group-posters', 152, 150, '#C98A5A', True,
     r'^Poster(Legit|Contraband)'),
    ('WFBlueprintShelfSigns', 'wf-blueprint-group-signs', 154, 150, '#D8C24A', True,
     r'^Sign(?!al)[A-Z0-9]'),
    ('WFBlueprintShelfBarSigns', 'wf-blueprint-group-bar-signs', 156, 600, '#E06AB0', True,
     r'^BarSign[A-Z]|^BarberSign'),
    ('WFBlueprintShelfClutter', 'wf-blueprint-group-clutter', 160, 100, '#8A7A66', True,
     r'^N14Decor'),
    ('WFBlueprintShelfKeepsakes', 'wf-blueprint-group-keepsakes', 162, 500, '#E0A0C0', True,
     r'^Plushie|^ToyFigurine|^Candle\w*Infinite$'),
]

# Shelves listed by hand: id, group, order, price, colour, under, scope, entities, floor tiles. Ids the game lacks are left out.
LISTED = [
    ('WFBlueprintShelfHullFloors', 'wf-blueprint-group-hull-floors', 20, 150, '#5C6470', False, None, '',
     'Plating FloorHull FloorHullReinforced FloorReinforced FloorReinforcedHardened FloorShuttleBlack FloorShuttleBlue FloorShuttleGrey '
     'FloorShuttleOrange FloorShuttlePurple FloorShuttleRed FloorShuttleWhite FloorTechMaint FloorTechMaint2 FloorTechMaint3 '
     'FloorTechMaintDark FloorTechGreeb FloorMono FloorDarkMono FloorSteelMono FloorWhiteMono FloorMetalDiamond FloorMetalGrey '
     'FloorMetalGreyDark FloorMetalGreySolid FloorMetalGreyDarkSolid FloorMetalBlue FloorMetalGreen FloorMetalRed FloorMetalTeal '
     'FloorMetalYellow FloorMetalPurple FloorMetalWhite FloorMining FloorMiningDark FloorMiningLight FloorElevatorShaft'),
    ('WFBlueprintShelfMoreFloors', 'wf-blueprint-group-more-floors', 22, 120, '#9A8F7A', False, None, '',
     'FloorSteelDiagonal FloorSteelHerringbone FloorSteelMini FloorSteelOffset FloorSteelPavement FloorSteelCheckerDark '
     'FloorSteelCheckerLight FloorDarkDiagonal FloorDarkHerringbone FloorDarkMini FloorDarkOffset FloorDarkPavement FloorDarkPlastic '
     'FloorWhiteDiagonal FloorWhiteHerringbone FloorWhiteMini FloorWhiteOffset FloorWhitePavement FloorWhitePlastic FloorKitchen '
     'FloorBar FloorLino FloorFreezer FloorHydro FloorShowroom FloorGlass FloorRGlass FloorWoodLarge FloorWoodTile FloorWoodMaple '
     'FloorWoodOak FloorWoodHouse FloorCarpetOffice FloorCarpetDeco FloorCarpetArt FloorBoxing FloorGold FloorSilver'),
    ('WFBlueprintShelfShipHull', 'wf-blueprint-group-ship-hull', 30, 0, '#7A8FA8', False, 'Ship',
     'WallShuttle WallShuttleDiagonal ShuttleWindow WallReinforced ReinforcedWindow WallPlastitanium WallPlastitaniumDiagonal '
     'PlastitaniumWindow AirlockShuttle AirlockGlassShuttle AirlockExternal AirlockExternalGlass Firelock FirelockGlass FirelockEdge '
     'BlastDoor ShuttersNormal ShuttersWindow Grille Catwalk', ''),
    ('WFBlueprintShelfShipMachines', 'wf-blueprint-group-ship-machines', 32, 0, '#C87A3A', False, 'Ship',
     'Thruster SmallThruster ThrusterLarge Gyroscope PortableGeneratorJrPacmanShuttle PortableGeneratorPacmanShuttle '
     'PortableGeneratorSuperPacmanShuttle GeneratorWallmountAPU GeneratorWallmountBasic SMESBasic SubstationBasic '
     'SubstationWallBasic APCBasic CableApcExtension CableMV CableHV ComputerShuttle ComputerRadar ComputerIFF '
     'ComputerPowerMonitoring ComputerCrewMonitoring ComputerSurveillanceCameraMonitor AirAlarm FireAlarm AirCanister OxygenCanister '
     'GasVentPump GasVentScrubber GasPassiveVent GasPipeStraight GasPipeBend GasPipeTJunction GasPipeFourway GravityGeneratorMini', ''),
    ('WFBlueprintShelfShipFittings', 'wf-blueprint-group-ship-fittings', 34, 400, '#A8B4C0', True, 'Ship',
     'GreebleAntenna1 GreebleAntenna2 GreebleAntenna3 GreebleRadarpanel1 GreebleRadarpanel2', ''),
]

listed = []
for sid, group, order, price, colour, under, scope, names, floor in LISTED:
    have = [n for n in names.split() if n in ents and not ents[n][0]]
    missing = [n for n in names.split() if n not in have]
    floors = [n for n in floor.split() if n in tiles]
    missing += [n for n in floor.split() if n not in floors]
    print(sid, len(have) + len(floors), 'missing:', ' '.join(missing))
    lines = ['- type: wfBlueprintCatalogue', f'  id: {sid}', f'  group: {group}', f'  order: {order}', f'  price: {price}',
             f'  color: "{colour}"']
    if under:
        lines.append('  under: true')
    if scope:
        lines.append(f'  scope: {scope}')
    if have:
        lines.append('  entities:')
        lines += [f'  - {name}' for name in have]
    if floors:
        lines.append('  tiles:')
        lines += [f'  - {name}' for name in floors]
    listed.append('\n'.join(lines))

blocks = []
for sid, group, order, price, colour, under, pattern in SHELVES:
    found = pick(pattern)
    print(sid, len(found))
    if not found:
        continue
    lines = ['- type: wfBlueprintCatalogue', f'  id: {sid}', f'  group: {group}', f'  order: {order}', f'  price: {price}',
             f'  color: "{colour}"']
    if under:
        lines.append('  under: true')
    lines.append('  entities:')
    lines += [f'  - {name}' for name in found]
    blocks.append('\n'.join(lines))

marker = '# More shelves, listed by Tools/_WF/Hideouts/shelves.py from the prototypes that match its patterns.'
if marker in text:
    text = text[:text.index(marker)].rstrip('\n') + '\n'
text = text.rstrip('\n') + '\n\n' + marker + '\n\n' + '\n\n'.join(listed + blocks) + '\n'
open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', '\r\n') if crlf else text)
