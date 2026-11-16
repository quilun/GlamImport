# Changelog

## 0.2.1.0 (2026-11-16)
- Plugin icon.
- "Visit plugin URL" button in the plugin installer, linking to the GitHub page.
- Description and tags mention "glamour", so searching for it in the plugin installer finds GlamImport.

## 0.2.0.0 (2026-11-16)
- **Renamed to GlamImport** (was ECImport), so more glamour sites can be added later. Command: `/glamimport`.
- **Apply to Target**: put a glam on the character you're targeting (another player, an NPC, or a GPose actor). Gender/race locks and the weapon are checked for the target.
- **Recent imports**: the clock button next to Settings lists your last 10 applied or saved glams to load again.
- **Glamourer version check**: a clear message if Glamourer is missing, too old, or has an API this version doesn't support yet.
- **Weapons**: hovering "Other job" shows which jobs can use the weapon.
- **Dye boxes** have a faint outline so very dark dyes stay visible.
- Tags written on one line ("Tags: Cute Final Fantasy") are split using Eorzea Collection's tag list.
- Parser tests (GlamImport.Tests) with sample pastes.

## 0.1.0.0 (2026-11-16)
- First version: paste an Eorzea Collection glam, check the gear and dyes, apply it to yourself or save it as a Glamourer preset.
- Revert to Game, facewear, clearing slots the glam leaves empty.
- Settings: gender/race-locked gear, preset names with tags and folders, Glamourer tags and descriptions from Eorzea Collection.
