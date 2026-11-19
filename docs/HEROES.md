# Heroes

Generated from the hero and ability data in `Assets/DuckAndCover/Data/Resources`. The data files are
the source of truth; numbers here are the ones the tests check. All names and kits are original.

Every hero has three abilities, all usable from level 1: nothing is locked behind a level. There are no ultimates:
every ability runs on a plain cooldown.

Ability rules (Zach, Nov 19 2026), checked for every hero by `AbilityRulesTests` (`report abilities` prints the table):
- Cooldowns sit between 13 and 20 seconds (`Ruleset.abilityCooldownFloor`/`Ceiling`). Cooldown-rate upgrades can't take
  an ability under 13 s. An ability may sit lower only with a written reason (`AbilityDef.shortCooldownReason`): today
  Rampart (a held shield; 5 s is the wait after it breaks) and Mending Rounds (the Mender's gun healing, 6 s).
- No single ability kills a full-health hero: one cast, with its damage over time and the caster's upgrades, deals at
  most 75% of the lowest max health in the roster to one enemy (150 of 200).
- Area damage is lower than single-target damage: an ability that hits several enemies deals each at most 50% of the
  lowest max health (100), counting 3 seconds in any lingering area; an area weapon's damage a second is below every
  single-target hero weapon's.
Levels run 1 to 20 with a pick every other level: traits at 2, 6 and 14, talents at 4, 12, 16 and 18, an upgrade to
the third ability at 8 (one of two; it is usable before and without it), the passive at 10 (heroes start without one)
and a capstone at 20. Each odd level gives a small automatic boost instead: +2% max health and +2% damage.
Keys: E, F and X abilities, hold Z or V for the left or right upgrade.

## Bulwark (vanguard)

Health 300. Weapons: Shotgun and pistol.

- **E (hold): Rampart.** Hold E to raise a wide shield in front of you; let go to lower it. It opens out from your arm to 5 m across over a third of a second, moves and turns with you, and stops enemy shots and abilities while your team shoots through it (it doesn't stop feet). It holds 900 damage and keeps what is left between raises, regaining 180 a second once it has been down for 1.5 seconds. If it breaks, it needs 5 seconds before it can go up again. While it is up you move 35% slower and can't fire. A stun or going down drops it.
- **F: Patch Drone.** Launch a repair drone that hovers behind your shoulder for 10 seconds and heals you 20 a second. Enemies can shoot it down (100 health); your team's shots pass it. Cooldown 14 s.
- **X: Bulkhead.** Drop a 9 m wide, 3.5 m high wall where you aim, up to 20 m away, for 8 seconds (2000 health). Nobody walks through it and enemy shots stop on it, but your team shoots through: cover for your team, or a cut-off route for the enemy. Cooldown 20 s.

Left (Z) is the **Guardian** path: a stronger, wider, faster Rampart that sends damage back. Right (V) is the **Brawler** path: up close, with a better Patch Drone, bigger and more frequent Bulkheads and a shotgun that fires faster and knocks back enemies at point-blank range.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Plating**: +25 max health. | **Close Quarters**: Your weapons deal 6% more damage. |
| 4 talent | **Dense Field**: Rampart holds 20% more damage and regains its strength 30% faster. | **Spare Parts**: Patch Drone heals 30% more. |
| 6 trait | **Anchor**: Take 8% less damage on a point your team holds, or beside your own minions. | **Quick Build**: Your abilities recharge 10% faster (never quicker than 13 seconds). |
| 8 ultimate | **Reinforced Bulkhead**: Bulkhead holds twice as much damage and stands 4 seconds longer. | **Wide Bulkhead**: Bulkhead is 4 m wider and 1 m taller. |
| 10 passive | **Braced**: Take 15% less damage while standing on a point, or beside your own minions. | **Second Wind**: Regain 8 health a second when you haven't been hit for a while. |
| 12 talent | **Deflector**: Rampart sends 25% of the damage it blocks back at whoever dealt it. | **Armoured Drone**: Patch Drone has twice the health and stays 4 seconds longer. |
| 14 trait | **Steady Guard**: Abilities recover 15% faster, so a broken Rampart comes back sooner. | **Pump Action**: Your shotgun fires 10% faster. |
| 16 talent | **Broad Rampart**: Rampart opens 1.5 m wider and stands 0.4 m taller. | **Shield Drone**: Patch Drone also gives you 6 shield every half second. |
| 18 talent | **Mobile Rampart**: Move at full speed while Rampart is up. | **Field Repairs**: Patch Drone also heals teammates within 5 m of it. |
| 20 capstone | **Mirror Rampart**: Rampart sends back 35% more of the damage it blocks, and regains its strength twice as fast. | **Point Blank**: Your shotgun deals 15% more damage, and knocks back enemies within 4 m. |

## Lancer (striker)

Health 200. Weapons: Rifle and pistol.

- **E: Slip.** Dash 6 m along your aim. Cooldown 13 s.
- **F: Tracer Mark.** Fire a tracer round at the enemy you aim at: 35 damage, and for 5 seconds they take 15% more damage and show through walls to your team. Cooldown 14 s.
- **X: Incendiary Grenade.** Throw a grenade where you aim: 25 damage to enemies within 3 m, and it leaves a 4 m fire for 5 seconds that burns enemies standing in it for 12 damage a second. Cooldown 18 s.

Left (Z) is the **gun path**, built around dominating whoever Tracer Mark hits: harder, longer-reaching shots and life steal. Right (V) is the **control path**: fire that keeps burning, slows and knockbacks for enemies who get too close.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Steady Aim**: Your weapons deal 5% more damage. | **Lingering Heat**: Your burns and slows last 15% longer. |
| 4 talent | **Lingering Mark**: Tracer Mark lasts 3 seconds longer. | **Pinning Mark**: Tracer Mark also slows the target by 30% for 2 seconds. |
| 6 trait | **Extended Mag**: Your magazines hold 25% more rounds. | **Quick Fuse**: Your abilities recharge 10% faster (never quicker than 13 seconds). |
| 8 ultimate | **Thermite**: Incendiary Grenade hits harder where it lands (55 damage), but its fire is smaller and burns 25% less. | **Clinging Fire**: Incendiary Grenade's fire reaches 1 m further, and enemies keep burning for 3 seconds after they leave it. |
| 10 passive | **Bloodthirst**: Heal for 10% of the damage your weapons deal. | **Suppressing Fire**: Your bullets slow enemies by 15% for a second. |
| 12 talent | **Hunter's Mark**: Marked enemies take 25% more damage instead of 15%. | **Repulse**: Slip blasts enemies within 4 m of where you start 4 m away and slows them by 40% for 2 seconds. |
| 14 trait | **Long Barrel**: Your weapons deal 8% more damage and keep full damage 30% further out. | **Stopping Power**: Your bullets push enemies back slightly. |
| 16 talent | **Twin Marks**: Tracer Mark holds two charges. | **Firewalk**: Slip leaves a 2.5 m fire where you started that burns enemies for 3 seconds. |
| 18 talent | **Blood Mark**: Tracer Mark deals 40 more damage and heals you 40. | **Sticky Fire**: Enemies in Incendiary Grenade's fire are slowed by 25%. |
| 20 capstone | **Apex Predator**: Your weapons deal 12% more damage and you heal for 10% more of it. | **Inferno**: Incendiary Grenade's fire reaches 1.5 m further and lasts 3 seconds longer. |

## Mender (support)

Health 200. Weapon: Mending sidearm (15 damage a shot). It heals only while loaded with Mending Rounds.

- **E: Mending Canister.** Throw a canister where you aim. For 6 seconds, you and teammates within 3.5 m of it regenerate 18 health a second, and keep regenerating for a second after stepping out. Cooldown 14 s.
- **F: Purge.** Clear every harmful effect (slows, stuns, silences, marks, damage and healing cuts) from you and teammates within 6 m. Cooldown 16 s.
- **X: Mending Rounds.** Reload at once with a magazine of healing rounds: each one that hits a teammate heals them 16 (shots stop on teammates instead of passing through). Healing rounds do no harm to enemies, so reload to fight. A normal reload or emptying the magazine swaps back. Cooldown 6 s.

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

Health 200. Weapons: SMG and pistol. A stealth gunner: he slips in unseen, opens up on one target, and shuts the enemy's best
player down.

- **E: Cloak.** Turn invisible to enemies for 5 seconds. Enemy turrets, minions and Bastions ignore you, and enemies within 5 m see only a shimmer. Firing or using an aimed ability ends it. Cooldown 14 s.
- **F: Combat Stim.** Fire 25% faster and move 25% faster for 5 seconds. It doesn't end Cloak. Cooldown 14 s.
- **X: Hush Grenade.** Throw a grenade where you aim. The one enemy nearest where it lands, within 3 m, takes 15 damage and is silenced (can't use abilities, can still shoot) for 2.5 seconds. It never hits more than one. Cooldown 16 s.

Attacking out of Cloak opens an **ambush** window of 2.5 seconds (`Ruleset.ambushSeconds`), and the shot or grenade
that breaks the cloak already counts. On its own it does nothing: the Assassin path's upgrades hit harder in it.
A reveal (Tracer Mark, Tracking Hush) beats a cloak while it lasts.

Left (Z) is the **Assassin** path: a stronger cloak, and a burst on one target when he comes out of it. Right (V) is
the **Shutdown** path: longer silences on one enemy at a time that weaken, expose and track the enemy carry.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Hollow Points**: Your weapons deal 5% more damage. | **Long Hush**: Everything you put on a target lasts 15% longer: silences, Hamper, and your own Cloak and Stim. |
| 4 talent | **Ambush**: For 2.5 seconds after you fire or throw out of Cloak, your weapons deal 20% more damage. | **Quick Hush**: Hush Grenade recovers 3 seconds sooner. |
| 6 trait | **Drum Mag**: Your magazines hold 25% more rounds. | **Handy**: Abilities recover 10% faster. |
| 8 third ability | **Sharp Hush**: Hush Grenade deals 25 more damage, to open an ambush. | **Deep Hush**: Hush Grenade silences for 1 second longer. |
| 10 passive | **Light Feet**: Move 6% faster, and another 10% faster while cloaked. | **Padded Vest**: +25 max health. |
| 12 talent | **Killing Window**: Ambush lasts 1.5 seconds longer, and your weapons deal 15% more damage during it. | **Hamper**: The enemy Hush Grenade hits deals 20% less damage and heals 25% less for 5 seconds. |
| 14 trait | **Close Work**: Your weapons deal 8% more damage. | **Wide Reach**: Your abilities reach 15% further (Hush Grenade still hits one enemy). |
| 16 talent | **Twin Fades**: Cloak lasts 2 seconds longer and holds two charges. | **Tracking Hush**: The enemy Hush Grenade hits shows through walls to your team for 4 seconds, even if cloaked. |
| 18 talent | **Smoke Stim**: Combat Stim also cloaks you for 2 seconds. | **Double Hush**: Hush Grenade holds two charges. |
| 20 capstone | **Phantom**: During Ambush your weapons deal 25% more damage and heal you for 15% of the damage they deal. | **Blackout**: The enemy Hush Grenade hits takes 15% more damage while silenced, and the silence lasts 1 second longer. |

## Gashrak, the war chief (striker)

Health 275. Weapon: Greataxe, melee only (no gun). A bruiser who closes the gap with War Leap and holds enemies in place
while his axe works. Gashrak is a working name.

- **Left mouse: Greataxe.** A swing hits every enemy (heroes and minions) within 3.2 m in a 100 degree arc in front of
  him for 60, once every 0.75 s. No ammo and no reload. Walls and enemy shields stop it; a shield or drone straight ahead
  takes the hit instead.
- **E: War Leap.** Leap up to 12 m to where you aim (onto the floor below it, so aiming at a wall or the sky still lands).
  Where he lands, 0.8 s later, enemies within 3.5 m take 30 damage and are knocked up: thrown into the air and stunned for
  0.75 seconds. Cooldown 14 s.
- **F: Axe Throw.** Throw a hand axe up to 25 m: the first enemy hit takes 50 damage and is stunned for 1.25 seconds.
  Cooldown 15 s.
- **X: Rage.** For 6 seconds, regenerate 15 health a second and deal 20% more damage. Cooldown 20 s.

Stuns and knock-ups use the built-in stun: Purge clears them, a ward blocks them, and `Ruleset.maxStunSeconds` caps them.
A silence (Hush Grenade) stops his abilities but not the axe, unless he has Unbroken.

Left (Z) is the **Bruiser** path: in and hitting, with a harder, longer Rage that heals him through the axe and a War
Leap that lands harder and wider. Right (V) is the **Warlord** path: longer stuns from Axe Throw and War Leap, two axes,
a sturdier body and Rage regeneration, and Unbroken, which lets Rage shake off a stun or silence and clears every
harmful effect.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Heavy Hands**: Your axe deals 6% more damage. | **Thick Hide**: +25 max health. |
| 4 talent | **Blood Up**: Rage gives 30% more damage instead of 20%. | **Heavy Haft**: Axe Throw stuns for 0.4 seconds longer. |
| 6 trait | **Long Reach**: Your swings reach 15% further (about half a metre). | **Warlord's Pace**: Abilities recover 10% faster. |
| 8 third ability | **Long Rage**: Rage lasts 2 seconds longer. | **Second Skin**: Rage regenerates 50% more health. |
| 10 passive | **Battle Hunger**: Heal for 8% of the damage your axe deals. | **Hard to Kill**: Take 10% less damage while at half health or less. |
| 12 talent | **Crushing Landing**: War Leap's landing deals 30 more damage. | **Ground Shaker**: War Leap's knock-up stuns for 0.4 seconds longer. |
| 14 trait | **Quick Swings**: You swing 10% faster. | **Iron Grip**: Your stuns, knock-ups and Rage last 15% longer. |
| 16 talent | **Blood Rage**: While Rage is up, your axe also heals you for 15% of the damage it deals. | **Unbroken**: Rage can be used while stunned or silenced, and clears every harmful effect on you when it starts. |
| 18 talent | **Wide Landing**: War Leap's landing reaches 1.5 m further. | **Twin Axes**: Axe Throw holds two charges. |
| 20 capstone | **Berserker**: Rage gives 20% more damage on top, and you swing 20% faster while it is up. | **Warlord**: Your stuns, knock-ups and Rage last 25% longer, and Rage lasts 2 seconds longer. |

## Skulk (striker)

Health 200. Weapon: Scrap Rifle, his only gun (automatic, 22 damage a round, 20 rounds). A goblin jungler: fast in the
jungle, fed by camp kills, and finishing enemies up close with a knife that gets sharper with every kill.

- **E: Shrapnel Charge.** Toss a charge up to 8 m ahead. After 0.6 seconds it bursts, dealing 40 damage to each enemy within 3 m. Cooldown 14 s.
- **Passive (in F's place): Jungle Stalker.** Move 15% faster in the jungle. Finishing off a camp creature (any way) heals you 35 and gives you Spoils: 10% more damage for 15 seconds.
- **X: Serrated Knife.** Slash the nearest enemy in a 110° arc within 2.5 m in front of you for 45 damage (one target: area damage stays lower per target than single-target damage). Every enemy the knife kills (hero, minion or camp creature, not a Bastion) adds a notch: 3 more knife damage, up to 10 notches, kept for the whole match, through deaths. Cooldown 13 s.

Skulk is the one hero with a passive in his base kit (Zach asked for it), so he has no F ability: E and X are his
abilities and the HUD shows the passive in F's slot. **In the jungle** (`StatConditions.Jungle`): on a map with one
(Breakwater), on a jungle trail or within 9 m of a camp pad, and never on the lane (`Core/Maps/JungleArea`,
`Ruleset.jungleTrailMargin`, `jungleClearingRadius`); on a map without one (Gantry Yard), anywhere off a live point.
Kill rewards are data (`AbilityDef.onKill`, `KillRewardDef`): who counts (hero, minion, camp), and whether the kill must
be by that ability, by anything else, or either. No single use of his abilities kills a full-health 200 hero, at any
level down either path (`SkulkKitTests.NoOneUseKillsAFullHealthHero`).

Left (Z) is the **Jungle** path: faster, tougher and quicker to recover in the jungle, bigger camp rewards, a knife
that heals him on kills and holds more notches, and two Shrapnel Charges. Right (V) is the **Headhunter** path: more gun
damage, hero kills by anything notch the knife and pay Jungle Stalker's rewards, a bleeding knife, Shrapnel that makes
enemies take more damage, and notches worth more.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Trailwise**: In the jungle you move another 5% faster. | **Sharp Sights**: Your weapons deal 5% more damage. |
| 4 talent | **Big Game**: Jungle Stalker's kill rewards heal you 25 more. | **Head Count**: A hero you kill with anything else (your rifle, Shrapnel Charge) also adds a notch to Serrated Knife. |
| 6 trait | **Thick Hide**: Take 8% less damage in the jungle. | **Scrap Mag**: Your magazine holds 25% more rounds. |
| 8 third ability | **Field Dressing**: Every kill with Serrated Knife heals you 30. | **Bloodletter**: Serrated Knife makes enemies bleed for 6 damage a second for 4 seconds. |
| 10 passive | **Lurker**: In the jungle, regain 8 health a second when you haven't been hit for a while. | **Restless**: Move 7% faster everywhere. |
| 12 talent | **Rich Pickings**: Spoils lasts 10 seconds longer and adds another 10% damage. | **Trophy Hunter**: Killing a hero also gives you Jungle Stalker's rewards: the heal and Spoils. |
| 14 trait | **Undergrowth**: In the jungle your abilities recover 20% faster. | **Long Barrel**: Your weapons deal 8% more damage and keep full damage 30% further out. |
| 16 talent | **Trophy Rack**: Serrated Knife holds 5 more notches. | **Shredder**: Enemies Shrapnel Charge hits take 15% more damage for 4 seconds. |
| 18 talent | **Satchel**: Shrapnel Charge holds two charges. | **Keen Edge**: Each Serrated Knife notch adds 2 more damage. |
| 20 capstone | **Jungle King**: In the jungle you deal 15% more damage and move another 10% faster. | **Headhunter**: Hero kills give Serrated Knife two notches instead of one. |

## Bolete (support)

Health 200. Weapon: Spore bombs. A fungal healer: he lobs spore bombs, grows mushrooms that heal his team, rots the
enemy and grows a frenzy mushroom out of a teammate.

- **Primary: Spore bombs.** Lob a spotted spore bomb in an arc (it falls, so aim above far targets). It bursts where it
  lands: 10 damage on a direct hit plus a 25 damage burst that reaches 2.5 m (down to 40% at the edge). Six bombs a
  load, one every 0.7 s, 2 s to reload. Bombs never hurt or heal teammates. Because a burst can catch several enemies,
  one bomb does less damage a second than Mender's sidearm does to one target: at most 50 a second on direct hits
  (34 counting reloads) against the sidearm's 94 (63).
- **E: Bloom Cap.** Plant a healing mushroom where you aim. For 6 seconds, you and teammates within 3.5 m of it heal 18
  health a second, and keep healing for a second after stepping out. Cooldown 14 s.
- **F: Rot Spore.** Throw a rotten spore pod where you aim. It bursts a second later: enemies within 3.5 m are poisoned
  for 7 damage a second and Wilt (deal 20% less damage) for 5 seconds. Both are harmful effects, so a cleanse (Purge)
  clears them (35 damage in all, so it never kills a healthy hero alone). Cooldown 15 s.
- **X: Frenzy Cap.** Grow a frenzy mushroom on the teammate you aim at (within 1.5 m of where your aim
  lands, up to 30 m): for 6 seconds they fire 25% faster and deal 15% more damage. With no teammate there it is
  refused and spends nothing (`TargetingDef.needsTarget`). Cooldown 20 s.

Left (Z) is the **Overgrowth** path: everything grows bigger, lasts longer and spreads. Right (V) is the **Symbiosis**
path: Bolete gains from his own fungus, healed when he heals, feeding on his poison and sharing his Frenzy.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Wide Spores**: Bloom Cap, Rot Spore and Frenzy Cap reach 10% further. | **Hardy Stalk**: +25 max health. |
| 4 talent | **Lasting Bloom**: Bloom Cap lasts 2 seconds longer. | **Quick Spores**: Rot Spore recovers 2 seconds sooner. |
| 6 trait | **Lingering Rot**: Everything your fungus puts on someone lasts 25% longer: poison, Wilt, Frenzy and Bloom Cap's healing. | **Potent Bombs**: Your spore bombs deal 8% more damage. |
| 8 ultimate | **Wild Frenzy**: Frenzy Cap also makes its teammate move 20% faster. | **Kindred Frenzy**: Frenzy Cap also grows on you. |
| 10 passive | **Fertile Ground**: Your healing is 10% stronger. | **Shared Roots**: Whenever your healing heals a teammate, you heal 30% as much. |
| 12 talent | **Clinging Spores**: Anyone who steps into Bloom Cap carries its healing with them for 4 seconds after they leave. | **Leeching Rot**: Each enemy hero Rot Spore poisons gives you 6% more damage and 5% more speed for 6 seconds (up to 3 at once). |
| 14 trait | **Big Bombs**: Your spore bombs burst 30% wider. | **Steady Growth**: Abilities recover 10% faster. |
| 16 talent | **Creeping Rot**: Rot Spore's poison lasts 3 seconds longer and its cloud reaches 1 m further. | **Heart of the Bloom**: Take 15% less damage while you stand in your own Bloom Cap. |
| 18 talent | **Shared Frenzy**: Frenzy Cap also grows on every teammate within 4 m of the one you aim at. | **Feeding Rot**: Your poison heals you for half the damage it deals. |
| 20 capstone | **Great Bloom**: Bloom Cap reaches 2 m further and heals 30% more. | **Mycelial Link**: Shared Roots gives 60% instead of 30%, and Frenzy Cap lasts 3 seconds longer. |

The rules behind the Symbiosis path are stats any hero could use: `healShare` (Shared Roots; healing teammate heroes,
not minions, and never its own echo), `statusLifeSteal` (Feeding Rot; damage over time that heals its source), an
effect reaching `selfPerHero` (Leeching Rot; once per enemy hero caught, never minions) and a "self" effect on an
object's pulse, which reaches the owner only while they stand in it (Heart of the Bloom). Big Bombs is the
`splashRadius` stat. Bots and tests aim lobbed weapons on their arc with `WeaponDef.AimAt`.
## Nocturne, the vampire lord (striker)

Health 250. Weapon: Blood Darts, thrown from his hands (no gun). A brawler who drains what he hits: he grabs a foe,
steals their health and sweeps through a crowd to heal. Nocturne is a working name.

- **Left mouse: Blood Darts.** Each chop of a hand throws one red dart: 28 damage, four throws a second. Darts fly fast
  (75 m/s) and straight, so they can be seen and dodged, and a dart counts where it strikes: 1.75x to the head. No ammo
  and no reload: the throwing rhythm is the only limit.
- **E: Grasping Claw.** A huge claw lunges up to 6 m where you aim and seizes the enemy nearest that spot (within 1.5 m):
  45 damage, a 2 m pull toward him, and he steals 30 of their max health for 30 seconds (they lose it; he gains it, and
  the health with it). Each grab refreshes the steal and adds to it, up to 3 times (90). A miss steals nothing. The
  drain is a harmful effect, so a cleanse (Mender's Purge) gives it back. Cooldown 14 s.
- **F: Bat Swarm.** Burst into a cloud of bats and fly 7 m along your aim. For half a second he takes no damage and
  enemies can't put harmful effects on him. Cooldown 13 s.
- **X: Crimson Sweep.** Rake the claws through everyone within 5 m in a 120 degree arc in front of him: 50 damage each,
  and he heals for 40% of the damage dealt. Cooldown 16 s.

No ability kills a full-health hero on its own. The only kill is Executioner (right path), which finishes off
enemies already at 20% health or less (30% with Deathmark). Units (minions, Bastions, camp guards) are never executed.
"Crits" are head hits: the game has no random crits, and the right path makes head hits much stronger (the
`headDamage` stat).

Left (Z) is the **Blood Tyrant** path: a life-stealing tank who gets in and siphons health to keep going, ending in a
draining aura when the sweep hits a crowd. Right (V) is the **Nightstalker** path: elusive with Bat Swarm, big head hits
with the darts, and an execute on the grab.

| Level | Left (hold Z) | Right (hold V) |
|---|---|---|
| 2 trait | **Thick Blood**: +25 max health. | **Keen Eye**: Your head hits deal 10% more damage. |
| 4 talent | **Deep Siphon**: Crimson Sweep heals you for 60% of its damage instead of 40%. | **Long Flight**: Bat Swarm flies 3 m further. |
| 6 trait | **Thirst**: Heal for 8% of the damage your darts deal. | **Quick Hands**: You throw darts 8% faster. |
| 8 ultimate | **Wide Sweep**: Crimson Sweep reaches 1.5 m further and sweeps 180 degrees instead of 120. | **Quick Sweep**: Crimson Sweep recovers 3 seconds sooner. |
| 10 passive | **Blood Bank**: Healing you receive is 10% stronger. | **Night Runner**: Move 8% faster. |
| 12 talent | **Greedy Grasp**: Grasping Claw steals 45 max health a grab instead of 30. | **Twin Flight**: Bat Swarm holds two charges. |
| 14 trait | **Iron Veins**: +8% max health. | **Killing Eye**: Your head hits deal 25% more damage. |
| 16 talent | **Blood Aura**: When Crimson Sweep hits 3 or more enemies, you wear a draining aura for 5 seconds: enemies within 4 m lose 10 health a second, and you gain what they lose. | **Executioner**: Grasping Claw finishes off an enemy hero left at 20% health or less. |
| 18 talent | **Hungry Aura**: Blood Aura needs only 2 enemies hit and lasts 2 seconds longer. | **Out of the Dark**: For 3 seconds after Bat Swarm, your darts deal 30% more damage. |
| 20 capstone | **Blood Tyrant**: Heal for 10% more of the damage your darts deal, and Blood Aura reaches 1.5 m further. | **Deathmark**: Your head hits deal 20% more damage, and Executioner finishes off enemies at 30% health instead of 20%. |

## Against minion waves (WP20)

Every ability and weapon hits minions, Bastions and camp guards as it hits heroes. Each hero's job against a wave,
as `Tests/CoreSim/Tests/WaveKitTests.cs` checks it:

- **Bulwark** protects: Rampart stops minion fire at him and the teammates behind it, and Bulkhead stops a wave's fire
  and feet (`UnitRules.LineOfFire`, `UnitRules.Walk`) while his team shoots through. His shotgun clears a wave from close up.
- **Lancer** clears: Incendiary Grenade catches a whole wave; Thermite destroys Scrappers outright.
- **Mender** protects his own wave: Mending Canister regenerates minions, Purge frees them from slows, and Mending Rounds heal the minion he shoots.
- **Bolete** clears with his bombs' bursts, which catch a clump of minions, while Rot Spore wilts a wave's fire and
  Bloom Cap heals his own minions.
- **Gashrak** cleaves: every swing hits every minion in front of him, so he clears a wave fastest of all from up
  close, but has to walk into it to do so.
- **Skulk** clears with the Scrap Rifle and Shrapnel Charge, and every minion his knife finishes notches it for the match.
- **Gashrak** cleaves: every swing hits every minion in front of him, so he clears a wave fastest of all from up
  close, but has to walk into it to do so.
- **Rigger** slips past a lane: minions and Bastions don't shoot him while he is cloaked, so he can get behind a wave
  or reach the enemy's heroes. Against the wave itself he is the SMG, sped up by Combat Stim.

Turrets shoot the nearest enemy they can see, so a minion in front of one comes before a hero at the edge of its reach.
Upgrades that work "on a point" (Anchor, Braced) also work within 8 m of your own minions or Bastion
(`Ruleset.escortRadius`).
`dotnet run --project Tests/CoreSim -- report kitwaves` shows how long each hero takes to clear one wave alone.
