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

Left (Z) is the **Guardian** path: a stronger, wider, faster Rampart that sends damage back. Right (V) is the **Brawler** path: up close, with a better Patch Drone, bigger and more frequent Bulkheads and a shotgun that slows and knocks back.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Plating**: +25 max health. | **Close Quarters**: Your weapons deal 6% more damage. |
| 4 talent | **Dense Field**: Rampart holds 20% more damage and regains its strength 30% faster. | **Spare Parts**: Patch Drone heals 30% more. |
| 6 trait | **Anchor**: Take 8% less damage on a point your team holds, or beside your own minions. | **Quick Build**: Bulkhead charges 15% faster. |
| 8 ultimate | **Reinforced Bulkhead**: Bulkhead holds twice as much damage and stands 4 seconds longer. | **Wide Bulkhead**: Bulkhead is 4 m wider and 1 m taller. |
| 10 passive | **Braced**: Take 15% less damage while standing on a point, or beside your own minions. | **Second Wind**: Regain 8 health a second when you haven't been hit for a while. |
| 12 talent | **Deflector**: Rampart sends 25% of the damage it blocks back at whoever dealt it. | **Armoured Drone**: Patch Drone has twice the health and stays 4 seconds longer. |
| 14 trait | **Steady Guard**: Abilities recover 15% faster, so a broken Rampart comes back sooner. | **Rattling Spread**: Your shotgun slows the enemies it hits by 15% for a second. |
| 16 talent | **Broad Rampart**: Rampart opens 1.5 m wider and stands 0.4 m taller. | **Shield Drone**: Patch Drone also gives you 6 shield every half second. |
| 18 talent | **Mobile Rampart**: Move at full speed while Rampart is up. | **Field Repairs**: Patch Drone also heals teammates within 5 m of it. |
| 20 capstone | **Mirror Rampart**: Rampart sends back 35% more of the damage it blocks, and regains its strength twice as fast. | **Point Blank**: Your shotgun deals 15% more damage and knocks enemies back. |

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

Health 200. Weapon: Mending sidearm (15 damage a shot). It heals only while loaded with Mending Rounds.

- **E: Mending Canister.** Throw a canister where you aim. For 6 seconds, you and teammates within 3.5 m of it regenerate 16 health a second, and keep regenerating for a second after stepping out. Cooldown 12 s.
- **F: Purge.** Clear every harmful effect (slows, stuns, silences, marks, damage and healing cuts) from you and teammates within 6 m. Cooldown 12 s.
- **X: Mending Rounds.** Reload at once with a magazine of healing rounds: each one that hits a teammate heals them 16 (shots stop on teammates instead of passing through). Enemies still take normal damage. A normal reload or emptying the magazine swaps back. Cooldown 6 s.

Left (Z) is the **single-target** path: the gun and Mending Rounds, ending in healing rounds that ricochet from the teammate hit to the nearest hurt teammate within 6 m (`Ruleset.healRicochetRange`, 60% of the heal: `healRicochetShare`). Right (V) is the **area** path: the canister and Purge, with a canister that latches onto a teammate and follows them, and a Purge that wards everyone it reaches so the next enemy debuff on them is blocked.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Aim**: Your gun hits 8% harder, and Mending Rounds heal 8% more. | **Wide Reach**: Mending Canister and Purge reach 10% further. |
| 4 talent | **Quick Load**: Mending Rounds recover 1.5 seconds sooner. | **Lasting Canister**: Mending Canister lasts 2 seconds longer. |
| 6 trait | **Spare Vials**: Your magazine holds 20% more rounds, healing rounds too. | **Healing Hands**: Your healing is 10% stronger. |
| 8 ultimate | **Potent Rounds**: Mending Rounds heal 25% more. | **Splash Rounds**: A Mending Round also heals teammates within 2.5 m of the one it hits, for half. |
| 10 passive | **Field Kit**: Regain 6 health a second when you haven't been hit for a while. | **Steady Supply**: Abilities recover 10% faster. |
| 12 talent | **Triage**: Healing teammates at half health or less is 30% stronger. | **Tethered Canister**: Mending Canister latches onto the teammate nearest where it lands (or you) and follows them. |
| 14 trait | **Long Barrel**: Your shots keep full strength (damage and healing) 30% further out. | **Lingering Care**: Your regen and wards last 25% longer. |
| 16 talent | **Second Opinion**: Your healing rounds also clear harmful effects from the teammates they heal. | **Warding Purge**: Purge also wards everyone it reaches for 8 seconds: the next harmful effect an enemy puts on them is blocked. |
| 18 talent | **Ricochet**: A healing round that heals a teammate jumps to the nearest hurt teammate within 6 m and heals them 60% as much. | **Twin Canisters**: Mending Canister holds two charges. |
| 20 capstone | **Lifeline**: Ricochets jump twice, and your healing is 15% stronger. | **Overflow**: Mending Canister reaches 1.5 m further and its regen is 50% stronger. |

## Rigger (controller)

Health 200. Weapons: SMG and pistol.

- **E: Snare Mine.** Place a mine where you aim (two charges, two out at once). It arms after a second, then the first enemy within 3 m sets it off: 30 damage and slowed by half for 3 seconds. Cooldown 9 s.
- **F: Sentry.** Set down a small sentry gun that shoots the nearest enemy it can see within 14 m for 25 seconds. Cooldown 18 s.
- **X (ultimate): Lockout Gate.** Raise an 8 m wide, 4 m high gate where you aim that closes the route for 6 seconds. Your team can shoot through it.

Left (Z) is the **Engineer** path: Sentry and the SMG. Right (V) is the **Trapper** path: Snare Mines and slows.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Spray**: Your weapons deal 5% more damage. | **Long Fuse**: Your slows and stuns last 15% longer. |
| 4 talent | **Long Service**: Sentries last 10 seconds longer. | **Hair Trigger**: Snare Mines arm half a second sooner. |
| 6 trait | **Drum Mag**: Your magazines hold 25% more rounds. | **Handy**: Abilities recover 10% faster. |
| 8 ultimate | **Long Lockout**: Lockout Gate stands 4 seconds longer. | **Snaring Gate**: Enemies within 4 m of the gate when it rises are slowed by 60% for 3 seconds. |
| 10 passive | **Tinkerer**: Abilities recover 10% faster. | **Light Feet**: Move 6% faster. |
| 12 talent | **Hardened Sentry**: Sentries have 75% more health. | **Shock Mine**: Snare Mines also stun for 0.75 seconds. |
| 14 trait | **Padded Vest**: +25 max health. | **Wide Reach**: Your mines, sentries and areas reach 15% further. |
| 16 talent | **Overclock**: Sentries fire 20% faster and their shots deal 2 more damage. | **Spare Mines**: Snare Mine holds three charges, and three can be out at once. |
| 18 talent | **Twin Sentry**: Two sentries can be out at once, and Sentry holds two charges. | **Sticky Mines**: Enemies who set off a Snare Mine show through walls to your team for 5 seconds. |
| 20 capstone | **Fortified Sentry**: Sentries reach 30% further and have 50% more health. | **Minefield**: Snare Mines go off for enemies 1.5 m further away, and their slow lasts 3 seconds longer. |

## Against minion waves (WP20)

Every ability and weapon hits minions, Bastions and camp guards as it hits heroes. Each hero's job against a wave,
as `Tests/CoreSim/Tests/WaveKitTests.cs` checks it:

- **Bulwark** protects: Rampart stops minion fire at him and the teammates behind it, and Bulkhead stops a wave's fire
  and feet (`UnitRules.LineOfFire`, `UnitRules.Walk`) while his team shoots through. His shotgun clears a wave from close up.
- **Lancer** clears: Incendiary Grenade catches a whole wave; Thermite destroys Scrappers outright.
- **Mender** protects his own wave: Mending Canister regenerates minions, Purge frees them from slows, and Mending Rounds heal the minion he shoots.
- **Rigger** holds a lane: a Sentry left alone takes apart most of a wave, Snare Mine slows the front of a wave that
  walks onto it, and Lockout Gate holds a wave and its fire while Rigger's own minions shoot out through it.

Sentries shoot the nearest enemy they can see, so a minion in front of one comes before a hero at the edge of its reach.
Upgrades that work "on a point" (Anchor, Braced) also work within 8 m of your own minions or Bastion
(`Ruleset.escortRadius`). Damage and healing on units give half the ultimate charge they give on heroes
(`Ruleset.ultimateChargeUnitShare`), so clearing waves helps ultimates without replacing fights.
`dotnet run --project Tests/CoreSim -- report kitwaves` shows how long each hero takes to clear one wave alone.
