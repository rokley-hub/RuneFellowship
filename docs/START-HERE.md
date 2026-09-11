# Rune Fellowship — Windows beta

Rune is an unofficial Valheim companion app. Desktop 0.4.41 uses gameplay plugin 0.3.26. Valheim is required and is not included. This is a beta, not a stable release or an official game product.

## Install

Extract the entire online-installer ZIP and open **Install Rune.exe**. Choose a separate installation folder and the components you want. Setup downloads selected runtimes, speech libraries and models from their original publishers and verifies the locked file checksums. It shows download sizes and keeps a cache for retrying. No other authors' game mods are bundled. You do not need to arrange dependency folders or compile Rune.

Kokoro is the fast voice option. Chatterbox Turbo and V3 are optional expressive voices. Local Qwen is needed for Local or Hybrid mode. For ChatGPT mode, install the official Codex helper separately and sign in through Rune's account settings with your own eligible account. There is no API-key option; account usage limits apply. Voice stays local in every mode.

## Set up a game

1. In **Mods → Profile options**, locate Valheim and create or import a profile. A new profile can download its requirements; an existing profile has **Set up required mods** in the same menu. Downloads come from original packages. Required-mod setup currently uses Thunderstore regardless of browsing preference.
2. **Installed** manages the selected profile. **Browse mods** uses the source selected in **Settings → Mods**. Nexus downloads and update checks use its website; download a ZIP and use Import ZIP. Select an installed mod to enable/disable it, open its page, edit its own config or remove it. Profile options includes XML import/export and backup restoration.
3. **Settings → AI connection** chooses Local, Hybrid or ChatGPT. Local uses Qwen for conversation and commands. Hybrid uses ChatGPT for commands and Qwen for conversation. ChatGPT mode uses ChatGPT for both and does not need Qwen.
4. **Settings → Voice & mic** controls input, output, language, performance and spoken replies. Turning spoken replies off releases voice resources while retaining microphone commands and written replies. Bind keys in **Settings → Controls**; a single modifier such as Alt is supported.
5. In **Companions**, select a companion and edit Personality, Voice or Behaviour. Voice has **Test voice**, available before saving. Save companion commits your edits. Show on map saves immediately for that companion. Summon and Unsummon control presence in the game; dismissal may first need to return borrowed tools or cargo.
6. Start modded from the Play page. Start with a test world. The Commands page gives examples, expected behavior and prerequisites. Speak to one selected companion at a time. Click a companion card on Play to change the voice target; its gold border and the footer show who is selected. Watch the game task overview for actual progress and blocked reasons.

## Updates and recovery

Open **Settings → Updates & support → Check for updates**. Rune also checks its official GitHub releases once at startup. Downloading/installing requires an explicit action. Close Valheim before installing. Rune saves current companion edits, closes its own services, installs the verified app/plugin together, and reopens. Preferences, memories, models and other mods are preserved. See [UPDATES.md](UPDATES.md).

Users on 0.4.26 or earlier need the online installer once to gain in-app updates. Run a new installer into the existing installation to repair files or add missing optional components. Keep backups until the new version works. Verify Rune and Uninstall Rune are included; uninstall preserves personal data by default and never deletes Valheim saves.

## Beta limits and support

Building compatibility remains unverified with the newer blueprint integration. An older integration caused a player-spawn failure; disable the incompatible integration if affected. Do not assume all listed building intentions can complete with every installed mod version.

Broad multiplayer, lower-end/non-NVIDIA hardware, gameplay under heavy GPU load and full fresh installation of every optional voice stack need field testing. Hardware minimums have not been established. Initial model loading can be slow. This beta is unsigned; file hashes detect damage but do not authenticate the publisher. Do not disable Windows security.

Open **Settings → Updates & support** for the player guide, local session report and GitHub issue page. Review any report before sharing; never upload account folders or your entire installation. See [PRIVACY.md](PRIVACY.md).

## Detailed companion and mount guide

See [PLAYER-GUIDE.md](PLAYER-GUIDE.md) for direwolf riding, stopping, jumping and combat; food and supplies; F7/F8 status displays; map tracking; and troubleshooting.
