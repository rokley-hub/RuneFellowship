# Rune Fellowship

An unofficial Windows app and Valheim companion mod with local speech, natural-language orders, companion profiles, mod management and an in-game task overview.

**Release candidate: not yet published or certified stable.** See the setup guide, privacy description and known limitations in `docs`. The app does not include Valheim. Donations are voluntary and do not unlock features. Rune is not affiliated with Iron Gate, Coffee Stain or OpenAI.

## Install and play

Use the packaged Rune app; you do not need to compile this source or arrange DLL files yourself. Install Valheim through Steam using your own license, then:

1. Open Rune's **Mods** page, locate Valheim if needed, and create or select a Rune-owned profile.
2. A new profile automatically downloads and installs **BepInExPack_Valheim**, **Jötunn**, **PlanBuild** and their dependencies. For an existing profile, click **Install Rune requirements**. Rune keeps compatible installed versions and enables the required packages.
3. Use **Browse Thunderstore** on the Mods page for any additional mods. No separate mod manager or manual ZIP extraction is required.
4. Set up your microphone, voice and companion, then use **Start modded**.

Internet access is needed to download missing requirements. Rune shows download/install progress. If setup fails, the profile is kept and **Install Rune requirements** retries it. Close Valheim before changing mods. See [the player setup guide](docs/START-HERE.md) for the app and optional voice/model packs.

The build instructions below are **for developers changing Rune's source**, not player installation steps.

## Components

- `src/Voice`: .NET 8 WPF desktop with shared gameplay command rules.
- `src/Mod`: Valheim/BepInEx game executor; language models cannot invent unsupported actions.
- `src/Shared`: protocol, capability catalogue and combat policy.
- `audio`: loopback speech recognition and voice services.
- `blueprint-library` and `tools`: original shelter definitions and their generator/checker.
- `tests`: deterministic protocol/game-policy tests; native gameplay validation additionally requires an isolated game world.
- `packaging`: release staging, allowlisted source export, integrity checks, installer and rollback tooling.

ChatGPT mode uses the user's own account through the official user-installed helper. Local mode uses Qwen. All microphone recognition and companion speech remain local. Consult `docs/PRIVACY.md` before enabling cloud dialogue.

## Developer build: desktop and tests

Install a .NET 8 SDK on Windows, then run from this source folder:

```
dotnet restore src/Voice/RuneVoice.csproj --configfile NuGet.Config
dotnet publish src/Voice/RuneVoice.csproj -c Release -o build/desktop
dotnet run --project tests/RuneTests.csproj
```

## Developer build: game plugin

Compiling the plugin requires reference assemblies from your own licensed Valheim installation and the original BepInExPack_Valheim and Jötunn packages. Rune can download and install those packages for normal play as described above. This source project's build expects a separate reference folder containing `BepInExPack/BepInExPack_Valheim/BepInEx/core/BepInEx.dll`, `0Harmony.dll` beside it, and `Jotunn/plugins/Jotunn.dll`. Arrange that folder only if you are building the plugin yourself; the app's installed profile layout is different.

```
dotnet build src/Mod/RuneCompanion.csproj -c Release -p:ValheimDir="YOUR_VALHEIM_FOLDER" -p:DependencyDir="YOUR_DEPENDENCY_FOLDER/"
```

No game assemblies or third-party mod binaries are stored in this source export. Tests must use new worlds/characters and may not run against important saves.

## License

Rune's original source is GPLv3-only with the additional permission in `LINKING-EXCEPTION.md` for Valheim/Unity and the listed modding components. See `LICENSE`. Third-party software, models and assets retain their own terms. This source release does not grant rights to redistribute the Valheim game.

Packaging scripts currently expect the developer staging layout documented in `packaging/BUILDING-RELEASE.md`; runtime binaries and model packs are separate inputs. A source checkout alone does not contain multi-gigabyte models.
