# Rune Fellowship 0.4.41 beta

Desktop and Thunderstore package **0.4.41**, with gameplay plugin **0.3.26**. This release brings the direwolf and companion improvements developed since public 0.4.29 together in one update.

## Changes

- A custom saddled direwolf companion with native wolf movement and combat animation, riding, stamina-aware jumping and biting.
- Corrections for see-through body faces, materials, saddle attachment, chest/belly shape and torso weights. The rider anchor follows the animated saddle, and visible reins connect to the rider's hands.
- Mounted movement now sends a full stop when forward input is released; auto-run can be cancelled by manual movement or braking.
- A model-derived mount HUD icon, wolf-head map marker and a distinct illustrated direwolf companion portrait.
- Companion combat progression, weapon scoring and tactical choices; dwarf crossbow reloads and offensive elemental projectile magic with food/eitr requirements. Summoning and support staves remain unsupported.
- More robust local-model context budgeting and incomplete-response handling.
- A cleaner fellowship overlay without skill levels or the team/proximity row. F8 now shows useful companion information: health, stamina, food, resources, equipment, inventory and current work.
- Clearer Rune Fellowship cover artwork and a substantially expanded player guide covering mounts, companion needs, controls, profiles and troubleshooting.

## Install or update

New users: download **Rune-Online-Installer-0.4.41-beta.zip**, extract the entire ZIP and run **Install Rune.exe**. Existing users can select their current installation. Optional components download from their original publishers.

Users on desktop 0.4.27 or newer can use **Settings → Updates & support → Check for updates**. Close Valheim before applying it. Older users need the full installer once. The update ZIP is for Rune's updater, not a first-time installation.

The Thunderstore package includes the same full installer as **Rune Installer.zip**. An external mod manager installs/updates the game plugin but does not run the desktop installer. Keep the app and loaded game plugin current together.

## Verification and limitations

Release builds, deterministic companion-policy checks and package/source integrity checks are documented in the supplied verification notes. Prior screenshots establish that the custom mount loads and can be ridden. The latest seat, reins and stop corrections have offline/build checks; wider live riding and multiplayer remain beta testing areas. No ordinary player saves were opened for release checks.

Blueprint construction remains an unverified compatibility limitation. This beta is unsigned; there is no second-PC or all-hardware validation. Optional speech/model requirements vary by hardware. Other authors' game mods and Valheim animation files are not bundled.

[Expanded player guide](https://github.com/rokley-hub/RuneFellowship/blob/main/docs/PLAYER-GUIDE.md) | [Issues](https://github.com/rokley-hub/RuneFellowship/issues)
