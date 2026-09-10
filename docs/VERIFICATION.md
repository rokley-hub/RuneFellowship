# Release verification — desktop 0.4.29 / gameplay 0.3.16

Checked on 10 September 2026 on the development PC. The source and release records distinguish local candidates from public versions.

## Established

- The desktop builds with the visual refresh. Native WPF renders exercise Settings sections, companion tabs, Mods, Commands and shared dialogs at the minimum 1280 × 760 window size. Layout checks cover control bounds, voice test access, retained companion drafts, per-companion tracking and the public command catalogue.
- The existing updater has 24 deterministic checks covering source selection, payload hashes, path restrictions, blocked installation while Valheim is open, profile preservation and rollback. Windows PowerShell 5 interrupted-journal recovery and the detached-worker fixture passed for the preceding updater implementation. These establish the tested cases, not a live public download or arbitrary power-failure recovery.
- The official NuGet feed returned no known vulnerable packages for the desktop project's direct and transitive packages on this date. This is a scoped .NET dependency check, not an audit of every downloaded Python/model/native component. Builds can still display a cached NU1900 restore warning; the separate online audit completed successfully.
- Earlier default online setup tests installed speech, started services, generated a synthetic Kokoro sentence and transcribed it with Whisper. Text-only startup passed. Existing installer regressions cover damaged downloads and preservation/rollback behavior.
- Source export uses an allowlist and a privacy scan. The individual source archive includes its own report. No private profiles, credentials or conversations are part of release payloads.

## Remaining limits

- No second physical PC or fresh Windows VM test. Broad multiplayer, non-NVIDIA hardware, GPU-heavy gameplay and every optional voice stack's fresh online installation remain unverified.
- Blueprint compatibility is still a beta limitation. A newer integration loaded its world but failed fixture setup; successful building was not established.
- The installed 0.4.28 updater transaction succeeded and its normal launcher opened on the development PC. Synthetic Kokoro generation and Whisper recognition passed separately. This does not establish a public-network update download or real microphone/headset playback end to end.
- The beta is unsigned. Licence notices and source accompany Rune; the engineering review is not blanket legal clearance. Nexus review remains separate.

Do not read successful renders or synthetic command checks as proof that every action works in ordinary gameplay. Keep release notes accurate about these limits.

## 0.4.29 release additions

The typography, quiet background, notification panel and Play voice-target fix are included. Actual WPF renders use isolated fixture data. The Play click regression failed before the fix and passed afterward for three companions while preserving editor drafts. Notification fixtures cover app/mod updates, dependency issues, failed checks, Nexus limitations and clearing resolved notices. No gameplay changes were introduced.
