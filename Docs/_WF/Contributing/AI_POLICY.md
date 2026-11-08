# AI-generated content policy

AI use is allowed when contributing to Wolfgate. However, you are responsible for everything you submit, regardless of how it was made.

This page covers what is and isn't allowed regarding AI-generated content, what you have to disclose and what reviewers do about it.

## Code

AI-assisted code is allowed. Things like autocomplete, chat assistance and coding agents are fine to use under the following conditions:
- You must understand what has been made, and justify changes, removals and additions.
- Build it and test it yourself, as described in [Wolfgate Guidelines](../../../Wolfgate%20Guidelines.md#7-test-before-you-pr). TEST YOUR STUFF!
- Keep the PR to the size you can review yourself.

## Sprites, textures and audio

AI-generated sprites, textures, sounds, music and voice lines are only accepted if they have been substantially reworked by hand.
- Raw model output, or model output with only light touch-ups, is not accepted.
- You can use AI for references, concepts or palette ideas.

## Player-facing text

Lore, guidebook pages, Fluent strings, item descriptions and in-game dialogue can be AI-assisted, but a person has to write or rewrite them.
- Edit generated text until it reads like the rest of the game: short, plain and in-world. Don't paste raw AI output.
- Check names, numbers and mechanics against the game.

Commit messages, PR descriptions and design docs follow the same rule. Short and to the point!

## Ports and licensing

You are responsible for the licence and attribution of everything you submit.
- Don't use AI to copy or paraphrase code or assets you don't have the rights to. Porting from another fork still needs the original licence and credit.
- Code is under the AGPL-3.0 (see the [README](../../../README.md#license)). Don't submit generated code that you know reproduces code under an incompatible licence.

## Disclosure

The [PR template](../../../.github/PULL_REQUEST_TEMPLATE.md) has an **AI use** line. On PRs with a diff of more than 1,000 lines, you must disclose whether you used AI, which model and what for, in a few words.
- For example: "Claude Opus 5.5 used for systems and tests"
- Disclosure doesn't count against a PR. Hiding it does!
- PRs with a diff less than 1,000 lines are exempt from this.

