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

Left (Z) is the **Juggernaut** path: shotgun, Breach Rush and staying alive up front. Right (V) is the **Warden** path: Bastion Panel and holding ground for the team.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Hide**: +25 max health. | **Anchor**: Take 8% less damage on a point your team holds. |
| 4 talent | **Long Rush**: Breach Rush goes 2 m further and knocks enemies 2 m further. | **Reinforced Panel**: Bastion Panel has 30% more health. |
| 6 trait | **Close Quarters**: Your weapons deal 6% more damage. | **Steady Guard**: Abilities recover 10% faster. |
| 8 ultimate | **Crushing Line**: Hold the Line wears enemies down twice as fast. | **Lockdown**: Enemies caught when Hold the Line goes up are rooted for 1.5 seconds. |
| 10 passive | **Second Wind**: Regain 8 health a second when you haven't been hit for a while. | **Braced**: Take 15% less damage while standing on a point. |
| 12 talent | **Shockwave**: Breach Rush stuns the enemies you hit for 0.6 seconds. | **Bastion Aura**: Teammates within 4 m of where you plant Bastion Panel gain 15 shield a second while it stands. |
| 14 trait | **Last Stand**: Take 15% less damage while at half health or less. | **Point Warden**: Your weapons deal 12% more damage while on a point. |
| 16 talent | **Double Rush**: Breach Rush holds two charges. | **Wide Panel**: Bastion Panel is 1.5 m wider. |
| 18 talent | **Rallying Rush**: Breach Rush gives you a 60 shield for 3 seconds. | **Twin Panels**: Bastion Panel holds two charges, and two can stand at once. |
| 20 capstone | **Unbreakable**: +75 max health and 5 more health regained a second. | **Fortress**: Hold the Line also gives teammates inside it 20 shield a second. |

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
