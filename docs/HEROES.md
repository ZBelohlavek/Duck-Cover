# Starter heroes

Generated from the hero and ability data in `Assets/DuckAndCover/Data/Resources`. The data files are
the source of truth; numbers here are the ones the tests check. All names and kits are original.

Every hero has three abilities, all usable from level 1: nothing is locked behind a level. The third (X) may run on
ultimate charge, which builds from the start of the match, or on a plain cooldown.
Levels run 1 to 20 with a pick every other level: traits at 2, 6 and 14, talents at 4, 12, 16 and 18, an upgrade to
the third ability at 8 (one of two; it is usable before and without it), the passive at 10 (heroes start without one)
and a capstone at 20. Each odd level gives a small automatic boost instead: +2% max health and +2% damage.
Keys: E, F and X abilities, hold Z or V for the left or right upgrade.

## Bulwark (vanguard)

Health 300. Weapons: Shotgun and pistol.

- **E (hold): Rampart.** Hold E to raise a wide shield in front of you; let go to lower it. It opens out from your arm to 5 m across over a third of a second, moves and turns with you, and stops enemy shots and abilities while your team shoots through it (it doesn't stop feet). It holds 900 damage and keeps what is left between raises, regaining 180 a second once it has been down for 1.5 seconds. If it breaks, it needs 5 seconds before it can go up again. While it is up you move 35% slower and can't fire. A stun or going down drops it.
- **F: Patch Drone.** Launch a repair drone that hovers behind your shoulder for 10 seconds and heals you 20 a second. Enemies can shoot it down (100 health); your team's shots pass it. Cooldown 14 s.
- **X (ultimate): Bulkhead.** Drop a 9 m wide, 3.5 m high wall where you aim, up to 20 m away, for 8 seconds (2000 health). Nobody walks through it and enemy shots stop on it, but your team shoots through: cover for your team, or a cut-off route for the enemy.

Left (Z) is the **Guardian** path: a stronger, wider, faster Rampart that sends damage back. Right (V) is the **Brawler** path: up close, with a better Patch Drone, bigger and more frequent Bulkheads and a shotgun that fires faster and knocks back enemies at point-blank range.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Plating**: +25 max health. | **Close Quarters**: Your weapons deal 6% more damage. |
| 4 talent | **Dense Field**: Rampart holds 20% more damage and regains its strength 30% faster. | **Spare Parts**: Patch Drone heals 30% more. |
| 6 trait | **Anchor**: Take 8% less damage on a point your team holds, or beside your own minions. | **Quick Build**: Bulkhead charges 15% faster. |
| 8 ultimate | **Reinforced Bulkhead**: Bulkhead holds twice as much damage and stands 4 seconds longer. | **Wide Bulkhead**: Bulkhead is 4 m wider and 1 m taller. |
| 10 passive | **Braced**: Take 15% less damage while standing on a point, or beside your own minions. | **Second Wind**: Regain 8 health a second when you haven't been hit for a while. |
| 12 talent | **Deflector**: Rampart sends 25% of the damage it blocks back at whoever dealt it. | **Armoured Drone**: Patch Drone has twice the health and stays 4 seconds longer. |
| 14 trait | **Steady Guard**: Abilities recover 15% faster, so a broken Rampart comes back sooner. | **Pump Action**: Your shotgun fires 10% faster. |
| 16 talent | **Broad Rampart**: Rampart opens 1.5 m wider and stands 0.4 m taller. | **Shield Drone**: Patch Drone also gives you 6 shield every half second. |
| 18 talent | **Mobile Rampart**: Move at full speed while Rampart is up. | **Field Repairs**: Patch Drone also heals teammates within 5 m of it. |
| 20 capstone | **Mirror Rampart**: Rampart sends back 35% more of the damage it blocks, and regains its strength twice as fast. | **Point Blank**: Your shotgun deals 15% more damage, and knocks back enemies within 4 m. |

## Lancer (striker)

Health 200. Weapons: Rifle and pistol.

- **E: Slip.** Dash 6 m along your aim. Cooldown 6 s.
- **F: Tracer Mark.** Fire a tracer round at the enemy you aim at: 25 damage, and for 5 seconds they take 15% more damage and show through walls to your team. Cooldown 10 s.
- **X (ultimate): Incendiary Grenade.** Throw a grenade where you aim: 40 damage to enemies within 3 m, and it leaves a 4 m fire for 5 seconds that burns enemies standing in it for 20 damage a second.

Left (Z) is the **gun path**, built around dominating whoever Tracer Mark hits: harder, longer-reaching shots and life steal. Right (V) is the **control path**: fire that keeps burning, slows and knockbacks for enemies who get too close.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Aim**: Your weapons deal 5% more damage. | **Lingering Heat**: Your burns and slows last 15% longer. |
| 4 talent | **Lingering Mark**: Tracer Mark lasts 3 seconds longer. | **Pinning Mark**: Tracer Mark also slows the target by 30% for 2 seconds. |
| 6 trait | **Extended Mag**: Your magazines hold 25% more rounds. | **Quick Fuse**: Your ultimate charges 15% faster. |
| 8 ultimate | **Thermite**: Incendiary Grenade hits much harder where it lands (120 damage), but its fire is smaller. | **Clinging Fire**: Incendiary Grenade's fire reaches 1 m further, and enemies keep burning for 3 seconds after they leave it. |
| 10 passive | **Bloodthirst**: Heal for 10% of the damage your weapons deal. | **Suppressing Fire**: Your bullets slow enemies by 15% for a second. |
| 12 talent | **Hunter's Mark**: Marked enemies take 25% more damage instead of 15%. | **Repulse**: Slip blasts enemies within 4 m of where you start 4 m away and slows them by 40% for 2 seconds. |
| 14 trait | **Long Barrel**: Your weapons deal 8% more damage and keep full damage 30% further out. | **Stopping Power**: Your bullets push enemies back slightly. |
| 16 talent | **Twin Marks**: Tracer Mark holds two charges and recovers 30% sooner. | **Firewalk**: Slip leaves a 2.5 m fire where you started that burns enemies for 3 seconds. |
| 18 talent | **Blood Mark**: Tracer Mark deals 40 more damage and heals you 40. | **Sticky Fire**: Enemies in Incendiary Grenade's fire are slowed by 25%. |
| 20 capstone | **Apex Predator**: Your weapons deal 12% more damage and you heal for 10% more of it. | **Inferno**: Incendiary Grenade's fire reaches 1.5 m further, lasts 3 seconds longer and burns 30% harder. |

## Mender (support)

Health 200. Weapons: Mending sidearm (heals teammates it hits, 14 a shot).

- **E: Mending Field.** Throw down a field where you aim. For 6 seconds it heals you and teammates within 4 m by 20 every second. Cooldown 12 s.
- **F: Purge.** Clear slows, stuns and marks from you and teammates within 6 m, and heal them 30. Cooldown 10 s.
- **X (ultimate): Aegis Pulse.** Shield yourself and every teammate within 12 m for 100 for 5 seconds.

Left (Z) is the **Combat Medic** path: the healing sidearm and keeping herself alive. Right (V) is the **Field Healer** path: Mending Field and Purge for the whole team.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Dose**: Your sidearm hits 10% harder: more healing and more damage. | **Wide Reach**: Mending Field and Purge reach 10% further. |
| 4 talent | **Spare Vials**: Your magazine holds 20% more rounds. | **Lasting Field**: Mending Field lasts 2 seconds longer. |
| 6 trait | **Fast Reload**: Reload 20% faster. | **Efficient**: Abilities recover 10% faster. |
| 8 ultimate | **Long Aegis**: Aegis Pulse shields last 4 seconds longer. | **Broad Aegis**: Aegis Pulse reaches 6 m further. |
| 10 passive | **Field Kit**: Regain 6 health a second when you haven't been hit for a while. | **Charged**: Your ultimate charges 20% faster. |
| 12 talent | **Triage**: Healing teammates at half health or less is 30% stronger. | **Quickening Purge**: Purge also speeds everyone it reaches up by 25% for 3 seconds. |
| 14 trait | **Battle Medic**: Your sidearm hits 15% harder: more healing and more damage. | **Healing Hands**: Your healing is 10% stronger. |
| 16 talent | **Second Opinion**: Your sidearm's heals also clear slows, stuns and marks. | **Rich Field**: Mending Field heals 10 more each second. |
| 18 talent | **Steady Hands**: Heal yourself for 10% of the damage your sidearm deals. | **Twin Fields**: Mending Field holds two charges. |
| 20 capstone | **Lifeline**: Your healing and shields are 20% stronger. | **Sanctuary**: Mending Field also gives everyone inside it 15 shield a second. |

## Rigger (controller)

Health 200. Weapons: SMG and pistol. A stealth gunner: he slips in unseen, opens up on one target, and shuts the enemy's best
player down.

- **E: Cloak.** Turn invisible to enemies for 5 seconds. Enemy turrets, minions and Bastions ignore you, and enemies within 5 m see only a shimmer. Firing or using an aimed ability ends it. Cooldown 14 s.
- **F: Combat Stim.** Fire 25% faster and move 25% faster for 5 seconds. It doesn't end Cloak. Cooldown 14 s.
- **X: Hush Grenade.** Throw a grenade where you aim: 15 damage to enemies within 3 m, and they are silenced (can't use abilities, can still shoot) for 2.5 seconds. Cooldown 16 s.

Attacking out of Cloak opens an **ambush** window of 2.5 seconds (`Ruleset.ambushSeconds`), and the shot or grenade
that breaks the cloak already counts. On its own it does nothing: the Assassin path's upgrades hit harder in it.
A reveal (Tracer Mark, Tracking Hush) beats a cloak while it lasts.

Left (Z) is the **Assassin** path: a stronger cloak, and a burst on one target when he comes out of it. Right (V) is
the **Shutdown** path: longer, wider silences that weaken, expose and track the enemy carry.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Hollow Points**: Your weapons deal 5% more damage. | **Long Hush**: Everything you put on a target lasts 15% longer: silences, Hamper, and your own Cloak and Stim. |
| 4 talent | **Ambush**: For 2.5 seconds after you fire or throw out of Cloak, your weapons deal 20% more damage. | **Wide Hush**: Hush Grenade reaches 1 m further. |
| 6 trait | **Drum Mag**: Your magazines hold 25% more rounds. | **Handy**: Abilities recover 10% faster. |
| 8 third ability | **Sharp Hush**: Hush Grenade deals 25 more damage and recovers 4 seconds sooner, to open an ambush. | **Deep Hush**: Hush Grenade silences for 1 second longer. |
| 10 passive | **Light Feet**: Move 6% faster, and another 10% faster while cloaked. | **Padded Vest**: +25 max health. |
| 12 talent | **Killing Window**: Ambush lasts 1.5 seconds longer, and your weapons deal 15% more damage during it. | **Hamper**: Enemies Hush Grenade hits deal 20% less damage and heal 25% less for 5 seconds. |
| 14 trait | **Close Work**: Your weapons deal 8% more damage. | **Wide Reach**: Your abilities reach 15% further. |
| 16 talent | **Twin Fades**: Cloak lasts 2 seconds longer and holds two charges. | **Tracking Hush**: Enemies Hush Grenade hits show through walls to your team for 4 seconds, cloaked ones too. |
| 18 talent | **Smoke Stim**: Combat Stim also cloaks you for 2 seconds. | **Double Hush**: Hush Grenade holds two charges. |
| 20 capstone | **Phantom**: During Ambush your weapons deal 25% more damage and heal you for 15% of the damage they deal. | **Blackout**: Enemies Hush Grenade hits take 15% more damage while they are silenced, and the silence lasts 1 second longer. |

## Against minion waves (WP20)

Every ability and weapon hits minions, Bastions and camp guards as it hits heroes. Each hero's job against a wave,
as `Tests/CoreSim/Tests/WaveKitTests.cs` checks it:

- **Bulwark** protects: Rampart stops minion fire at him and the teammates behind it, and Bulkhead stops a wave's fire
  and feet (`UnitRules.LineOfFire`, `UnitRules.Walk`) while his team shoots through. His shotgun clears a wave from close up.
- **Lancer** clears: Incendiary Grenade catches a whole wave; Thermite destroys Scrappers outright.
- **Mender** protects her own wave: Mending Field heals minions, Purge frees them from slows, Aegis Pulse shields them.
- **Rigger** slips past a lane: minions and Bastions don't shoot him while he is cloaked, so he can get behind a wave
  or reach the enemy's heroes. Against the wave itself he is the SMG, sped up by Combat Stim, with Hush Grenade for the
  bunched front.

Turrets shoot the nearest enemy they can see, so a minion in front of one comes before a hero at the edge of its reach.
Upgrades that work "on a point" (Anchor, Braced) also work within 8 m of your own minions or Bastion
(`Ruleset.escortRadius`). Damage and healing on units give half the ultimate charge they give on heroes
(`Ruleset.ultimateChargeUnitShare`), so clearing waves helps ultimates without replacing fights.
`dotnet run --project Tests/CoreSim -- report kitwaves` shows how long each hero takes to clear one wave alone.
