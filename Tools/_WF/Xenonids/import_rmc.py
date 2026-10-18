#!/usr/bin/env python3
"""Copies RMC-14 source files into a Wolfgate module, rewriting namespaces and the bits Robust needs changed.

    python Tools/_WF/Xenonids/import_rmc.py <module> <rmc-relative path>... [--rmc E:/GitHub/RMC-14] [--dry-run] [--force]

A path is a file or folder relative to the RMC-14 checkout, for example
`Content.Shared/_RMC14/Xenonids/Plasma` or `Resources/Prototypes/_RMC14/Entities/Mobs/Xeno/drone.yml`. Each lands at the
same relative place with `_RMC14` replaced by `_WF/<module>` (and `_RMC14/Xenonids` collapsed to `_WF/Xenonids` for the
Xenonids module, so `Content.Shared/_RMC14/Xenonids/Plasma/X.cs` becomes `Content.Shared/_WF/Xenonids/Plasma/X.cs`).

For every .cs and .xaml file the script

  * strips the byte order mark and keeps the source line endings,
  * rewrites `Content.<Side>._RMC14.<Sub>` namespaces and usings by the prefix map in namespaces.yml (unmapped RMC
    namespaces are left as they are and listed in the report so the port can decide what to do with them),
  * drops `readonly` from [Dependency] fields and adds `partial` to the type that holds one,
  * removes duplicate usings and a using that names the file's own namespace.

Resource paths inside .yml, .ftl, .xaml and .cs files (`/Textures/_RMC14/`, `/Audio/_RMC14/`) are rewritten to the
module's folders. Everything else (images, audio, json) is copied byte for byte. An existing destination file is
skipped unless --force is given. The report lists every file written and the RMC namespaces still referenced.
"""
from __future__ import annotations

import argparse
import re
import shutil
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("import_rmc.py needs PyYAML: pip install pyyaml")

REPO = Path(__file__).resolve().parents[3]
NAMESPACES = Path(__file__).resolve().parent / "namespaces.yml"
CODE = {".cs", ".xaml"}
TEXT = {".cs", ".xaml", ".yml", ".yaml", ".ftl", ".md", ".txt", ".xml"}

DEPENDENCY = re.compile(r"\[\s*Dependency\b[^\]]*\]")
TYPE_DECL = re.compile(r"^(\s*)((?:public|internal|private|protected|static|abstract|sealed|\s)*?)(class|struct|record)\s", re.M)


def load_map(module: str) -> list[tuple[str, str]]:
    data = yaml.safe_load(NAMESPACES.read_text(encoding="utf-8")) or {}
    pairs = []
    for src, dst in (data.get("common") or {}).items():
        pairs.append((src, dst))
    for src, dst in ((data.get("modules") or {}).get(module) or {}).items():
        pairs.append((src, dst))
    # longest source first so a sub-namespace rule wins over its parent
    pairs.sort(key=lambda p: -len(p[0]))
    return pairs


def dest_path(rel: Path, module: str) -> Path:
    parts = list(rel.parts)
    out = []
    i = 0
    while i < len(parts):
        p = parts[i]
        if p == "_RMC14":
            out.append("_WF")
            out.append(module)
            if module == "Xenonids" and i + 1 < len(parts) and parts[i + 1] == "Xenonids":
                i += 1
        else:
            out.append(p)
        i += 1
    return Path(*out)


def rewrite_namespaces(text: str, pairs: list[tuple[str, str]]) -> str:
    def repl(m: re.Match) -> str:
        ns = m.group(2)
        for src, dst in pairs:
            if ns == src or ns.startswith(src + "."):
                return m.group(1) + dst + ns[len(src):] + m.group(3)
        return m.group(0)

    # `namespace X;`, `namespace X {`, `using X;`, `using static X.Y;`, `using A = X.Y;` and fully qualified names
    text = re.sub(r"(^\s*(?:namespace|using(?:\s+static)?|using\s+\w+\s*=)\s+)(Content\.(?:Shared|Server|Client)\._RMC14(?:\.[\w]+)*)(\s*[;{])",
                  repl, text, flags=re.M)
    text = re.sub(r"(\b)(Content\.(?:Shared|Server|Client)\._RMC14(?:\.[\w]+)*)(\b)", repl, text)
    # XAML clr-namespace references
    return text


def drop_readonly_dependencies(text: str) -> str:
    out = []
    changed_types = False
    for line in text.splitlines(keepends=True):
        if DEPENDENCY.search(line) and re.search(r"\breadonly\b", line):
            line = re.sub(r"\breadonly\s+", "", line, count=1)
            changed_types = True
        out.append(line)
    text = "".join(out)
    if changed_types or DEPENDENCY.search(text):
        def add_partial(m: re.Match) -> str:
            mods = m.group(2)
            if "partial" in mods:
                return m.group(0)
            return f"{m.group(1)}{mods}partial {m.group(3)} "
        text = TYPE_DECL.sub(add_partial, text)
    return text


def tidy_usings(text: str) -> str:
    ns = re.search(r"^namespace\s+([\w.]+)", text, re.M)
    own = ns.group(1) if ns else None
    seen = set()
    out = []
    for line in text.splitlines(keepends=True):
        m = re.match(r"^using\s+([\w.]+);\s*$", line)
        if m:
            if m.group(1) in seen or m.group(1) == own:
                continue
            seen.add(m.group(1))
        out.append(line)
    return "".join(out)


def rewrite_resources(text: str, module: str) -> str:
    text = text.replace("/Textures/_RMC14/", f"/Textures/_WF/{module}/")
    text = text.replace("/Audio/_RMC14/", f"/Audio/_WF/{module}/")
    return text


def transform(path: Path, text: str, module: str, pairs) -> str:
    if path.suffix in CODE:
        text = rewrite_namespaces(text, pairs)
        if path.suffix == ".cs":
            text = drop_readonly_dependencies(text)
            text = tidy_usings(text)
        else:
            text = text.replace("Content.Client._RMC14.", "Content.Client._WF." + module + ".")
    return rewrite_resources(text, module)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("module")
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--rmc", default=None, help="RMC-14 checkout (default: a sibling of the Wolfgate checkout)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--force", action="store_true", help="overwrite files that exist")
    args = ap.parse_args()

    rmc = Path(args.rmc) if args.rmc else next((b / "RMC-14" for b in [REPO.parent, *REPO.parents] if (b / "RMC-14").exists()), None)
    if rmc is None or not rmc.exists():
        sys.exit("RMC-14 checkout not found; pass --rmc")
    pairs = load_map(args.module)

    files: list[Path] = []
    for p in args.paths:
        src = rmc / p
        if src.is_dir():
            files += sorted(f for f in src.rglob("*") if f.is_file())
        elif src.is_file():
            files.append(src)
        else:
            print(f"missing: {src}", file=sys.stderr)
            return 2

    leftover: dict[str, set[str]] = {}
    written = skipped = 0
    for src in files:
        rel = src.relative_to(rmc)
        dst = REPO / dest_path(rel, args.module)
        if dst.exists() and not args.force:
            skipped += 1
            continue
        if src.suffix in TEXT:
            raw = src.read_bytes()
            text = raw.decode("utf-8-sig", errors="surrogateescape")
            text = transform(dst, text, args.module, pairs)
            for m in re.finditer(r"Content\.(?:Shared|Server|Client)\._RMC14(?:\.\w+)*", text):
                leftover.setdefault(m.group(0), set()).add(dst.relative_to(REPO).as_posix())
            if not args.dry_run:
                dst.parent.mkdir(parents=True, exist_ok=True)
                dst.write_bytes(text.encode("utf-8", errors="surrogateescape"))
        elif not args.dry_run:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
        written += 1
        print(f"{'would write' if args.dry_run else 'wrote'} {dst.relative_to(REPO).as_posix()}")
    print(f"\n{written} written, {skipped} skipped (exist)")
    if leftover:
        print("\nRMC namespaces still referenced (no rule in namespaces.yml):")
        for ns, fs in sorted(leftover.items()):
            print(f"  {ns}  ({len(fs)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
