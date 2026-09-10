# Updating Rune

Desktop 0.4.29 includes the updater introduced in 0.4.27. It checks the official rokley-hub/RuneFellowship GitHub releases once at startup; **Settings → Updates & support → Check for updates** checks again. Checking does not install anything. Beta clients can receive newer beta releases; stable clients exclude prereleases.

Close Valheim and choose the available update. Rune saves the selected companion edits, waits for its launcher to close, stops only its own services, applies authored app files and the matching game plugin, and reopens. Rune-owned mod profiles keep their enabled/disabled plugin state. External-manager profiles must update their plugin in that manager.

The update and each payload file are checksum-verified. Update metadata and assets must come from the official GitHub repository. This is not a publisher signature. Profiles, memories, credentials, optional models and other authors' mods are excluded from the payload. Existing speech dependencies are retained: this update format does not migrate runtime/model dependencies.

The release-backups folder and update journal support rollback. Caught failures restore affected files; startup recovers unfinished transactions. Keep incomplete backups. If a backup is damaged, retain it and use the installer to repair the app.

Version 0.4.26 and earlier require the installer once to gain this feature. A future release is offered only if its GitHub release has both Rune-Update-VERSION.zip and Rune-Update-VERSION.zip.sha256; an installer-only release is not offered. Download and extraction also need temporary disk space.
