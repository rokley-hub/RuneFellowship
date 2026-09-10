# Rune Fellowship 0.4.29 beta

This release updates the Rune Windows app. The gameplay plugin remains **0.3.16**; this is not a new building or combat release.

## What changed

- A quieter charcoal background, lighter headings, clearer labels and compact footer controls.
- A Rune logo that dims between warm glowing pulses and respects Windows animation preferences.
- Notifications beside the window controls for app updates, known mod updates, dependency problems and failed checks, with links to the relevant page.
- Click companion cards on Play to switch the voice target. Fixed a refresh that immediately switched it back and discarded unfinished companion edits.
- A combined Summon/Unsummon button, focused Settings sections, clearer AI modes and companion editing tabs.
- Correct spacing between control labels and their keybinding buttons.
- In-app updates from the official GitHub release feed, with payload checks, preservation of personal data and rollback support.
- Clearer intent-based command information, real thinking/voice status and reviewed feedback exports with common personal-data patterns redacted.

## Install or update

**New users and users on 0.4.26:** download `Rune-Online-Installer-0.4.29-beta.zip`, extract the whole ZIP and run **Install Rune.exe**. Existing users can select their current Rune installation folder. Keep a backup until the update works. Optional runtimes and models download from their original publishers; other authors' game mods are not bundled.

**Users with in-app updates (0.4.27 or later):** open **Settings → Updates & support → Check for updates**. The update ZIP and its checksum are for Rune's updater; do not use them as a first-time installer. Close Valheim before applying an update.

**Thunderstore:** the package includes the same complete online installer as **Rune Installer.zip**. Extract that nested ZIP and run **Install Rune.exe**. An external mod manager updates the game integration only; it does not run the desktop installer. Package and desktop version numbers are now aligned at 0.4.29.

## Verification and limits

The Windows build, actual WPF page renders/layout checks, Play card selection/draft regressions, notification cases and updater checks passed on the development PC. The clean installer and update archives are checked for integrity and have matching allowlisted source. No personal profiles, credentials, conversations or logs are packaged.

The beta is unsigned. There is no second-PC or broad multiplayer validation. Building integration remains a known compatibility limitation: a newer integration loaded but successful construction was not established. The gameplay DLL is unchanged. Local speech/model resource requirements still vary by hardware. Nexus mod versions require website checks. These UI and packaging checks do not prove every action works in a live game.

[Player guide](https://github.com/rokley-hub/RuneFellowship/blob/main/docs/START-HERE.md) · [Report an issue](https://github.com/rokley-hub/RuneFellowship/issues)
