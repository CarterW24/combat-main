# Combat + Frostfang Fury (WIP)

This branch adds a working **combat system** and the beginnings of the **Frostfang Fury**
adventure encounter to the Sanctuary Free Realms server emulator. It is a work-in-progress
research branch — combat was **reverse-engineered from the retail client** (packet formats
recovered via IDA + live testing against an unmodified client), so players connect with a
stock client and the server just sends animation/effect/ability ids that already exist in it.

## What works

- **Item-driven ability toolbar.** The ability bar is built from the equipped weapon, the way
  Free Realms did it. Each Ninja "Shadow Blade of X" grants a melee technique + a named special.
- **Full combat feedback loop.** Pressing an ability plays the swing animation, shows the floating
  damage number, drains the target's health bar, plays the per-ability hit effect, and applies recoil.
- **Ninja kit** with real decoded animation clips + per-ability composite effects, including
  **Shadow Army** (spawns temporary chasing ghost-ninja clones).
- **Frostfang Fury adventure offer popup** — clicking the Frostfang Growler wolf opens the real
  blue adventure panel (title / difficulty / description / prizes / **GO!**), driven entirely by
  server packets.
- **GO! → enter** the encounter: a real cross-world zone into the `sg_random_encounter_clearing`
  arena (identified from the client's own world data), then a wolf-pack fight (Frostfang Snarlers →
  Frostfang Alpha).

## Key packets reverse-engineered (new/rebuilt in this branch)

- `BaseCombatPacket / CombatPacketAttackProcessed` (op32/sub7) — damage number + health bar + hit fx + recoil.
- `BaseAbilityPacket` — `AbilityPacketStartCasting` (sub3), `AbilityPacketSetDefinition` (sub5, toolbar).
- `BaseEncounterPacket` family — `EncounterDetailsResponsePacket` (sub114, the offer popup),
  `EncounterZoneIsReadyPacket` (sub107, the ready handshake), `EncounterParticipantRequestEntrance`
  (sub108, GO!), `EncounterOverworldCombatPacket` (sub132), `EncounterPacketIsFighting` (sub133).
- `BaseMiniGamePacket` (op39) — `MiniGameGameStartPacket` (sub17), the loading-screen handshake.
- `BasePlayerUpdatePacket` — `HitPointModification`, `UpdateHitpoints`, `NpcRelevance`, `PlayCompositeEffect`.

## Notes

- Everything is server-authoritative; the client is never modified.
- Forked from [Open-Source-Free-Realms/Sanctuary](https://github.com/Open-Source-Free-Realms/Sanctuary).
- WIP: cooldowns/energy gating, the other five combat jobs, and the full encounter objective/prize
  flow are still in progress.
