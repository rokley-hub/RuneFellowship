# Rune Fellowship - player guide

**Desktop 0.4.41 beta / game plugin 0.3.26 / Thunderstore package 0.4.41.** Rune is an unofficial Windows companion app for your own licensed Steam copy of Valheim.

## Standalone plugin candidate 0.4.43 (not yet published)

Plugin 0.3.27 adds F6 companion setup and basic orders without the desktop app. Install BepInEx, Jotunn and the plugin in an isolated modded profile, enter a solo test world and press F6. Choose one of three slots, name/appearance and Summon. Follow, Stay, Defend, gathering, tool lending, return and unsummoning use the same native executor. F8 remains the information overview. The desktop app is optional for voice, AI dialogue and its advanced workflow. The in-game slots are separate from desktop profiles.

Build and deterministic checks passed, but the fresh-install menu and gameplay still require native validation. Thunderstore's permission is conditional on useful standalone functionality; the candidate is not yet approved or uploaded.

## Optional desktop installation or update

Download [the Windows installer from GitHub](https://github.com/rokley-hub/RuneFellowship/releases/tag/v0.4.41-beta), extract the entire ZIP and open **Install Rune.exe**. The desktop online installer is not permitted on Thunderstore. Its reported antivirus detections remain unresolved; do not disable protection or bypass quarantine to install it. Keep the extracted files together. Setup downloads selected runtimes and speech/model components from their original publishers.

**Install with App** installs the game integration in an external mod manager. It does not run the Rune desktop installer. The desktop app is needed for voice and AI dialogue; the standalone candidate adds basic in-game configuration and orders.

Existing users on 0.4.27 or newer can use **Settings → Updates & support → Check for updates**. Close Valheim before applying an update. Earlier users need the full installer once. Choose your existing Rune installation to retain preferences, companions and models. Keep the update backup until the new version works. Updating an external mod-manager profile does not update the desktop automatically.

## First session

1. Open Steam and make sure Valheim is installed and available to your account.
2. Open Rune using its installed shortcut or **Open Rune.exe**. In **Mods**, locate Valheim and create/select a Rune-owned profile. **Profile options → Set up required mods** downloads dependencies from their original packages. Review any optional building integration before launching; construction remains a beta limitation.
3. In **Settings → AI connection**, choose Local, Hybrid or ChatGPT. Local uses Qwen for conversation and commands; Hybrid uses Qwen for conversation and ChatGPT for commands; ChatGPT uses ChatGPT for both. The optional ChatGPT connection uses your own account through a separately installed official Codex helper. Account limits apply. Rune's current connection does not take an API key.
4. In **Settings → Voice & mic**, select microphone, output, recognition language and spoken replies. Kokoro is the lighter voice choice; expressive Chatterbox options need their selected components. All speech recognition and voices remain local in every AI mode. ChatGPT mode does not need Qwen for thinking, but still needs local speech components for audio.
5. Create or select companions, choose appearance, personality, voice and behaviour, then save. Use **Test voice** to check the selected engine. Click a companion card on **Play** to choose who receives your voice and typed orders.
6. Use **Start modded** for the selected Rune profile. Try a test world first. Summon your companion through the app, then try a simple typed **Follow me** before testing voice input.

## Controls and the fellowship display

Use your configured bindings in **Settings → Controls**; the defaults below can be changed. Push-to-talk is held to listen. In always-on mode, the microphone binding toggles mute. **Switch companion** changes the addressee.

- **F7** shows or hides the compact fellowship overlay. It shows health, weapon and current activity without skill levels or the redundant team/proximity row.
- **F8** opens the companion overview. Select a companion to inspect health, combat stamina, mount stamina where applicable, food, eitr, equipment/durability, carried supplies, current task, goal and next step. Values only appear where relevant or available.
- While the overview is open, drag the overlay by its header to reposition it. Closing the overview locks its position.

The overview is for information. Change appearance and presence in the desktop app; give orders through voice or Conversation. A written acknowledgement is not proof of completion: check the current task, blocked reason and the actual game result.

## Your direwolf mount

Choose **Direwolf** as a companion's appearance in Rune, save and summon it. This is a custom saddled grey direwolf with a fuller chest, tucked belly and a fixed riding saddle. The desktop portrait is stylized companion artwork; it is not intended as an exact preview of the mesh. The riding HUD uses a model-derived wolf icon and map tracking uses a wolf-head marker.

Approach the saddle and use Valheim's interaction prompt to mount. No separate saddle item is required for a summoned Rune direwolf, and the fitted saddle cannot be removed. A regular Wolf companion is a different appearance and is not this mount.

| Action | Control / behaviour |
| --- | --- |
| Move and steer | Use Valheim's movement and view controls. |
| Run | Use the game's Run control while moving. |
| Stop | Release forward movement, or use backward movement/Block to brake. If auto-run is active, toggle it off or brake. |
| Auto-run | Uses Valheim's auto-run control. Manual movement or braking cancels it. |
| Jump | Use Jump while grounded with enough mount stamina. Swimming, attacks and other blocked movement states prevent jumping. |
| Bite | Use primary Attack to start a native wolf bite. It needs stamina and a reachable target. |
| Dismount | Secondary attack or dodge retains native dismount behaviour. |

Jumping spends 20 mount stamina; a bite spends 10. The saddle's stamina is separate from the player's and the companion's combat stamina. Running uses mount stamina too. Let the mount rest to recover. Hunger slows its saddle-stamina recovery; use suitable wolf food. The **Hungry** label is a feeding status, not an error.

The direwolf uses the installed game's wolf skeleton and animation system, including idle, walk, run, jump and fighting. Rune distributes its generated mesh and texture, not extracted Valheim animation files. The rider seat follows the animated saddle, with visible reins leading to the rider's hands. The latest stopping, seat and rein corrections have build/offline checks; wider live riding and multiplayer still need field testing.

## Food, equipment and supplies

Companions have physical requirements. A humanoid can use appropriate tools, armour and weapons; a wolf fights with its teeth and cannot use humanoid equipment or perform every tool task. A command cannot bypass those limits.

The overview separates current combat stamina from mount stamina and shows available food/eitr information. Ranged attacks need the right weapon and ammunition. Bows, crossbows and magic currently require the **Dwarf** player rig. Crossbows use matching bolts and a timed, stamina-consuming reload; a nearby threat or interrupted guard can delay that reload. Offensive elemental projectile staves consume eitr supplied by carried food. Summoning, support and blood-magic staves are not supported. Try **shoot bolts** or **use elemental magic** when the appropriate gear is carried. Food and resource availability affect what can be done. Check the companion's inventory, equipment, permissions and current blocked reason before repeating an order.

Useful crafted equipment can be equipped; other results remain in the companion's inventory until needed or requested. Gathered cargo and borrowed tools are not disposable: a return or dismissal may need to hand them back first. Equipment durability is shown where relevant; a teeth attack does not have item durability. Combat progression remains part of the simulation even though skill numbers are hidden from the compact overlay.

## Useful orders

Speak naturally or type in **Conversation**. The desktop **Commands** page explains supported intentions and prerequisites; it is not a list of exact phrases to memorize.

| Intent | Example | Requirements / result |
| --- | --- | --- |
| Follow | Follow me | Cancels current work and recalls the companion. |
| Wait | Wait here | Holds position. |
| Protect | Defend me | Requests protection against nearby threats. |
| Gather | Gather 10 wood | Bounded task; needs reachable resources, a suitable body and tools where required. |
| Craft | Make a stone axe | Checks a learned recipe, ingredients, station requirements and inventory room. |
| Recipe information | What does a stone axe need? | Asks for information rather than starting work. |
| Cancel crafting | Clear crafting | Clears crafting/material planning without discarding inventory. |
| Check progress | What are you doing? | Reports current work and blockers. |

Weapon switching, guarding, attempted parries, ranged attacks and focusing enemies depend on equipped items, body capabilities and the situation. Recognition of an order does not guarantee that the environment permits it.

## Map tracking and profiles

Enable **Show on map** separately for each companion on its desktop companion page. This preference saves immediately. The direwolf uses a wolf-head marker; tracking can retain a last-known location, so a marker alone is not proof that the companion is currently visible or nearby.

Select your intended profile before installing, enabling, disabling or configuring mods. Rune-managed profiles should be launched through Rune. Rune already supplies its plugin; avoid duplicate copies. Thunderstore and Nexus browsing are separate choices. Nexus downloads and update checks use the website; import the downloaded ZIP. Changing browsing preference does not replace your installed profile.

For an external manager, install Rune and its declared dependencies in that profile, let the game create configuration, then close it. Set **General / BridgeFolder** in **BepInEx/config/local.rune.companion.cfg** to the **bridge** folder of the Rune installation. Keep that desktop instance running when launching the same profile. This route still needs a dedicated release-level end-to-end test.

## Troubleshooting

**Game will not start / Steam error:** open Steam, confirm Valheim is available, and launch the intended profile again. Check the actual startup error and enabled mods. Installing Rune does not replace Steam or grant a game license.

**Wolf keeps moving:** disable auto-run and use backward movement or Block to stop. Check that the loaded gameplay plugin is **0.3.26** and remove duplicate older Rune plugins after closing the game. This release corrects native saddle control handling that could keep a mount walking after releasing movement. If it persists, report whether the wolf moves across the ground or only plays a moving animation.

**Jump does nothing:** check mount stamina, grounded state, swimming and current attack/stagger. Use the game's Jump binding. Verify the loaded plugin version; an old saddle implementation can treat jumping as dismounting.

**Holes in the wolf or a floating rider:** update both app and profile plugin, restart the game, and verify the loaded plugin version. This release includes corrected face winding, materials, chest weighting and an animated saddle anchor. If the issue persists, provide side/front views and say whether it happens idle, running or turning. Do not delete companion data as a workaround.

**Speech is transcribed but no action occurs:** verify the selected companion, permission for spoken/typed orders, required tools/resources and the current task's blocked reason. Try typed Follow me to separate recognition from execution.

**No voice:** turn Spoken replies on, check volume/output and use Test voice. Confirm the chosen engine is installed. Turning spoken replies off intentionally leaves text and microphone commands available.

**Setup fails or a service port is occupied:** use the fully extracted installer and retry; cached downloads can be reused. Close the other Rune instance before starting another installation. Keep the exact error for support. Do not upload account folders or the whole installation.

**Construction fails:** blueprint integration remains a known beta limitation. An older integration caused repeated player spawning; disable an incompatible integration if affected. A newer integration loaded but has not passed successful construction validation. Required-mod setup can enable it, so review it again after rerunning setup.

## Support, privacy and limits

Open **Settings → Updates & support** for updates, the player guide and local reports. [Report issues on GitHub](https://github.com/rokley-hub/RuneFellowship/issues) with desktop/plugin versions, selected profile, enabled mods, the exact command, expected result and what actually happened. For riding issues include the movement state, stamina and a useful screenshot. Review reports first and remove private dialogue, personal paths and credentials.

Speech stays on your PC. Cloud dialogue sends relevant conversation and game context to the chosen provider. Downloads contact their original publishers. Notifications show available updates or failed checks; they do not automatically install changes.

This unsigned beta has development-PC and deterministic/offline checks, not broad multiplayer, every optional voice stack, all hardware or a second-PC guarantee. Rune source is GPLv3-only with the included Valheim/Unity linking exception. Other authors' software retains its own terms. AI tools were used for code and generated artwork/model assets. Rune is not affiliated with Iron Gate, Coffee Stain, OpenAI or Thunderstore.
