# Building the release candidate

The staging builder expects a workspace containing `work/rune` (this source project), `outputs/RuneCompanion` (reviewed runtime/model inputs), and writes `outputs/RuneRelease`. Place the `packaging` folder of a source export at `work/rune/release` for this workflow. It never reads the user's bridge, account or mod-profile folders.

When reconstructing this layout from the public export, keep `src`, `audio`, `tests`, `tools` and `blueprint-library` beneath `work/rune`. Copy the exported `docs` into `work/rune/release/docs`, and copy the root `README.md`, `.gitignore`, `LICENSE` and `LINKING-EXCEPTION.md` into `work/rune/release/public`. Run `fetch_notices.py` with network access to populate available upstream notices in `release/licenses`; review its error records rather than treating missing licenses as cleared.

Publish `src/Voice/RuneVoice.csproj` into `work/rune/release/app` using .NET 8. Build `src/Mod/RuneCompanion.csproj` against your licensed game references and copy its RuneCompanion.dll into `work/rune/release/plugin`. Supply the official Python 3.12.10 Windows x64 embedded ZIP as `python-3.12.10.zip`. The builder selects only current audio packages, Kokoro, Whisper small.en, .NET, the freshly built Rune plugin, blueprints, and explicitly selected model packs from the reviewed staging input.

Run `build_release.py core`, then `build_release.py packs`. Make any authored corrections using `refresh_candidate.py`, and finish with `build_release.py seal`. A new core build must start in an empty output folder; it refuses to merge with a previous candidate. Export source with `export_source.py --output NEW_FOLDER --deny PRIVATE_TERM`, repeating the deny option for locally known private names. The terms themselves are not written to the report.

Never supply a live installation as a release archive directly. Keep source exports and release archives separate from private development history and diagnostics. Review the third-party license inventory, release gates, signatures and resulting privacy report before upload.

Tests: `test_packaging.py` validates corruption rejection, archive path validation, update/rollback and failed-copy recovery using temporary fixtures. `Start Rune.ps1 -SmokeTest` runs the installed core against loopback ports 12439/12441/12442, generates a synthetic local-brain reply and renders the welcome screen. It never starts Valheim or asks for live microphone capture. The ordinary runtime uses 11439/11441/11442. Alternate ports prove service isolation, not hardware or OS independence.
