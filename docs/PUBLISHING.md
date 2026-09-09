# Publication controls

Status: HOLD — local release candidate, not cleared for external distribution.

Prepare public source from the allowlisted export, not from the development directory or its Git history. Rune's license is GPLv3-only with the approved linking exception. Resolve remaining speech dependency obligations before distributing even an unsigned testing build. No repository or source archive is uploaded by these scripts.

After clearance:
1. Choose the public repository/account and Thunderstore team. Start the public repository with the clean source export and no private development history. Add approved binaries, public documentation and issue templates there.
2. Obtain a code-signing identity eligible for the publisher's country/account. The agent cannot complete identity verification, purchase a certificate, or sign without an authorized certificate/service. Do not create a self-signed certificate and describe it as trusted.
3. Sign Rune executables with the chosen identity, timestamp signatures, regenerate file/pack manifests and SHA256SUMS after signing, and verify the final installed package. Hashes alone do not authenticate the publisher.
4. Upload the approved core and optional packs as a draft release. Host large assets on a service whose per-file limits fit them; several model packs exceed common release-host asset limits. Alternatively split/repackage packs and update the installer before publishing. Do not publish an incomplete download set.
5. Prepare the Thunderstore manifest with the actual website and dependencies; use a 256x256 PNG icon and README at ZIP root. Publish only Rune's gameplay DLL there; direct users to the approved desktop download. Validate team/package naming on submission.
6. Invite testers using the feedback template. Invitations are not sent automatically. Announce the known limitations and unsigned status if no signature is available.

No automatic update server is configured. The current updater installs a locally obtained, verified core package and supports rollback. Future automatic download needs a publisher-controlled HTTPS endpoint and authenticated update metadata; do not treat an arbitrary URL or mutable checksum file as trusted.

Official references:
- https://wiki.thunderstore.io/mods/creating-a-package
- https://wiki.thunderstore.io/mods/packaging-your-mods
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- https://www.valheimgame.com/news/regarding-mods/
