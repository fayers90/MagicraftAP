"""Magicraft Archipelago world: generation-side prototype.

The companion BepInEx client does not yet implement network item receipt,
location reporting, slot-data configuration, or victory reporting.
"""
from typing import Dict, List
from BaseClasses import Item, ItemClassification, Location, Region, Tutorial
from worlds.AutoWorld import World, WebWorld
from .options import MagicraftOptions

BASE_ID = 273900000
NPCS = {1: "Talents", 3: "Research", 4: "Sets", 5: "Training",
        6: "Spell Activation", 7: "Spell Disabling", 9: "Endless Talents"}
NPC_UNLOCKS = {3: "Research", 4: "Sets", 5: "Training", 6: "Spell Activation",
               7: "Spell Disabling", 9: "Endless Talents"}
DIFFICULTIES = ("Normal", "Hard")
# Placeholder enumerations for room/encounter checks: NOT enabled until
# the real game keys, per-difficulty stages and IDs are confirmed.
# Stable IDs are reserved for later releases, avoiding remapping chest/NPC IDs.
ITEMS = {f"Unlock {name}": BASE_ID + 100 + npc for npc, name in NPCS.items()}
ITEMS.update({"Magicraft Relic Cache": BASE_ID + 200,
              "Magicraft Filler": BASE_ID + 201})
LOCATIONS = {f"Chest Opened {n:03d}": BASE_ID + 1000 + n for n in range(1, 151)}
LOCATIONS.update({f"NPC{npc} Unlocked - {name}": BASE_ID + 2000 + npc
                  for npc, name in NPC_UNLOCKS.items()})
# A client-reported event, not a server-addressed check.
VICTORY_EVENT = "Magicraft Victory"


class MagicraftItem(Item):
    game = "Magicraft"


class MagicraftLocation(Location):
    game = "Magicraft"


class MagicraftWeb(WebWorld):
    theme = "magic"
    tutorials = [Tutorial(
        "Magicraft Setup", "Connect the BepInEx client and generate a multiworld.",
        "English", "setup_en.md", "setup/en", ["GatorBloom Studios"]
    )]


class MagicraftWorld(World):
    game = "Magicraft"
    web = MagicraftWeb()
    options_dataclass = MagicraftOptions
    options: MagicraftOptions
    item_name_to_id = ITEMS
    location_name_to_id = LOCATIONS
    origin_region_name = "Menu"
    required_client_version = (0, 6, 0)

    def create_item(self, name: str) -> MagicraftItem:
        classification = (ItemClassification.progression if name.startswith("Unlock ")
                          else ItemClassification.filler)
        return MagicraftItem(name, classification, self.item_name_to_id[name], self.player)

    def create_regions(self) -> None:
        region = Region("Menu", self.player, self.multiworld)
        self.multiworld.regions.append(region)
        enabled: List[str] = []
        enabled.extend(f"Chest Opened {n:03d}" for n in range(1, self.options.chest_checks.value + 1))
        if self.options.npc_unlock_checks.value:
            enabled.extend(f"NPC{npc} Unlocked - {name}" for npc, name in NPC_UNLOCKS.items())
        if not enabled:
            raise ValueError("Magicraft requires chest_checks > 0 or npc_unlock_checks enabled.")
        for name in enabled:
            region.locations.append(MagicraftLocation(self.player, name, LOCATIONS[name], region))
        # Victory is an event location completed by the game client after its
        # selected goal is achieved. It cannot be collected by a LocationChecks
        # packet because event locations have no network location ID.
        victory = MagicraftLocation(self.player, VICTORY_EVENT, None, region)
        victory.place_locked_item(MagicraftItem("Victory", ItemClassification.progression, None, self.player))
        region.locations.append(victory)

    def create_items(self) -> None:
        total = len(self.multiworld.get_unfilled_locations(self.player))
        pool: List[Item] = []
        if self.options.randomize_npcs.value:
            pool.extend(self.create_item(f"Unlock {name}") for name in NPCS.values())
        # If fewer locations than NPC items, precollect the excess items so
        # generation remains possible (e.g. chest_checks=0).
        while len(pool) > total:
            self.multiworld.push_precollected(pool.pop())
        pool.extend(self.create_item("Magicraft Filler") for _ in range(total - len(pool)))
        self.multiworld.itempool += pool

    def set_rules(self) -> None:
        self.multiworld.completion_condition[self.player] = (
            lambda state: state.has("Victory", self.player)
        )

    def fill_slot_data(self) -> Dict:
        return {
            "goal": self.options.goal.value,
            "chest_checks": self.options.chest_checks.value,
            "randomize_npcs": bool(self.options.randomize_npcs.value),
            "room_checks": bool(self.options.room_checks.value),
            "elite_boss_checks": bool(self.options.elite_boss_checks.value),
            "npc_unlock_checks": bool(self.options.npc_unlock_checks.value),
            "starting_relics": [3, 18, 73, 901] if self.options.starting_relics.value == 0 else [],
            "protocol_version": 1,
        }
