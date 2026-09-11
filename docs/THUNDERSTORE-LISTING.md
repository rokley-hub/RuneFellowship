# Rune Fellowship

![Rune Fellowship promotional banner](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/main/docs/images/rune-fellowship-cover.png)

**Thunderstore package 0.4.42 | Windows desktop 0.4.41 beta | game plugin 0.3.26**

Package 0.4.42 restores the app screenshots and adds the direwolf animation preview. It includes the same installer and game plugin as 0.4.41.

Build a fellowship of companions in Valheim. Speak or type supported orders, choose their personality and voice, and follow their work in the game. Local speech, optional local or ChatGPT dialogue, mod profiles and the companion desktop app are included in Rune's workflow.

## New in this release

- Custom saddled direwolf with native wolf movement/combat, riding, jumping and biting.
- Corrected body rendering and saddle tracking, fuller chest/tucked belly, visible reins and mounted stopping logic.
- Clearer mount HUD/map icons and a distinct direwolf companion portrait.
- Compact overlay without skill levels or redundant team/proximity information.
- F8 companion overview with health, stamina, food, equipment, carried items and current work.
- New fellowship artwork and an expanded player guide.

## Direwolf in motion

![Saddled direwolf running animation preview](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/main/docs/images/direwolf-running-preview.gif)

Looping Blender preview of the custom saddled direwolf's run cycle. Captured during development; in-game lighting and the latest body proportions differ. This shows the model animation, not a live gameplay recording.

## Inside the desktop app

Actual Rune interface captures with example companions. Play and Settings are from desktop 0.4.29; Commands is an earlier 0.4.25 capture. Some controls have since changed.

### Play and companion selection

![Rune Play screen](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/v0.4.29-beta/docs/images/play.png)

Choose who receives your voice and typed orders. Companion cards show the fellowship, while notifications highlight updates and setup issues.

### AI and voice settings

![Rune AI settings](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/v0.4.29-beta/docs/images/settings.png)

Choose Local, Hybrid or ChatGPT for conversation and commands. Speech recognition and companion voices stay local.

### Commands and abilities

![Rune commands screen](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/v0.4.29-beta/docs/images/commands.png)

Browse supported intentions, examples and their requirements. Available actions depend on the companion's body, equipment and surroundings.

## Install the desktop app

1. Choose **Download** and extract the package ZIP.
2. Extract the included **Rune Installer.zip**.
3. Open **Install Rune.exe** and choose an installation folder and optional components.
4. Open Steam, then Rune; configure a profile, companion and voice, and use **Start modded**.

The included installer is the complete Windows online installer. Selected runtimes and models download from their original publishers. **Install with App** installs the game integration through an external mod manager; it does not run the desktop installer. Rune needs your own licensed Valheim installation and does not bundle the game or other authors' mods.

Existing users on 0.4.27 or later: **Settings → Updates & support → Check for updates**. Close Valheim before updating. Earlier users can run the full installer into their existing Rune folder. Keep app and game plugin current together.

## Riding and companion information

Create a **Direwolf** companion in Rune, save and summon it. Interact with its fixed saddle to ride. Movement and Run use Valheim controls; releasing forward movement stops it unless auto-run is active. Backward movement or Block brakes. Jump uses mount stamina; primary Attack starts a wolf bite. Secondary attack or dodge dismounts. Hunger slows mount-stamina regeneration.

F7 toggles the compact fellowship overlay. F8 opens companion information and unlocks dragging the overlay header. Appearance and orders are managed through the desktop app. Body capabilities, food, stamina, equipment, tools and resources limit what companions can do.

[Detailed player guide and troubleshooting](https://thunderstore.io/c/valheim/p/RuneFellowship/RuneFellowship/wiki/5843-getting-started-and-player-guide/) | [GitHub source and releases](https://github.com/rokley-hub/RuneFellowship) | [Report an issue](https://github.com/rokley-hub/RuneFellowship/issues)

## Beta status

Release builds, deterministic companion-policy checks, updater tests and installer/package checks passed. The mount has been loaded and ridden in live screenshots; the latest stop/seat/rein corrections still need wider live field testing. Multiplayer, a second PC and all hardware are not verified. Blueprint construction remains an unverified compatibility area; disable incompatible integrations if they cause spawning failures. This beta is unsigned.

Speech stays local. Optional cloud dialogue sends relevant conversation/game context to the chosen provider using your own connection. Review reports before sharing. AI tools were used in code, model and artwork creation; generated portraits and cover art are illustrations, not gameplay screenshots.

Rune's original source is GPLv3-only with the supplied linking exception. Third-party software retains its own terms. Native Valheim animation files are not distributed. Rune is not affiliated with Iron Gate, Coffee Stain or OpenAI.
