# Starter heroes

Generated from the hero and ability data in `Assets/DuckAndCover/Data/Resources`. The data files are
the source of truth; numbers here are the ones the tests check. All names and kits are original.

Levels run 1 to 20 with a pick every other level: traits at 2, 6 and 14, talents at 4, 12, 16 and 18, the ultimate
unlocks at 8 once you pick one of its two forms, the passive at 10 (heroes start without one) and a capstone at 20.
Each odd level gives a small automatic boost instead: +2% max health and +2% damage. Keys: E and F abilities,
X ultimate, hold Z or V for the left or right upgrade.

## Bulwark (vanguard)

Health 300. Weapons: Shotgun and pistol.

- **E: Bastion Panel.** Plant a 3 m wide armoured panel where you aim. It stops shots and bodies until it breaks or runs out. Teammates shoot through it. Cooldown 14 s.
- **F: Breach Rush.** Charge 7 m along your aim. Enemies where you land take damage and are knocked back. Cooldown 9 s.
- **X (ultimate): Hold the Line.** Lock down the ground around you for 6 seconds: enemies inside are slowed and worn down, and you gain a 100 shield.

Not reworked yet: its old passive is now one of the level 10 choices, and level 18 is a placeholder pair.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Hide**: +50 max health. | **Anchor**: Take 10% less damage on a point your team holds. |
| 4 talent | **Reinforced Panel**: The panel has 50% more health. | **Double Rush**: Breach Rush holds two charges. |
| 6 trait | **Close Quarters**: Your weapons deal 10% more damage. | **Quick Load**: Reload 20% faster. |
| 8 ultimate | **Iron Ring**: Hold the Line covers 3 m more ground. | **Lockdown**: Enemies caught when Hold the Line goes up are rooted for 1.5 seconds. |
| 10 passive | **Braced**: Take 15% less damage while standing on a point. | **Second Wind**: Regain 8 health a second when you haven't been hit for a while. |
| 12 talent | **Shockwave**: Breach Rush stuns the enemies you hit for 0.6 seconds. | **Rapid Panel**: Bastion Panel recovers 30% sooner. |
| 14 trait | **Relentless**: Abilities recover 15% faster. | **Point Warden**: Your weapons deal 15% more damage while on a point. |
| 16 talent | **Long Rush**: Breach Rush goes 3 m further and knocks enemies 3 m further. | **Rallying Rush**: Breach Rush gives you a 60 shield for 3 seconds. |
| 18 talent | **Quick Panel**: Bastion Panel recovers 20% sooner. | **Quick Rush**: Breach Rush recovers 20% sooner. |
| 20 capstone | **Unbreakable**: +75 max health and 5 more health regained a second. | **Siege Breaker**: Your weapons deal 15% more damage and Breach Rush recovers 25% sooner. |

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

Not reworked yet: its old passive is now one of the level 10 choices, and level 18 is a placeholder pair.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Bedside Manner**: Your healing is 10% stronger. | **Nimble**: Move 6% faster. |
| 4 talent | **Wide Field**: Mending Field reaches 1.5 m further. | **Lasting Field**: Mending Field lasts 3 seconds longer. |
| 6 trait | **Fast Reload**: Reload 20% faster. | **Sturdy**: +25 max health. |
| 8 ultimate | **Long Aegis**: Aegis Pulse shields last 4 seconds longer. | **Broad Aegis**: Aegis Pulse reaches 6 m further. |
| 10 passive | **Field Kit**: Regain 6 health a second when you haven't been hit for a while. | **Charged**: Your ultimate charges 20% faster. |
| 12 talent | **Quickening Purge**: Purge also speeds everyone it reaches up by 25% for 3 seconds. | **Deep Purge**: Purge heals 40 more. |
| 14 trait | **Steadfast**: Take 15% less damage while on a point. | **Survivor**: Regain 8 more health a second when you haven't been hit for a while. |
| 16 talent | **Rich Field**: Mending Field heals 10 more each second. | **Rapid Purge**: Purge recovers 30% sooner. |
| 18 talent | **Quick Field**: Mending Field recovers 20% sooner. | **Quick Purge**: Purge recovers 20% sooner. |
| 20 capstone | **Lifeline**: Your healing and shields are 20% stronger. | **Battle Medic**: Your weapons deal 15% more damage and you take 10% less. |

## Rigger (controller)

Health 200. Weapons: SMG and pistol.

- **E: Snare Mine.** Place a mine where you aim (two charges, two out at once). It arms after a second, then the first enemy within 3 m sets it off: 30 damage and slowed by half for 3 seconds. Cooldown 9 s.
- **F: Sentry.** Set down a small sentry gun that shoots the nearest enemy it can see within 14 m for 25 seconds. Cooldown 18 s.
- **X (ultimate): Lockout Gate.** Raise an 8 m wide, 4 m high gate where you aim that closes the route for 6 seconds. Your team can shoot through it.

Not reworked yet: its old passive is now one of the level 10 choices, and level 18 is a placeholder pair.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Spray**: Your weapons deal 8% more damage. | **Light Feet**: Move 6% faster. |
| 4 talent | **Spare Mines**: Snare Mine holds three charges, and three can be out at once. | **Shock Mine**: Snare Mine also stuns for 0.75 seconds. |
| 6 trait | **Quick Reload**: Reload 25% faster. | **Padded Vest**: +25 max health. |
| 8 ultimate | **Long Lockout**: Lockout Gate stands 4 seconds longer. | **Snaring Gate**: Enemies within 4 m of the gate when it rises are slowed by 60% for 3 seconds. |
| 10 passive | **Tinkerer**: Abilities recover 10% faster. | **Wide Reach**: Your mines, sentries and areas reach 15% further. |
| 12 talent | **Twin Sentry**: Two sentries can be out at once. | **Hardened Sentry**: Sentries have 75% more health. |
| 14 trait | **Dug In**: Take 15% less damage on a point your team holds. | **Charged**: Your ultimate charges 20% faster. |
| 16 talent | **Heavy Rounds**: Sentry shots deal 4 more damage. | **Wide Mines**: Snare Mines go off for enemies 1.5 m further away. |
| 18 talent | **Quick Mine**: Snare Mine recovers 20% sooner. | **Quick Sentry**: Sentry recovers 20% sooner. |
| 20 capstone | **Overclock**: Abilities recover 20% faster and sentries fire 20% faster. | **Fortifier**: Your mines, sentries and areas reach 20% further, and +25 max health. |
