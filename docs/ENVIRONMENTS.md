# Environments

Maps are layouts (`Core/Maps`) and environments are themes laid over them (`Game/Env`). A layout picks
its theme with `MapDef.theme`; any layout can wear any theme, so new layouts for new modes get art for
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
  continuous texture and every edge catches a line of light.
- `SkyPainter`: paints a 360° panorama (gradient, clouds, a ringed planet, a moon) for Unity's
  `Skybox/Panoramic` shader. Without that shader the camera clears to the horizon colour.
- `MapDresser`: puts it together by block kind (never by block name): materials and meshes on the
  blocks, trims (base and cap courses, glowing strips, stripes, container posts, crate bands), vines,
  floor patches and zone paving, then cliffs, trees and gantries outside the play area, and the light.

## Fair-play rules for dressing

Art must never change how a map plays, so the validator's numbers stay true:

- A block keeps its transform and collider; only its mesh and material change.
- Nothing the dresser adds has a collider.
- Inside the play area, added pieces lie flat on the floor (at most 2 cm), sit flush on a block (at most
  `MapDresser.MaxProud`, 7 cm, proud of it) or hang high overhead (gantries at 12 m).
- Tall pieces (cliffs, trees, gantry legs) stand outside the outer walls.

## Sources and licences

Everything here is made in code in this repository: no third-party models, textures or sounds, so
there is nothing to license. Free libraries we may use later must be CC0 or allow commercial use
(Kenney, Quaternius, Poly Haven, ambientCG), with the licence file beside them; record each one here.
Never use assets ripped from other games.

| Asset | Source | Licence |
|---|---|---|
| All environment textures, meshes and skies | Made in code (`Game/Env`) | Ours |

Concept renders (Blender, made in code too) are in the project files under `art/env-jungle-moon/`.

## Themes

| Id | Name | Status | Mood |
|---|---|---|---|
| `jungle_moon` | Jungle moon | On Gantry Yard | A freight yard on a jungle moon: sandstone walls with lime trim lights, ochre dirt with moss, magenta, teal, mustard and violet containers, yellow overhead gantries, purple cliffs, giant capped trees with glowing pink fruit, a teal-to-gold sky with a ringed lilac planet. |
| `sunforge_quarry` | Sunforge quarry | Proposed (data only) | Carved red sandstone at golden hour, brass machinery, turquoise glow, a dusky violet sky with a small ringed moon. No giant trees. |
| `coral_reactor` | Coral reactor | Proposed (data only) | A reactor station on a tidal world grown over by dry coral: bone-white concrete, coral pinks and reds, sea-green trims, coral-fan trees, a bright aqua sky with a huge pale planet. |

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
