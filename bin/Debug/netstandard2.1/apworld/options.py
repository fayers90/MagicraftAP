from dataclasses import dataclass
from Options import Choice, Range, Toggle, PerGameCommonOptions


class Goal(Choice):
    """Choose the victory milestone. The C# client must report completion of the selected goal."""
    display_name = "Goal"
    option_chapter_3 = 0
    option_normal_clear = 1
    option_hard_clear = 2
    option_endless = 3
    default = 1


class ChestChecks(Range):
    """Number of cumulative chest openings that give checks. Elite-room chests are excluded by the mod."""
    display_name = "Chest Checks"
    range_start = 0
    range_end = 150
    default = 30


class RandomizeNPCs(Toggle):
    """Require Archipelago items to use NPC services. When disabled, NPC services remain available normally."""
    display_name = "Randomize NPC Services"
    default = 1


class RoomChecks(Toggle):
    """Include per-difficulty, stage, and room-clear checks. Room layout assumptions must match the mod."""
    display_name = "Room Clear Checks"
    default = 1


class EliteBossChecks(Toggle):
    """Include unique elite and boss kill checks per difficulty. Exact encounter keys must match the mod."""
    display_name = "Elite and Boss Checks"
    default = 1


class NPCUnlockChecks(Toggle):
    """Include story NPC unlock locations independently of NPC service item randomization."""
    display_name = "NPC Unlock Checks"
    default = 1


class StartingRelics(Choice):
    """Initial relic pool. 'basic_four' uses relic IDs 3, 18, 73, and 901; 'none' uses fallback only. The C# mod must consume this setting."""
    display_name = "Starting Relics"
    option_basic_four = 0
    option_none = 1
    default = 0


@dataclass
class MagicraftOptions(PerGameCommonOptions):
    goal: Goal
    chest_checks: ChestChecks
    randomize_npcs: RandomizeNPCs
    room_checks: RoomChecks
    elite_boss_checks: EliteBossChecks
    npc_unlock_checks: NPCUnlockChecks
    starting_relics: StartingRelics
