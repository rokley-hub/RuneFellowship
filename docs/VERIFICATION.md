# Release-candidate verification

Desktop 0.4.21, gameplay 0.3.14. Checked on 8 September 2026.

## Established

- The allowlisted source export builds both the Windows desktop and game plugin in a separate checkout. No game assemblies are included in the export.
- 316 command/protocol, 31 recorder and 29 combat-policy checks passed. These are synthetic checks, not a new gameplay session.
- Existing desktop session/command regressions and ChatGPT contract checks passed. The helper started signed out in an isolated account folder, returned a browser-login URL and supported cancellation. No authenticated cloud model request was made for this release test.
- Nine installer tests passed: upgrade/rollback preserve user data; damaged input is rejected; copy failure restores earlier files; overlapping paths, unknown nonempty folders, duplicate entries, unsafe archive paths and changed-file rollback are rejected.
- The real core package installed into a separate test directory. The local-brain and both expressive-model packs passed hash verification and extraction there.
- Portable embedded Python imports the bundled speech dependencies without global/user Python packages. All six Kokoro voices generated synthetic WAV files. English recognition transcribed a synthetic gathering command and conversation sentence correctly.
- With developer PATH entries removed and an intentionally invalid PYTHONPATH, isolated startup brought up its own audio and Qwen services, produced `Rune ready.`, and rendered the welcome screen. Test services used a separate loopback port range and were stopped afterward.
- Chatterbox Turbo and V3 each generated a non-empty synthetic English WAV on the available CUDA device. Cold generation for the short samples took approximately 33.5 and 12.0 seconds respectively; this is not a game-load benchmark or a listening-quality guarantee.
- The welcome screen was rendered and visually inspected. Installer scripts parsed successfully.
- The one-click game-requirements setup passed an actual Thunderstore download/install into a separate empty profile: BepInEx 5.4.2333, Jötunn 2.29.2, HookGenPatcher 0.0.4 and PlanBuild 0.18.4. Required library files existed and were enabled; no game was launched. Requirements tests also cover compatible-version reuse, minimum-version upgrades, disabled dependencies and an incomplete catalogue. The updated Mods page was rendered at 1280×800 and inspected.
- The public source privacy scanner checks known private terms, absolute user-home paths, unexpected emails, credential-shaped text and PNG text/EXIF chunks. See the source archive's own report for the final file count and result.

## Not established

This is not testing on a second physical PC or a fresh Windows VM. Windows Sandbox is unavailable here. Fresh-machine driver/runtime behaviour, lower-end GPUs, non-NVIDIA hardware, broad mod compatibility, multiplayer, live microphone/headset playback on other machines and Valheim FPS under AI load still need field testing.

The desktop build reported an unavailable NuGet vulnerability index; this is not a clean vulnerability-audit result. The game build retains existing assembly-reference and deprecated-Unity-API warnings, with no errors. Build success does not resolve those warnings or establish full gameplay correctness.

No trusted code-signing certificate was available in the current user's signing store. Nothing has been uploaded, published, or sent to testers. Rune's source license is approved; corresponding-source/native dependency obligations, signing and release-host configuration remain publication gates.
