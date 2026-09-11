# Checks for 0.4.41 beta

11 September 2026, development PC, background-only release work.

- Desktop Release rebuild and publish: passed, zero errors. NuGet vulnerability information could not be refreshed (NU1900); this is not a clean current vulnerability audit.
- Gameplay Release build: passed, zero errors, 34 existing reference/obsolete-API/unused-field warnings.
- 349 protocol/command checks; 1,535 progression/scoring checks; 31 recorder checks; 29 combat-policy checks; 37 tactics scenarios; 42 ranged-resource/food/command checks: passed.
- 13 local-model context/response checks: passed, including complete-history trimming, rejection of incomplete/null decisions and bounded retry on truncation. These use a simulated HTTP handler, not a live model.
- 24 in-app updater checks: passed, including paths, corruption, failure rollback and private-data/profile preservation.
- 20 package and online-installer regressions: passed under the normal Windows token. The restricted development token could not access Python's owner-only temporary fixtures; rerunning with ordinary temporary-directory access succeeded.
- These are deterministic/build/packaging checks, not a fresh live riding session or a second-PC test. No ordinary Valheim save was opened.

The final source export contains its privacy report. Release archive checksums accompany the downloads. Public asset hashes are verified before publication.
