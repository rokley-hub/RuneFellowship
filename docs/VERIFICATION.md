# Release verification - 0.4.41 beta / gameplay 0.3.26

Prepared on 11 September 2026. See the accompanying release-check report for this build's results.

## Established before packaging

The custom direwolf loads in the game and has been ridden in user-provided screenshots. Earlier visual reports drove corrected winding/materials, the animated saddle anchor and torso reshaping. Offline sampling of 523 frames across 12 native clips checked the skin and saddle tracking, not the final player pelvis in live gameplay. The latest movement correction sends native Stop rather than an ignored Turn command. Reins use the rider's hand transforms. Source/build verification of these changes does not establish all live scenarios.

Existing deterministic suites cover protocol/capability rules, combat progression and scoring, recording, policy, tactics and ranged resources. The prior updater has 24 checks covering source selection, hashes, paths, blocked installation while Valheim is open, profile preservation and rollback. Earlier isolated installer/speech checks exercised default downloads, app startup, Kokoro synthesis and Whisper recognition. These earlier results are distinct from fresh tests listed in this release's check report.

Release packaging uses a clean prior installer template, newly built authored binaries, explicit current source assets and public documentation. It does not archive a live installation. Source export excludes private operational notes, profiles, credentials, conversations, logs and build output. The source includes the three generated direwolf resource files required to compile the main plugin. Native game animation files are not distributed.

## Remaining limitations

The latest mount stopping, rider-seat alignment and reins need further live field testing. The F8 overview and overlay changes have source/build checks. Broad multiplayer, every optional voice stack, a second PC, non-NVIDIA hardware and heavy GPU gameplay remain unverified. Blueprint construction is not confirmed with the newer integration. The beta is unsigned. Offline tests and packaging checks do not demonstrate every action in an ordinary game world. No ordinary save is opened by release checks.
