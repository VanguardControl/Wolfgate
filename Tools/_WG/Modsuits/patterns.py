"""Generate modPattern prototypes and their names from every MODsuit control unit.

Reads every entity with a ModSuit component (inheritance resolved), groups variants that share a part set
(the ...PowerCell and ...Filled suits), joins each group to its plating tag through the Modsuit construction
graph, and writes:

  Resources/Prototypes/_WG/Modsuits/patterns.yml
  Resources/Locale/en-US/_WG/Modsuits/patterns.ftl

Usage: python Tools/_WG/Modsuits/patterns.py [--check]
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import yaml

REPO = Path(__file__).resolve().parents[3]
PROTOTYPES = REPO / "Resources" / "Prototypes"
GRAPH = PROTOTYPES / "_WG" / "Modsuits" / "construction.yml"
OUT_YAML = PROTOTYPES / "_WG" / "Modsuits" / "patterns.yml"
OUT_FTL = REPO / "Resources" / "Locale" / "en-US" / "_WG" / "Modsuits" / "patterns.ftl"

# Pattern id suffix and player-facing plate name per base control unit. A control unit missing here fails the run.
PATTERNS: dict[str, tuple[str, str]] = {
    "ClothingModsuitStandard": ("Standard", "Standard"),
    "ClothingModsuitEngineer": ("Engineer", "Sapper"),
    "ClothingModsuitAtmostech": ("Atmostech", "Aeolus"),
    "ClothingModsuitParamedic": ("Paramedic", "Sanitäter"),
    "ClothingModsuitMining": ("Mining", "Mining"),
    "ClothingModsuitLoader": ("Loader", "Loader"),
    "ADTClothingModsuitBackClown": ("Clown", "Clown"),
    "ClothingModsuitSecurity": ("Security", "Soldat"),
    "ClothingModsuitBrigMed": ("BrigMed", "Trauma"),
    "ClothingModsuitHeadOfSecurity": ("HeadOfSecurity", "Bulwark"),
    "ClothingModsuitResearchDirector": ("ResearchDirector", "Minerva"),
    "ClothingModsuitCaptain": ("Captain", "Magnate"),
    "ClothingModsuitTrainer": ("Trainer", "Career Trainer"),
    "ClothingModsuitPraetorian": ("Praetorian", "Praetorian"),
    "ClothingModsuitUSSPVaryag": ("Varyag", "Varyag"),
    "ClothingModsuitUSSPZastavnik": ("Zastavnik", "Zastavnik"),
    "ClothingModsuitRogue": ("Rogue", "Rogue"),
    "ClothingModsuitVolta": ("Volta", "Volta"),
    "ClothingModsuitMercenary": ("Mercenary", "Mercenary"),
    "WGAsfClothingModsuitField": ("AsfField", "ASF Field"),
    "WGAsfClothingModsuitAegis": ("AsfAegis", "ASF Aegis"),
    "ClothingModsuitSyndicate": ("Syndicate", "Syndicate"),
    "ClothingModsuitSyndicateElite": ("Elite", "Elite"),
    "ClothingModsuitContractor": ("Contractor", "Contractor"),
    "ClothingModsuitCorporate": ("Corporate", "Corporate"),
    "ClothingModsuitResponsoryLeader": ("ResponsoryLeader", "Leader Responsory"),
    "ClothingModsuitResponsorySecurity": ("ResponsorySecurity", "Security Responsory"),
    "ClothingModsuitResponsoryMedical": ("ResponsoryMedical", "Medical Responsory"),
    "ClothingModsuitResponsoryEngineering": ("ResponsoryEngineering", "Engineering Responsory"),
    "ClothingModsuitResponsoryJanitorial": ("ResponsoryJanitorial", "Janitorial Responsory"),
    "ClothingModsuitInquisitory": ("Inquisitory", "Inquisitory"),
    "ClothingModsuitApocryphal": ("Apocryphal", "Apocryphal"),
}

# Plates a Protean keeps between rounds. Not security, command, faction, antagonist or ERT.
CIVILIAN = {"Standard", "Engineer", "Atmostech", "Paramedic", "Mining", "Loader", "Clown"}


class _Loader(yaml.SafeLoader):
    pass


def _any_tag(loader, _suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


_Loader.add_multi_constructor("!", _any_tag)


def load_docs(path: Path) -> list:
    return yaml.load(path.read_text(encoding="utf-8"), Loader=_Loader) or []


def load_entities() -> dict[str, dict]:
    ents: dict[str, dict] = {}
    for path in sorted(PROTOTYPES.rglob("*.yml")):
        if path == OUT_YAML:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if "Modsuit" not in text and "ModSuit" not in text:
            continue
        for doc in load_docs(path):
            if isinstance(doc, dict) and doc.get("type") == "entity" and "id" in doc:
                ents[doc["id"]] = doc
    return ents


def parents(ent: dict) -> list[str]:
    par = ent.get("parent")
    if par is None:
        return []
    return par if isinstance(par, list) else [par]


def component(ents: dict[str, dict], eid: str, name: str) -> dict | None:
    """The component's fields after inheritance, or None if neither the entity nor a parent has it."""
    ent = ents.get(eid)
    if ent is None:
        return None
    merged: dict | None = None
    for par in parents(ent):
        inherited = component(ents, par, name)
        if inherited is not None:
            merged = {**(merged or {}), **inherited}
    for comp in ent.get("components") or []:
        if comp.get("type") == name:
            merged = {**(merged or {}), **{k: v for k, v in comp.items() if k != "type"}}
    return merged


def ancestors(ents: dict[str, dict], eid: str) -> set[str]:
    out: set[str] = set()
    stack = list(parents(ents.get(eid, {})))
    while stack:
        cur = stack.pop()
        if cur in out:
            continue
        out.add(cur)
        stack.extend(parents(ents.get(cur, {})))
    return out


def plating_tags() -> dict[str, str]:
    """Control unit id -> plating tag, from the Modsuit construction graph."""
    graph = next(d for d in load_docs(GRAPH) if d.get("type") == "constructionGraph" and d.get("id") == "Modsuit")
    node_entity = {n["node"]: n["entity"] for n in graph["graph"] if "entity" in n}
    tags: dict[str, str] = {}
    for node in graph["graph"]:
        for edge in node.get("edges") or []:
            target = node_entity.get(edge.get("to"))
            if target is None:
                continue
            for step in edge.get("steps") or []:
                if "tag" in step and str(step["tag"]).startswith("ModsuitPlating"):
                    tags[target] = step["tag"]
    return tags


def build() -> tuple[str, str, list[str]]:
    ents = load_entities()
    tags = plating_tags()
    warnings: list[str] = []

    groups: dict[tuple, list[str]] = {}
    suits: dict[str, dict] = {}
    for eid, ent in ents.items():
        if ent.get("abstract"):
            continue
        suit = component(ents, eid, "ModSuit")
        if suit is None:
            continue
        suits[eid] = suit
        key = tuple(sorted((suit.get("parts") or {}).items()))
        groups.setdefault(key, []).append(eid)

    patterns = []
    for members in groups.values():
        # The base is the member every other member inherits from; otherwise the shortest id.
        bases = [m for m in members if all(m == o or m in ancestors(ents, o) for o in members)]
        base = bases[0] if bases else min(members, key=len)
        if base not in PATTERNS:
            raise SystemExit(f"No pattern name for control unit {base} (variants: {sorted(members)}); add it to PATTERNS.")
        suffix, display = PATTERNS[base]
        suit = suits[base]
        inbuilt = list(suit.get("inbuiltModules") or [])
        for other in members:
            if other != base and list(suits[other].get("inbuiltModules") or []) != inbuilt:
                warnings.append(f"{other} has different inbuilt modules from {base}; the pattern uses {base}'s.")
        plating = next((tags[m] for m in [base, *sorted(members)] if m in tags), None)
        patterns.append({
            "id": f"WGPattern{suffix}",
            "suffix": suffix,
            "display": display,
            "theme": suit["theme"],
            "parts": suit.get("parts") or {},
            "inbuilt": inbuilt,
            "plating": plating,
            "source": base,
            "civilian": suffix in CIVILIAN,
        })

    missing = sorted(set(PATTERNS) - {p["source"] for p in patterns})
    if missing:
        raise SystemExit(f"PATTERNS names control units that no longer exist: {missing}")

    order = list(PATTERNS)
    patterns.sort(key=lambda p: order.index(p["source"]))

    y = ["# Generated by Tools/_WG/Modsuits/patterns.py from the control units. Do not edit by hand.", ""]
    f = [
        "# Generated by Tools/_WG/Modsuits/patterns.py. Do not edit by hand; change PATTERNS in the generator.",
        "",
        "## Draft, not yet seen by Moon",
        "",
    ]
    for p in patterns:
        loc = "wg-mod-pattern-" + "".join("-" + c.lower() if c.isupper() else c for c in p["suffix"]).lstrip("-")
        y += ["- type: modPattern", f"  id: {p['id']}", f"  name: {loc}", f"  source: {p['source']}", f"  theme: {p['theme']}"]
        y.append("  parts:")
        y += [f"    {slot}: {proto}" for slot, proto in p["parts"].items()]
        if p["inbuilt"]:
            y.append("  inbuiltModules:")
            y += [f"  - {m}" for m in p["inbuilt"]]
        if p["plating"]:
            y.append(f"  plating: {p['plating']}")
        if p["civilian"]:
            y.append("  civilian: true")
        y.append("")
        f.append(f"{loc} = {p['display']}")
    f.append("")
    return "\n".join(y), "\n".join(f), warnings


def write(path: Path, text: str, check: bool) -> bool:
    """Writes text with the file's existing line endings (CRLF on a new file on Windows). True if it changed."""
    newline = "\n"
    if path.exists():
        raw = path.read_bytes()
        newline = "\r\n" if b"\r\n" in raw else "\n"
        current = raw.decode("utf-8").replace("\r\n", "\n")
    else:
        newline = "\r\n" if sys.platform == "win32" else "\n"
        current = None
    if current == text:
        return False
    if not check:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(text.replace("\n", newline).encode("utf-8"))
    return True


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="fail if the outputs are stale instead of writing them")
    args = ap.parse_args()

    yml, ftl, warnings = build()
    for w in warnings:
        print(f"warning: {w}")
    changed = [p for p, t in ((OUT_YAML, yml), (OUT_FTL, ftl)) if write(p, t, args.check)]
    for p in changed:
        print(f"{'stale' if args.check else 'wrote'}: {p.relative_to(REPO)}")
    return 1 if args.check and changed else 0


if __name__ == "__main__":
    sys.exit(main())
