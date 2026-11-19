# Environments

Maps are layouts (`Core/Maps`) and environments are themes laid over them (`Game/Env`). A layout picks
its theme with `MapDef.theme`, and can dress parts of itself in another theme with `MapDef.artRegions`
(footprints in x and z; the floor and outer walls always keep the map's theme); any layout can wear any theme, so new layouts for new modes get art for
free. `MapScene.Greybox = true` shows the bare layout again.

The look we aim for: bold, saturated palettes, chunky shapes with soft worn edges, painted (matte, not
glossy) surfaces, big readable silhouettes and dramatic skies. Heroes must stand out, so a theme keeps
the team colours (saturated blue and orange) off anything big.

## How it works

- `EnvTheme`: one theme is just data: surface colours, plants, sky, sun, ambient light and haze, and
  switches for the dressing (giant trees, overhead gantries, vines).
- `PaintedTextures`: tileable painted textures made in code from the theme's colours (stone courses,
  rock slabs, dirt, ribbed metal, planks, concrete, deck plate, shutter slats, bark, leaves, paving).
- `EnvMesh.ChamferBox`: blocks get chamfered edges and world-scale UVs, so neighbours share one
  continuous texture and every edge catches a line of light. `EnvMesh.LumpyBox` draws natural walls
  (tree lines, rocks, cliffs): every corner of a face grid is pushed inward only, so the drawing never
  leaves the collider, and faces pressed against a neighbouring block stay flat so there is never a
  slit to see through between two blocks.
- `SkyPainter`: paints a 2048 x 1024 panorama for Unity's `Skybox/Panoramic` shader, laid out the way
  that shader reads it so the sun's glow sits where the sun light comes from: a graded sky with horizon
  haze and a sun halo, a ringed planet with an atmosphere glow and a moon, a cloud deck in perspective
  lit from the sun's side, high streaks, cloud heaps on the horizon, and two painted mountain ranges
  (theme colours `haze`, `mountainFar`, `mountainNear`, `canopyFar`, `cloudCover`, `planetGlow`). Painted
  once per theme (about a second) and cached. Without that shader the camera clears to the horizon colour.
- `MapDresser`: puts it together by block kind (never by block name): materials and meshes on the
  blocks, trims (base and cap courses, glowing strips, stripes, container posts, crate bands), vines,
  floor patches, flowers and zone paving, then cliffs, trees and gantries outside the play area, a
  backdrop of rolling jungle hills and far mesas fading into the fog, the sun with a soft fill light
  from the other side, and the fog.

## Lanes and jungle

Lane maps read like a road through a forest: a lane of packed dirt, walled in by tree lines and rocks,
with gaps where trails lead off into the jungle.

- `MapDef.paths` (art only): `ArtPath` lines painted flat on the ground. `"lane"` is wide packed dirt
  with darker verges; `"trail"` is a narrow mossy path that starts with a leafy arch overhead (4.6 m up),
  marking a jungle entrance.
- `MapBlock.style` (art only): `"treeline"` draws a wall block as a dense hedge with bushes inside its
  box and now and then a tree rising out of it; `"rocks"` draws it as an outcrop with a boulder and moss.
  The block keeps its box collider. Anything that frames a lane must be a real block, because in a
  shooter what looks like a wall has to stop players and shots.
- Gaps between styled blocks are the jungle entrances: put a trail through each one.
- Each side's jungle is one through-route: a trail leaves the lane near one end, loops out through
  the jungle and rejoins the lane near the other end, with camp clearings along it.
- `LaneStudy` (`Core/Maps`) is a mock-up of this, not a playable map: a winding lane with a jungle loop
  on each side (four entrances). The capture builds it beside the real map (shots 13 to 15), and
  `dotnet run --project Tests/CoreSim -- report lanestudy out.svg` draws its plan.

## Fair-play rules for dressing

Art must never change how a map plays, so the validator's numbers stay true:

- A block keeps its transform and collider; only its mesh and material change.
- Nothing the dresser adds has a collider.
- Inside the play area, added pieces lie flat on the floor (at most 2 cm), sit flush on a block (at most
  `MapDresser.MaxProud`, 7 cm, proud of it) or hang high overhead (gantries at 12 m).
- Tall pieces (cliffs, trees, gantry legs) stand outside the outer walls, or grow out of a styled block
  (a tree line's trees), above its top.
- Modelled pieces (`EnvArt`) are stretched to their block's box (`EnvArt.Fit`), so the drawing never
  leaves the collider. Only a tree's crown may spread past its block, and only above 9 m.

## Sources and licences

Everything here is made in code in this repository: no third-party models, textures or sounds, so
there is nothing to license. Free libraries we may use later must be CC0 or allow commercial use
(Kenney, Quaternius, Poly Haven, ambientCG), with the licence file beside them; record each one here.
Never use assets ripped from other games.

| Asset | Source | Licence |
|---|---|---|
| All environment textures, meshes and skies | Made in code (`Game/Env`) | Ours |
| Breakwater's modelled pieces (`Data/Resources/EnvArt`) | Made in Blender from our own scripts | Ours |

Concept renders (Blender, made in code too) are in the project files under `art/env-jungle-moon/`.

## Modelled pieces (Breakwater)

Breakwater, the Overrun lane map, draws its blocks with modelled pieces instead of code-made shapes:
tree-line hedges (some with a tree), rock walls, low walls, rock cover, crates and crate stacks, the
temple side gates and broken temple wall, both teams' Forward and Core Bastions with a fallen stump,
launch pads and the four camp pads. They carry the Bastions' design: teal crystal clusters, brass caps
and Bastion stone.

- Each piece is `Data/Resources/EnvArt/<name>.bytes` (a small binary mesh, format DCM2, read by
  `Game/Env/EnvArt.cs`), `<name>_c.jpg` (painted colour, baked) and, for lightened pieces, `<name>_n.jpg`
  (normal map). Binary meshes and JPEGs need no Git LFS. `Editor/EnvArtImport.cs` sets the texture import
  settings.
- Glowing parts (trim lights, crystals) are separate parts with a flat glowing colour. A Bastion's
  crown crystal (`crystal_a`/`crystal_b`) is recoloured by `MapView` to show if it is sealed or open.
- `MapDresser.DressArt` picks the piece for each block by kind and style (`treeline`, `rocks`,
  `templegate`, `templeruin`); interior blocks walled in on all four sides keep the code-made look so the
  map stays light. If the pieces are missing, every block falls back to the code-made look.
- The sources are in the project files under `art/env-assets-v1/`: `build.py` makes the Blender models,
  `unity_export.py` bakes and exports them, `to_dcm2.py` writes the game files. To regenerate:
  `pip install bpy`, then `python3 unity_export.py <out>` and `python3 to_dcm2.py <out>
  Assets/DuckAndCover/Data/Resources/EnvArt`.

## Themes

| Id | Name | Status | Mood |
|---|---|---|---|
| `jungle_moon` | Jungle moon | Gantry Yard's main look | A freight yard overgrown by a jungle moon: mossy violet stone with lime trim lights, green earth thick with moss, magenta, teal, lime and violet containers, yellow overhead gantries, purple cliffs, giant capped trees with glowing pink fruit, a teal-to-gold sky with a ringed lilac planet. |
| `jungle_temple` | Jungle temple | Gantry Yard's heart (both halls and the middle) | A temple quarter in the jungle: pale rose sandstone with plum courses, carved glyph bands, lime trim lights and a flagstone courtyard. Kept pale so orange-team heroes stand out against it. |
| `sunforge_quarry` | Sunforge quarry | Proposed (data only) | Carved red sandstone at golden hour, brass machinery, turquoise glow, a dusky violet sky with a small ringed moon. No giant trees. |
| `coral_reactor` | Coral reactor | Proposed (data only) | A reactor station on a tidal world grown over by dry coral: bone-white concrete, coral pinks, reds and magentas (no orange, it's a team colour), sea-green trims, coral-fan trees, a bright aqua sky with a huge pale planet. |

More ideas for later layouts, each needing a few new prop builders:

- **Frostglass Relay**: a mountain relay station in violet ice and white stone, aurora sky, amber lights
  (keep the ice off saturated blue).
- **Ember Gardens**: terraced volcanic gardens with black basalt, red-leaved trees, glowing lava channels
  well outside the play area, a smoky gold sky.
- **Sky Dock**: floating platforms over a cloud sea with a sunset sky, wooden decks, brass cranes and
  balloons; the drop is outside the walls.

## Next steps

- Bloom on the glowing trims (needs a URP volume, so the Game assembly would reference URP).
- Real models for hero props (crane cabs, cargo nets, signage, lamps) from CC0 libraries or Blender, and
  per-theme prop sets for the new mode layouts.
- Baked lighting or light probes once layouts settle.
