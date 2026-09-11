# Publishing Rune Fellowship

Desktop 0.4.41 is paired with gameplay plugin 0.3.26. A prepared archive is not proof of publication. Check the publication record before describing a version as available.

## Release checklist

1. Build from reviewed source and run the checks described in VERIFICATION.md. Keep gameplay, optional-stack and hardware limits explicit.
2. Export the allowlisted source using export_source.py. Exclude private Git history, accounts, profiles, conversations and logs. Include the GPLv3-only licence, Valheim/Unity linking exception, matching source and applicable notices.
3. Prepare a clean online installer. Download third-party runtimes/models from their locked original sources; never package a live installation or other authors' game mods. Retain source/download and licence notices.
4. If a trusted signing identity is available, sign and timestamp binaries before generating final manifests/checksums. Otherwise clearly label the beta unsigned. Hashes alone do not authenticate the publisher.
5. Publish the matching installer, source, update ZIP and update .zip.sha256 together in the official GitHub release. Ensure release tag and filenames match. Verify availability before announcing it. Users on 0.4.26 need the installer once.
6. Refresh the existing Thunderstore package with the matching Rune plugin, nested full online installer, truthful version description, icon, README, licence and required dependency declarations. Do not bundle dependency mods. Explain how to extract the nested installer.
7. Keep the wiki, known limitations and support links consistent with the released version. No background diagnostic uploads are enabled.

Nexus review is a separate host-specific step. The recorded staff-review request is pending; successful local tests or publication elsewhere are not evidence of Nexus acceptance. See ONLINE-LICENCE-REVIEW.md for the documented dependency/integration review and its limits.

Never replace an existing public version silently. Publish a new version for changed binaries, and keep rollback backups until the update works. Secure the release account; checksums hosted beside unsigned update files cannot protect against compromise of that account.
