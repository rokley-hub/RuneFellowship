# Rune Fellowship — private testing beta

Rune is an unofficial Windows companion for Valheim. This is a test build, not a stable release or an Iron Gate product. Valheim itself is required and is not included.

## Install

Extract the core ZIP into a download folder. Put optional pack ZIPs beside that extracted folder. Open **Setup Rune.cmd**, choose a separate install folder, and select the packs you want. Setup does not need administrator rights when installing in your own user folder. Keep the download until testing succeeds.

The core contains the desktop app, bundled .NET and Python runtimes, English speech recognition and Kokoro voice. No Python or Codex desktop installation is required for local audio. Install **Local brain** for Qwen dialogue. For ChatGPT, install the official Codex CLI/helper yourself and choose it in Rune's ChatGPT settings if it is not found automatically. Sign in with your own account; availability and usage depend on your account. No API key option is provided.

**Chatterbox Turbo** needs the expressive-runtime pack and turbo pack. **Chatterbox V3** needs expressive-runtime and v3. Both can be installed together. Initial voice loading can take considerably longer than subsequent replies. Start with Kokoro if expressive speech is too slow. English recognition is bundled; German/Dutch recognition requires the in-app download.

## Set up your first game

1. In Mods, locate Valheim if needed and create a new Rune-owned mod profile. Your r2modman profiles are not required.
2. Rune automatically downloads **BepInExPack_Valheim**, **Jötunn**, **PlanBuild** and their dependencies when you create a profile. For an existing profile, click **Install Rune requirements** on the Mods page. Compatible installed versions are kept and required packages are enabled. Progress appears on the page; if a download fails, keep the profile and use the same button to retry. Close Valheim before installing. Rune also attaches its companion plugin to its profiles. You do not need to download ZIPs in a browser, arrange DLL folders, compile source, or use another mod manager. Use **Browse Thunderstore** for additional mods.
3. In Settings, choose the brain, microphone and output device. Set language, bind your microphone key and check volume. Microphone and companion speech can be controlled independently.
4. In Companions, choose a voice and use **Test voice** before playing.
5. Start modded from Rune. Begin in a new test world with a new character. Do not use important saves for initial beta testing.
6. Use the Commands page for supported actions and their prerequisites. Follow, stop and defense commands interrupt work. Physical actions still require materials, equipment, reachable targets and native game prerequisites.

## Update and recover

Close Rune and Valheim, then run the new core's Setup Rune.cmd and select the existing beta install folder. Application files are verified before copying. Personal bridge data and mod profiles are not overwritten. Interrupted core installs restore changed files. Run **Rollback Rune.cmd** to restore the last core installation; this does not undo gameplay, profile changes or separately installed model packs.

Optional packs are hash-checked and fully staged before activation. The core installer reports a pack failure separately, so you can retry it. Do not edit packaged runtime files in place.

**Verify Rune.cmd** checks the installed core against its file manifest. Hashes detect damage; an unsigned manifest does not establish publisher identity. Use only the download received from the publisher. This beta is unsigned; do not disable Windows security.

**Uninstall Rune.cmd** removes unchanged core files and retains profiles, memories, optional packs and backups. It never deletes Valheim saves. Inspect remaining data before deleting the install folder yourself.

## Requirements and limitations

Windows x64; a licensed PC installation of Valheim; an Internet connection for mod downloads, account sign-in/cloud dialogue or optional recognition downloads. Hardware minimums have not been established. Space requirements are listed in the release pack manifest; installation needs temporary space for verification and rollback. GPU performance varies; CUDA availability is not a guarantee that expressive speech will remain fast while Valheim is running.

Only one Rune instance should run per PC. Local services use loopback ports 11439, 11441 and 11442. Close another Rune installation before testing this one. Multiplayer/dedicated-server compatibility, arbitrary mod combinations and broad hardware coverage are not certified. Do not describe all game actions as supported: the in-app Commands list and game-reported blocked reasons are authoritative.
