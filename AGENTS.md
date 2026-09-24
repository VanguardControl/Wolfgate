# Wolfgate agent guidelines

Wolfgate is a downstream fork of Monolith (itself built on Frontier, Delta-V, CE and other layers). Every rule
here exists to keep upstream merges cheap and Wolfgate changes easy to find.

## Modularity

New files go in the `_WF` folder for their area:

| Area | Folder |
|---|---|
| C# | `Content.Client/_WF`, `Content.Server/_WF`, `Content.Shared/_WF` |
| Tests | `Content.IntegrationTests/Tests/_WF`, `Content.Tests/_WF` |
| Prototypes | `Resources/Prototypes/_WF` |
| Localization | `Resources/Locale/en-US/_WF` |
| Textures, audio, maps | `Resources/Textures/_WF`, `Resources/Audio/_WF`, `Resources/Maps/_WF` |
| Guidebook pages | `Resources/ServerInfo/_WF` |
| Scripts and generators | `Tools/_WF` |
| Design docs | `Docs/_WF` |

Namespaces follow the folder (`Content.Server._WF.Traders`). A partial class that extends an upstream system
lives in `_WF` but keeps the upstream namespace.

Content ported from another fork may keep that fork's folder and IDs (`_HL`, `_Common`, `_Floof`, ...).
Database migrations stay in `Content.Server.Database/Migrations` and are named `Wolfgate<Change>`. Symphony
(the hub integration, `*/Symphony` and `*.Symphony.cs`) is its own module and stays where it is.

Any edit to a file outside `_WF` is non-modular and must be marked:

- A single line: `// WOLFGATE` at the end of the line, or `// WOLFGATE: reason` on the line above.
- A block: `// WOLFGATE START: reason` before it and `// WOLFGATE END` after it.
- JavaScript uses the `//` forms. YAML, Fluent, Python and TOML use `#` (`# WOLFGATE START: reason` /
  `# WOLFGATE END`). XAML and XML use `<!-- WOLFGATE: reason -->` and `<!-- WOLFGATE START/END -->`.
- Added `using` lines count as edits and get `// WOLFGATE`.
- Fluent and `.gitignore` only treat `#` as a comment at the start of a line, so never put a marker at the end
  of a line there. The same goes for YAML block scalars (`|`, `>`).
- A comment can't go inside an XML tag, so an added attribute or `xmlns` on a tag is named in the nearest marker
  instead.
- Guidebook XML (`Resources/ServerInfo`): the parser only skips a comment that sits directly before content, so
  put the marker at the start of the line it marks, or just before `<Document>` for the first line inside it.
- Exempt: files that can't hold comments (JSON, images, audio, rich-text `.txt` such as `Resources/ServerInfo/Rules.txt`),
  map files (the mapper rewrites them), generated EF migration and snapshot files, and `Resources/Changelog`.

Keep upstream edits to the smallest hook that works. Prefer, in order: a new `_WF` system that subscribes to
existing events, a partial class in `_WF`, then a marked edit. Don't reformat, reorder or re-indent upstream
code, and don't delete upstream code outright; comment or branch around it inside a marked block so a merge
shows what changed.

## Before building

- Check whether the feature already exists in upstream SS14 or any fork layer (`_NF`, `_Mono`, `_DV`, `_CE`,
  `_Goobstation`, ...). Grep verbs, commands, components and loc strings. Report what exists and how it differs
  before building anything new.
- When porting from another fork, check that the systems and components it depends on exist here. HardLight
  in particular has renames and systems Wolfgate lacks.

## Code style

- No license header in `_WF` files. (`MARKERS.md` is the upstream Frontier convention and doesn't apply here.)
- Comments are short and precise: a one-line `/// <summary>` on types, public members and non-trivial methods;
  a brief `//` only where the intent isn't obvious. No narrative or restating comments.
- Dependencies: `[Dependency] private X _x = default!;`, without `readonly`.
- Every player-facing string goes through Fluent in `Resources/Locale/en-US/_WF`. No hard-coded text.
- Wolfgate prototype IDs take a `WF` prefix (`WFTractorBeamEmitter`), unless they deliberately override an
  upstream ID or were ported from another fork. Renaming an entity ID needs a `Resources/migration.yml` entry
  (inside its `WOLFGATE` block). Renaming an ID that is saved (profiles, consent rows, loadouts, cvars) needs an
  entry in `Content.Shared/_WF/Prototypes/WFLegacyPrototypeIds.cs`.
- `Content.Server/_WF/SafetyDepositBox` and `Content.Client/_WF/SafetyDepositBox` are the reference for tone and
  density.

## Engine traps

- Never edit `RobustToolbox`. It is a submodule; engine changes need a separate discussion.
- Client and Shared code is checked against `RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml` at client
  load, and the compiler won't catch a violation. Not allowed: `BinaryWriter`, `EndOfStreamException`,
  `ConditionalWeakTable`, `System.Diagnostics.Process`, a collection expression assigned to a `List<T>`, and
  `string += char`. Put these in Server, or use `new List<T> { ... }` and `StringBuilder`.
- Only one system may subscribe a given (component, event) pair. Before subscribing to an upstream component's
  event, grep for an existing subscription; a duplicate compiles and then crashes the server at startup.
- `DefaultWindow` subclasses must not name controls `CloseButton`, `ContentsContainer`, `TitleLabel` or
  `WindowHeader`.
- Files are LF in git but CRLF in a Windows working tree; keep each file's existing line endings. Don't write a
  doubled carriage return (`\r\r\n`); Fluent rejects the file and git treats it as binary.

## Verifying

- Build the affected projects. Say so plainly if you didn't.
- Prototype changes: start a headless server and grep its log for `[ERRO]`/`[FATL]`, then run
  `dotnet run --project Content.YAMLLinter -c Release`. The linter always crashes in DebugOpt on Windows.
- Logic: add an NUnit test under `Content.IntegrationTests/Tests/_WF` (or `Content.Tests/_WF` for pure logic
  with no server). Never use `PoolSettings { Destructive =
  true }`; each one permanently costs CI about 1.9 GB and the runner is near its limit.
- Client or Shared changes: start a real client and grep its log for `Sandbox violation`. Integration tests
  don't run the sandbox check.
- Report failures with their output. Don't call something working unless you ran it.

## Git and PRs

- Never commit to `main`. Work on a branch and open a PR against `VanguardControl/Wolfgate`. Pass
  `--repo VanguardControl/Wolfgate` to `gh`; plain `gh` resolves to upstream Monolith.
- Fill in `.github/PULL_REQUEST_TEMPLATE.md`, including a `:cl:` changelog for player-facing changes.
- Read CI results from the PR's checks, not from local re-runs. Several lints and map tests give false results
  on Windows.
