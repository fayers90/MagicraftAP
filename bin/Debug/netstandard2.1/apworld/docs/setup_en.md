# Magicraft Archipelago — development build

Install `magicraft.apworld` using Archipelago Launcher > Install APWorld. Generate locally using a Magicraft YAML and upload the generated `.archipelago` file to archipelago.gg.

**This is not yet a playable client.** The C# BepInEx mod does not yet connect to the server, receive items, report locations, read slot data, or report goals.

## Registered checks
- Cumulative chest openings, 0–150 (default 30)
- Story unlocks for NPC3, NPC4, NPC5, NPC6, NPC7 and NPC9

## Options
- `goal`: chapter_3, normal_clear, hard_clear, endless (provisional mappings)
- `chest_checks`: 0–150
- `randomize_npcs`: true/false
- `npc_unlock_checks`: true/false
- `room_checks` and `elite_boss_checks`: reserved options, **no corresponding locations yet**
- `starting_relics`: basic_four or none

If you disable NPC unlock checks, you must enable at least one chest check.

## Not yet implemented
- Exact room, elite and boss location definitions
- Camp shop purchases, talents, research and other purchase checks
- Game-side NPC randomization configuration and AP item receipt
- Goal completion detection and AP status updates
- Reliable item-access logic for progression-sensitive seeds
