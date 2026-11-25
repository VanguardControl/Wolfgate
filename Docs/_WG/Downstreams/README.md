# Downstreams

A downstream is a server built on Wolfgate the way Wolfgate is built on Monolith: a GitHub fork that merges
Wolfgate's `main` in and keeps its own changes in a layer of its own. The first one is **KnotGate**
(`Aphelion-Moon/KnotGate`, layer `_KG`, marker `KNOTGATE`), a PvE and ERP server that shares Wolfgate's player
database. This page covers how a downstream is set up and what Wolfgate keeps doing so that merging it stays cheap.

The module tooling is shared: `Tools/_WG/Ci/modules.py --config <layer's modules.yml>` runs every check in this
repository's AGENTS.md on the downstream's own layer, and CI's **Wolfgate Modules** workflow runs it when the
repository variable `MODULES_LAYER_CONFIG` is set.

## In Wolfgate: keeping downstreams cheap

- **Make it a switch.** A feature a downstream might not want (encounters, factions, a hazard, a game mode) gets
  a cvar, or a prototype field, with Wolfgate's behaviour as the default. A downstream then turns it off in its
  `server_config.toml` instead of editing Wolfgate's code. Most hostile content already works this way
  (`wg.encounters.*`, `wg.sector.enabled`, `wg.defcon.enabled`, `wg.ship_squatters`, ...).
- **Migrations are additive.** A downstream that shares the database runs the same schema, and its build can be a
  day or two behind Wolfgate's. A new column is nullable or has a default; a rename or a drop happens a release
  after the code stops using the old name.
- **Saved IDs live here.** Anything written into a player's profile (species, markings, traits, loadouts, jobs,
  companies, consent keys) has to exist on every server that reads that profile. Content of that kind that a
  downstream wants goes into Wolfgate, behind a switch if Wolfgate shouldn't offer it.
- **Don't hard-code preset IDs in code.** Gate on rules or components in the preset instead, so a downstream's own
  game preset behaves like the Wolfgate preset it copies.

## In a downstream: setting one up

1. **Fork** Wolfgate on GitHub. The fork's `main` is the downstream's `main`.
2. **Pick a prefix and a marker**: two or three letters (`KG`) and one word (`KNOTGATE`). The layer's folders are
   the table in AGENTS.md with `_KG` in place of `_WG`; IDs take the prefix (`KGHavenPreset`).
3. **Add `Tools/_<PREFIX>/Ci/modules.yml`**:

   ```yaml
   layer:
     prefix: KG
     marker: KNOTGATE
     forbidden:   # only when the database is shared with Wolfgate
       - path: Content.Server.Database/Migrations/**
         reason: the database is shared with Wolfgate; schema changes go to Wolfgate
   # The Wolfgate commit the downstream is based on. Move it forward after each Wolfgate merge.
   fork_point: <commit>
   external: {}
   unmarked: []
   ```

   Then run `python Tools/_WG/Ci/modules.py --config Tools/_KG/Ci/modules.yml --write` and commit
   `Docs/_KG/NONMODULAR.md` and the module READMEs it creates.
4. **Repository variables** (Settings, Secrets and variables, Actions, Variables):
   - `MODULES_LAYER_CONFIG` = `Tools/_KG/Ci/modules.yml`. Wolfgate's own `--pr-check` step steps aside and the
     downstream's runs instead.
   - `CDN_FORK_ID` = the downstream's Robust.Cdn fork (`knotgate`). `publish.yml` publishes there unless the
     run names another fork.
5. **Repository secret** `PUBLISH_TOKEN`: the CDN fork's `UpdateToken`.

## In a downstream: rules

- **`_WG` is upstream.** Inside the downstream, every Wolfgate file, `_WG` folders included, is upstream code. An
  edit to one carries the downstream's marker (`// KNOTGATE(Haven): reason`) under the same rules as a Wolfgate
  edit to Monolith code. Never add `WOLFGATE` markers or files under `_WG`; those changes go to Wolfgate as a pull
  request, where every downstream gets them.
- **Prefer, in order:** a setting in the server's `server_config.toml`; a new prototype or system in the layer; a
  marked edit. A downstream that only needs configuration should not carry code for it.
- **Merging Wolfgate in:** on a branch named `upstream/<date>`, `git fetch wolfgate && git merge wolfgate/main`,
  open the pull request with a title starting `[UPSTREAM]` (the layer check skips it), regenerate the layer docs,
  and move `fork_point` to the merged Wolfgate commit.

## When the database is shared

KnotGate and Wolfgate use one PostgreSQL database: the same characters, bank balances, bans, playtime and
whitelist. Upstream SS14 supports several servers on one database; these rules keep two codebases on it safe.

- **No migrations in the downstream.** `forbidden:` in its `modules.yml` makes the pull request check fail on any
  file under `Content.Server.Database/Migrations`. Schema changes go to Wolfgate.
- **Wolfgate deploys first.** A Wolfgate build that adds a migration goes live before the downstream merges it.
  Until the downstream catches up it runs on a newer schema than its code knows, which additive migrations allow.
- **Don't disable saved content.** Turning off a company, species or marking in the downstream would strip it from
  a character when the player saves there, and the character would lose it on Wolfgate too. Leave it offered, or
  hide it in Wolfgate behind a switch both servers read.
- **Give each server its own name** in the database: `[adminlogs] server_name` in `server_config.toml`, so rounds
  and admin logs say where they happened.
- **Bring it up on the test database first**, beside the Wolfgate test server, and move it to the live database
  once it has run a few rounds.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

None.

## Non-modular edits

- [`.github/workflows/publish.yml`](../../../.github/workflows/publish.yml): a downstream names its CDN fork in a repository variable
- [`.github/workflows/wg-modules.yml`](../../../.github/workflows/wg-modules.yml)
  - a downstream (MODULES_LAYER_CONFIG set) checks its own layer below instead
  - a downstream's own layer, from the repository variable MODULES_LAYER_CONFIG

<!-- WOLFGATE-GENERATED END -->
