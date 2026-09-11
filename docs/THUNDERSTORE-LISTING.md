# Rune Fellowship

![Rune Fellowship promotional banner](https://raw.githubusercontent.com/rokley-hub/RuneFellowship/main/docs/images/rune-fellowship-cover.png)

**Package 0.4.43 | game plugin 0.3.27 | optional desktop 0.4.41 beta**

Basic companions work entirely in the game: press **F6** to name and summon a companion, then use Follow, Stay, Defend, gathering and return controls. This package contains the game plugin, icon and documentation. The Rune desktop app and online installer are not included.

Build a fellowship of companions in Valheim. Use the in-game controls for basic companions, combat and a rideable direwolf. The separate desktop app adds voice, AI dialogue, its own companion profiles and advanced orders; it is optional for basic gameplay.

## New in package 0.4.43

- Added an in-game F6 menu with three saved companion slots, names and six appearances. No desktop setup is needed.
- Added direct Follow, Stay, Defend, Gather 20 wood/stone, Lend tools, Return and Unsummon controls.
- Kept F8 as the information overview, with a button opening the separate controls.
- Removed the desktop online installer from the archive.

## Companion features

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

## Install and start without the desktop app

1. Install this package in a Valheim mod-manager profile. BepInEx and Jotunn are required and declared as dependencies. For manual installation, install those dependencies and copy the supplied `BepInEx/plugins/RuneFellowship/RuneCompanion.dll` into the matching folder of your intended profile.
2. Open Steam and launch that modded profile into a **solo world** with a living character. Try a separate test world first.
3. Press **F6** (also shown in the empty fellowship overlay). Select one of the three slots, choose a name and appearance, and select **Summon**. The default choices include Rune, Odin the direwolf and Eira.
4. Use the menu to Follow, Stay, Defend, gather 20 wood or stone, return cargo, or unsummon. Humanoids can borrow tools. Resource gathering still requires reachable resources and appropriate tools; wolves cannot use tools or armour.
5. Press **F8** for health, food, stamina, equipment, inventory and current activity. F7 toggles the overlay. F6 controls can be rebound in the plugin configuration.

Companion identities and world state persist. Names and appearances for the three in-game slots are saved in the plugin configuration after a successful summon. To change a summoned local companion's body, first return cargo and borrowed equipment, then unsummon it and change the selection. A companion saved farther away is not duplicated or teleported: Summon reports its saved position. Unsummoning an occupied mount or a companion carrying cargo/borrowed gear is refused.

Existing app-created companions nearby also appear in F6 and can receive basic orders without resetting their identity or work permissions. In-game slots and desktop companion profiles are separate; there is no automatic profile import between them. Rune-managed profiles already supply a plugin: avoid duplicate DLL copies or letting a desktop update overwrite your chosen plugin version. This plugin retains desktop 0.4.41's command protocol, but the app is not necessary for the F6 workflow.

## Optional voice and AI dialogue

The separate Rune desktop app supplies speech, AI dialogue and its own advanced command workflow. [Source and separate desktop releases](https://github.com/rokley-hub/RuneFellowship) are on GitHub. No installer, desktop application, speech runtime or model is bundled in this Thunderstore package.

Thunderstore declined the desktop online installer because it could not audit all downloaded components and antivirus vendors flagged it. Those antivirus findings remain unresolved; this plugin packaging change is not a security clearance for the installer. Do not bypass quarantine or disable protection to install it.

## Riding and companion information

In **F6**, choose a free in-game slot, select **Direwolf**, then **Summon**. Interact with its fixed saddle to ride. Movement and Run use Valheim controls; releasing forward movement stops it unless auto-run is active. Backward movement or Block brakes. Jump uses mount stamina; primary Attack starts a wolf bite. Secondary attack or dodge dismounts. Hunger slows mount-stamina regeneration.

F7 toggles the compact fellowship overlay. F8 opens companion information and unlocks dragging the overlay header. Basic setup and orders are available in the separate F6 menu; the desktop app is optional. Body capabilities, food, stamina, equipment, tools and resources limit what companions can do.

[Detailed player guide and troubleshooting](https://thunderstore.io/c/valheim/p/RuneFellowship/RuneFellowship/wiki/5843-getting-started-and-player-guide/) | [GitHub source and releases](https://github.com/rokley-hub/RuneFellowship) | [Report an issue](https://github.com/rokley-hub/RuneFellowship/issues)

## Beta status

The plugin build, 72 new standalone command checks and existing deterministic suites passed. Background native checks passed in a fresh isolated solo world with the Rune app closed: default Rune/Odin summoning, Follow/Stay orders, opening and closing the menu through its runtime methods, native saddle mounting, mounted idle, occupied-mount dismissal protection, world save/reload, ownership persistence and duplicate-summon prevention. Tested with game plugin 0.3.27, BepInExPack 5.4.2350 and Jotunn 2.29.2. The windowless test does not verify physical F6 keyboard input, rendered menu layout, riding animations, full terrain traversal or optional desktop integration. The mount has been loaded and ridden in live screenshots; the latest stop/seat/rein corrections still need wider live field testing. Multiplayer, a second PC and all hardware are not verified. Blueprint construction remains an unverified compatibility area; disable incompatible integrations if they cause spawning failures. This beta is unsigned.

Speech stays local. Optional cloud dialogue sends relevant conversation/game context to the chosen provider using your own connection. Review reports before sharing. AI tools were used in code, model and artwork creation; generated portraits and cover art are illustrations, not gameplay screenshots.

Rune's original source is GPLv3-only with the supplied linking exception. Third-party software retains its own terms. Native Valheim animation files are not distributed. Rune is not affiliated with Iron Gate, Coffee Stain or OpenAI.
