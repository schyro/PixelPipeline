# Pixel Pipeline — Technical reference

How each system works, what each setting changes, and where the limits are. For installation and
first steps, see the [README](../README.md).

Settings are listed in the order of the pages in **Tools > Pixel Pipeline > Settings Window**.
Paths such as `Materials/MAT_Grass` are relative to the package folder.

- [Package layout](#package-layout)
- [Renderer setup](#renderer-setup)
- [Resolution](#resolution)
- [Outline](#outline)
- [Lighting](#lighting)
- [Wind](#wind)
- [Day & Night](#day--night)
- [Weather](#weather)
- [Clouds](#clouds)
- [God Rays](#god-rays)
- [Wind Streaks](#wind-streaks)
- [Rain](#rain)
- [Water](#water)
- [Grass](#grass)
- [Tree foliage](#tree-foliage)
- [Pixel-perfect camera](#pixel-perfect-camera)
- [Demo scene](#demo-scene)

## Package layout

- `Runtime` — the `PixelPipeline.Runtime` assembly: renderer feature, settings, and scene components.
- `Editor` — the `PixelPipeline.Editor` assembly: settings window and renderer setup.
- `Shaders`, `Materials`, `Textures` — shared by both. Materials installed from a git URL or a
  registry are read-only; duplicate one into `Assets` to change it.

The package depends on `com.unity.render-pipelines.universal` and `com.unity.inputsystem`. It can
also be installed by copying the `com.schyro.pixel-pipeline` folder into a project's `Packages`
folder.

## Renderer setup

Every renderer that carries `PixelRendererFeature` reads the `PixelPipelineSettings` asset assigned
to it. The asset is project data, so it lives in your `Assets` folder; the package only ships the
shaders, textures, and default materials it points at.

Run `Tools > Pixel Pipeline > Configure Renderers` after adding the feature to a new renderer. It:

- assigns the shader references a player build needs,
- adds URP's **Screen Space Shadows** feature, which the pixel shadows depend on (see
  [Pixel shadows](#pixel-shadows)),
- raises the shadow map resolution of every pipeline asset that uses the pixel renderer to at
  least 2048.

URP asset requirements:

- **Rendering Path**: **Deferred+** for banded lighting (see [Banded lighting](#banded-lighting)).
- **Main light shadows**: hard.
- **Opaque Texture** on with **Opaque Downsampling** at None, for water refraction.

### Settings window

The window edits the settings asset selected in its toolbar; it picks the first one in the project
when it opens. Most pages open with a preview of what they control: the Game view's pixel scale, a
smooth and a banded sphere with the banded shadow edge, a wind compass, the whole day's sky and
light colours, each weather state's lighting, the live cloud shadow drawn by the in-game shader, a
streak's shape, and the water's depth colours and shore profile. Edits have full undo and repaint
the Scene and Game views as they happen.

In the asset's Inspector, wind, day/night, weather, clouds, god rays, wind streaks, and rain are
grouped under **Environment Systems**, and water under **Water**.

## Resolution

- **Internal Width** / **Internal Height** — the pixel resolution the scene is drawn at. The
  default height is 540.
- **Match Camera Aspect** — derives the width from the active camera, avoiding non-square pixels
  on displays that are not 16:9.
- **Render Scene View** — applies the pixel passes to the Scene view as well.
- **Base Camera Only** — skips overlay cameras.

The scene is drawn straight at the internal resolution: the feature gives the camera a URP render
scale for the length of its own render, so the depth prepass, shadows, opaque copy, water, and URP
post-processing all shade only the pixels that end up on screen. The sharp upscale then writes a
screen-sized image that takes over as the camera colour.

Where that is not possible, the scene is drawn at screen resolution and point sampled down instead:

- in the Scene view,
- for cameras with a camera stack,
- with **Match Camera Aspect** off,
- on screens where the scale would fall outside URP's 0.1 to 2 range.

The pipeline asset's own **Render Scale** and **Upscaling Filter** are put back after every camera,
so their saved values do not change.

## Outline

Outlines come from the opaque depth and normals. There are two kinds of line: a dark **silhouette**
line where depth jumps, and a bright **crease** line on inner edges where the normal turns.

- **Enable Outline** — turns the pass on or off.
- **Line Darken** — the silhouette line's colour, as a multiplier on the colour next to it. Lower
  is darker.
- **Line Alpha** — opacity of the silhouette line.
- **Crease Brighten** — how much brighter than the surface a crease line is.
- **Crease Alpha** — opacity of the crease line.
- **Kernel Radius** — distance to the neighbouring samples, in internal pixels. Larger values give
  thicker lines.
- **Depth Threshold** — depth difference that counts as a silhouette.
- **Grazing Angle Start** / **Grazing Depth Scale** — raise the depth threshold on surfaces seen
  at a grazing angle, which keeps flat floors from filling with lines. The first is where the
  correction starts, the second how strong it gets.
- **Crease Depth Low** / **High** and **Crease Normal Low** / **High** — the depth and normal
  difference ranges over which a crease line fades in.

Grass writes an outline-exclusion marker into DepthNormals, so it can hide geometry behind it
without gaining an outline of its own. Foliage does the same with **Exclude From Outline** on its
material. Water is not outlined by the edge detection; its shore foam outlines it instead.

## Lighting

### Banded lighting

A smooth N·L ramp across a curved surface reads as a gradient of chunky pixels rather than as pixel
art. On a **Deferred+** or **Deferred** renderer the feature lights every G-buffer pixel itself and
snaps the direct light to flat bands, so a surface turns from lit to unlit in clean steps whose
edges are one pixel wide.

- **Enable Light Bands** — off gives smooth Lambert lighting.
- **Light Bands** — brightness steps between unlit and fully lit.
- **Light Band Bias** — moves the thresholds. Positive values grow the brighter bands, negative
  values the darker ones.

Point and spot lights are banded over their own reach instead of URP's inverse-square falloff: the
pool steps down through the bands to a hard outer ring. The light's **Range** is where that ring
sits and its **Intensity** is how bright the pool is, each on its own. An intensity of 1 lights a
surface under the light as the sun at intensity 1 would. Grass, foliage, and the grass terrain take
the same lights, each blade and leaf card as one tone.

The ambient light is snapped to three zones (facing the ground, the horizon, or the sky), so
floors, walls, and ceilings keep exactly their own ambient colour.

No material has to change. Anything that writes a G-buffer is lit this way: URP Lit, Simple Lit,
Complex Lit, and Shader Graph Lit materials, with their own textures, normal maps, emission, and
environment reflections. The pass runs between URP's G-buffer and deferred lighting passes
(`Shaders/Lighting/PixelDeferredLighting.shader`): it adds the banded lights and the difference
between banded and smooth ambient to the camera colour, then clears the pixel's material type in
the stencil buffer, so URP's own smooth lighting skips it.

Limits:

- **Deferred+** is the path to use. Plain **Deferred** works too, but it tries every visible light
  on every pixel, and URP's GPU Resident Drawer does not support it.
- On **Forward** and **Forward+** renderers there is no G-buffer, so lit materials keep URP's
  smooth lighting. Pixel shadows, outlines, and everything else still work there.
- The banded light is diffuse only: no specular highlights. Environment reflections and emission
  come through from the material.
- Transparent materials are drawn forward by URP and are not banded.
- Baked lightmaps are not accounted for in the ambient zones; the look is meant for realtime light.
- A flat surface changes band abruptly as the sun moves. The grass terrain has a shader of its own
  and stays smooth for that reason.
- Grass, foliage, grass terrain, and water light themselves and are drawn after the deferred
  lighting, through `UniversalForwardOnly` passes.

### Pixel shadows

Even URP's hard shadows are a bilinear PCF tap, and one shadow-map texel covers several internal
pixels, so a plain shadow edge fades across a ramp of in-between tones. With **Enable Pixel
Shadows** on, the shadow is snapped to hard bands instead.

- **Shadow Steps** — 1 is a single hard edge; 2–4 keep a stepped penumbra.
- **Shadow Edge Bias** — moves the band thresholds to grow or shrink the shadow by a fraction of a
  shadow texel. Positive values grow the lit side.
- **Shadow Edge Filter** — how widely the shadow map is filtered before it is snapped. Snapping a
  single tap leaves the edge on the shadow-map texel grid: a staircase of teeth, turned against the
  screen by the light direction. Snapping a tent-filtered value puts the edge where the true
  outline crosses the middle of the filter, so it becomes a straight pixel line. **Tent 5x5**
  (9 fetches) is the default; 7x7 rounds sharp shadow corners more, 3x3 is too narrow to remove the
  teeth.

On the deferred paths the banded lighting pass does the snapping while it lights each pixel. On the
forward paths the renderer feature resolves the main-light shadow once per screen pixel right after
the depth prepass and publishes the result as URP's screen-space shadow texture, which URP Lit,
Simple Lit, and Shader Graph materials read through their standard `_MAIN_LIGHT_SHADOWS_SCREEN`
variant. Either way no material has to change.

Grass, foliage, and water sample the shadow map themselves through
`Shaders/Lighting/PixelShadows.hlsl` with the same quantisation, so they match in every camera.
Custom HLSL shaders can include
`Packages/com.schyro.pixel-pipeline/Shaders/Lighting/PixelShadows.hlsl` and call
`PixelPipelineMainLightShadow(positionWS)` the same way. Transparent materials cannot use a
screen-space shadow, so the feature switches them back to the ordinary shadow-map variant before
the transparent pass, exactly as URP's own feature does.

URP strips the `_MAIN_LIGHT_SHADOWS_SCREEN` variants from a player build unless an active **Screen
Space Shadows** renderer feature is present. That is why `Configure Renderers` adds it; its own
result is overwritten by the pixel pass, and the extra full-screen R8 pass costs next to nothing.

Shadow map resolution and **Shadow Distance** on the URP asset still decide how fine the outline
is. At 1024 over the default 50 m one texel spans about four internal pixels, so the filter has to
round corners by that much. A shorter distance or a larger map gives sharper corners and less acne
on surfaces at grazing angles to the sun.

## Wind

One wind drives every wind-driven system.

- **Direction** — the single world-space X/Z direction used by cloud motion, grass sway, rain
  drift, water waves, and the wind streaks.
- **Strength** — scales every wind response together. Zero is calm.
- **Rain Drift Speed** — horizontal velocity in metres per second added to falling drops at
  Strength 1. The visible streak angle follows the ratio between this and the Rain page's speed.

### Ground Height

**Ground Height** is the one world height the scene stands on. God rays end there, and the god
rays, wind streaks, and rain all centre on the point where the camera looks at that height. It is
shown at the bottom of the God Rays, Wind Streaks, and Rain pages and is the same value on all of
them.

## Day & Night

Add one `PixelEnvironmentController` to a scene and assign the shared settings asset, the Sun, an
optional Moon, and the target Camera.

- **Enabled** — turns the cycle on or off.
- **Advance In Play Mode** — lets the clock run while playing.
- **Start Time Hours** — the time a scene starts at.
- **Day Duration In Seconds** — length of a full 24-hour cycle.
- **Sunrise Time** / **Sunset Time** — when the sun crosses the horizon.
- **Orbit Axis Y** — rotates the sun's path around the vertical axis, in degrees.
- **Max Sun Intensity** / **Max Moon Intensity** — peak light intensity.
- **Shadow Strength** — strength of the sun's shadows. The moon's are weaker.
- **Light Colours** — sun colour at the horizon and at midday, moon colour at the horizon and at
  its highest.
- **Ambient Colours** — sky, equator, and ground ambient for day and for night, plus the camera
  background colour for each.

Sun and Moon colour and intensity are evaluated from their actual height in the sky. The controller
keeps directional shadows hard so they remain pixel-shaped after downsampling.

The runtime clock, cloud coverage, and rain amount live on the scene's controller, so editing one
scene never writes transient state back into the shared asset. **Current Time Hours** and **Target
Weather** on the controller are what the scene starts with: they are taken from the asset's **Start
Time Hours** and **Initial Weather** when an asset is assigned (or with **Reset To Profile** from
the context menu), and from then on the scene keeps its own values.

## Weather

Three states: Sunny, Cloudy, and Rain. Call `SetWeather` on the controller to request one, or use
the **Weather** entries of its context menu.

- **Initial Weather** — the state a scene starts in.
- **Sunny** / **Cloudy** / **Rain Cloud Coverage** — how much of the sky each state fills.
- **Cloud Build Seconds** / **Cloud Disperse Seconds** — how long coverage takes to rise and fall.
- **Rain Start Cloud Coverage** — the coverage at which rain begins.
- **Rain Fade In Seconds** / **Rain Fade Out Seconds** — how long rain takes to start and stop.
- **Lighting Multipliers** — sun intensity, ambient intensity, and god ray intensity for each
  state. Values in between are blended as the coverage and rain amount change.

A Rain request builds cloud coverage first and only begins raining after **Rain Start Cloud
Coverage**; a Sunny or Cloudy request fades rain out before cloud coverage is allowed to disperse.

## Clouds

Cloud shading is applied once by a low-resolution screen-space pass, which keeps ordinary geometry,
terrain, and grass aligned. It works on standard URP Lit and Shader Graph materials.

- **Enabled** — turns cloud shadows on or off. Off also disables god rays, which depend on them,
  and the cloud-shadow texture is not created.
- **Noise** — the cloud noise texture. Assign `Textures/CloudNoise`. Without it there are no cloud
  shadows.
- **Scale** — size of the cloud pattern in world space.
- **World Height** — the height the clouds are projected from.
- **Speed** — how fast the clouds move along the wind.
- **Threshold**, **Contrast**, **Power** — shape the noise into cloud masses: how much of it counts
  as cloud, how hard the edges are, and how the density falls off.
- **Coverage Influence** — how strongly the weather's cloud coverage shifts the threshold. Sunny
  raises the effective threshold, Cloudy fills the mask, and Rain fills it further, all without
  replacing the noise itself.
- **Projection Direction** — the direction the shadows are cast along.
- **Divergence Angle** — the noise is sampled twice, scrolling this many degrees to either side of
  the wind direction, so the clouds change shape as they move.
- **Shadow Strength** — how dark the cloud shadow is.
- **Shadow Minimum** — the least light the densest cloud lets through.
- **Shadow Steps** — flattens the cloud shadow into this many brightness bands. Zero keeps a
  smooth gradient.

## God Rays

God rays are light shafts where the sun falls through gaps in the clouds. The renderer feature
marches every internal pixel's view ray through the air between **Ground Height** and **Top
Height**, after the outline, and adds the light it gathers over the pixelated scene. A point in the
air is lit when the ground point it looks down on along the sun is a *gap*: clear, but with cloud
around it. Open sky has nothing around it and solid cloud lets nothing through, so a sunny day has
few or no shafts, and every shaft lands exactly in a sunlit hole of the cloud shadow. The shadow
map lets buildings and trees block them, and they follow the main light's direction and colour.

- **Enabled** — off removes the pass entirely. God rays need cloud shadows.
- **Color** — shaft colour, multiplied by the sun's own colour, so shafts warm up towards sunset.
- **Intensity** — overall brightness, before the weather's God Ray multiplier.
- **Gaps** — **Gap Size** is the patch of sky, in metres, a point is compared against; **Gap
  Threshold** and **Gap Softness** decide how much clearer than its surroundings it must be. The
  God Rays page marks the gaps live on the cloud shadow, for any weather.
- **Volume** — **Top Fade** and **Ground Fade** soften both ends, so a shaft beside a floating
  island does not stop in mid-air; **Haze Falloff** thins the air with height; **Density** is how
  quickly light builds up through a shaft; **Area Radius** keeps shafts around what the camera
  looks at; **Occlusion** turns the shadow-map test off.
- **Pixel Look** — **Steps** per ray, **Bands** of flat tone, and ordered **Dither** where tones
  meet.
- **Sun** — below **Minimum Sun Height** there are no shafts; they fade in over **Sun Fade Range**.

The Weather page's God Ray multipliers scale the shafts per state, and they disappear at night. The
pass runs at the internal resolution only where there is something to draw.

## Wind Streaks

Stylised wind trails: GPU-instanced curved ribbons travelling along the shared wind direction.

Add `PixelWindStreakRenderer` to the same GameObject as the environment controller and assign that
controller. The component only needs the controller reference, so a scene never stores VFX state
of its own. **Reseed Streaks** in its context menu restarts the pool.

Each streak is one instance of a flat ribbon mesh. The C# side owns a fixed pool: it moves a head
point along **Wind > Direction**, recycles it when its lifetime runs out, and uploads position,
seed, length, and width per instance. The vertex shader bends the ribbon behind that head and
billboards its width against the view plane, so the curve costs nothing in simulation and the draw
cost never depends on how long the scene has been running.

- **Rendering** — **Enabled** removes the draw entirely when off. **Material** takes
  `Materials/MAT_WindStreaks`. **Maximum Streaks** sizes the pool, capped at the 1023 instances one
  batch can carry. **Count Wind Response** decides how much of that pool is live at the current
  wind strength; at 0 the full pool always draws.
- **Volume** — **Area Size** and **Height Range** above **Ground Height** describe the box streaks
  spawn in, and **Follow Camera** keeps it centred on the point the camera looks at. Each streak's
  path is centred on the volume too, so a streak spends its whole life inside the visible band
  rather than drifting downwind before it dies.
- **Motion** — **Speed** is metres per second at Wind Strength 1; **Speed Variance** keeps the
  streaks from moving as one sheet. The lifetime range sets how long a streak lives, and **Life
  Fade** the fraction of it spent fading in and out. **Minimum Wind Strength** and **Wind Fade
  Range** are the calm cutoff: below the minimum nothing is drawn at all, and the effect fades in
  over the range above it.
- **Shape** — **Length** and **Width** ranges plus **Tail Width Scale**, which is what makes a
  streak read as a trail rather than a bar. **Segments** is the ribbon resolution the curve needs.
- **Curve** — **Curve Amplitude** is the sideways bend in metres and **Curve Frequency** the number
  of waves over one streak; below 0.5 the streak is a single gentle arc, above 1 it starts to hook.
  The offset is measured against the head, so the trail bends behind the streak instead of sliding
  the whole line sideways. **Curve Vertical** moves part of that bend onto Y, and **Curve Scroll
  Speed** animates the shape over time.
- **Appearance** — **Color**, **Intensity**, the **Head Fade** and **Tail Fade** tips, and **Edge
  Softness** across the width (zero keeps a hard pixel edge). **Alpha Steps** quantises the ramp
  into bands for the pixel look; it rounds up, so the thinnest streaks keep their lowest band
  instead of disappearing. **Light Response** scales the colour by the light around the streaks,
  the sun or moon plus the sky ambient, capped at full daylight: daytime streaks keep their colour,
  and at night or in a storm they dim with everything else. Zero keeps the colour as set.

Blending is the one value the settings do not push, because it is baked into the material:
`MAT_WindStreaks` ships additive, and its **Source Blend**/**Destination Blend** can be set to
`SrcAlpha`/`OneMinusSrcAlpha` for flat, non-glowing lines instead.

## Rain

Rain is drawn by `PixelRainRenderer` as two procedural draws, one for the drops and one for their
splashes, with no particle system, no colliders, and no CPU simulation. Both shaders work out every
drop from its instance id and the time in the same way (`RainDrops.hlsl`), so each splash plays
exactly where and when its drop lands.

Add `PixelRainRenderer` next to the environment controller and assign the controller. The
**Weather** page decides when it rains; this page decides what it looks like.

While it rains, the renderer feature draws a depth map of the scene along the falling direction of
the drops, the same way a shadow map is drawn for a light. A drop falls along that direction, so
the texel under its path holds the first surface it meets — ground, a roof, a tree crown, the water
surface — and it lands there. Drops are dealt out on world-space tiles, so they keep falling where
they are while the camera pans. Unlit as they are, drops and splashes follow the scene light and
darken at night.

- **Drops** — **Drop Material** takes `Materials/MAT_PixelRain`. **Drops Per Second** landing
  across the area at full rain, **Fall Height**, the speed and width ranges, streak **Length**, and
  **Light Response**. **Wind > Rain Drift Speed** slants them.
- **Area** — **Area Size**, the square around the point the camera looks at that rain falls in.
- **Impacts** — off lets drops fall through everything to **Ground Height** with no splashes.
  **Occlusion Resolution** sets the texel size of the map; **Depth Range** must contain everything
  rain can land on. **Occlusion Layers** picks what stops rain; an object also needs a `DepthOnly`
  pass, which URP Lit, grass, foliage, and water all have.
- **Splashes** — **Splash Material** takes `Materials/MAT_RainSplash`. **Splash Lifetime** and the
  size range. Each splash is a short pixel animation: an impact spike, a crown of droplets, and a
  flat ring squashed to the camera angle. **Minimum Up Facing** keeps walls free of splashes.

The map is rendered only for the controller's target camera, the one the drops are drawn for. It
reuses that camera's culling, so an object stops rain only while at least part of it is in view.
Where the map is empty, as over the edge of a floating island, drops fall on past **Ground Height**
without splashing.

## Water

Assign a material using `PixelPipeline/Water` to a horizontal, subdivided plane. All tuning is on
the **Water** page and is uploaded as shader globals, so every water surface in the scene stays in
sync. The material itself only carries three per-surface multipliers — `Surface Tint`,
`Depth Scale`, and `Reflection Scale` — for a trough that should read shallower than a lake.

The colours are surface colours, not final screen colours: the body, the surface pattern, and the
shore foam are all lit by the main light and ambient like any other surface, so the water darkens
at dusk and turns deep blue under the moon on its own. The refracted bed below is already lit by
the scene. Every pattern is anchored in world space, so orbiting or zooming the camera never moves
it.

- **Depth colour** — `Shallow Colour`/`Deep Colour` over `Depth Distance` metres, flattened into
  `Colour Steps` bands. `Shallow Opacity`/`Deep Opacity` decide how much of the riverbed shows.
  `Shadow Strength` is how much a shadow removes the direct light; ambient always remains.
- **Refraction** — a screen-space offset from the wave normal, rounded onto the internal pixel grid
  so it never wobbles at a finer scale than the visible pixels. Samples that turn out to sit in
  front of the surface are rejected, which is what keeps the shoreline from smearing.
- **Surface pattern** — a network of thin, wavy lines drawn from the borders of slowly reshaping
  cells, drifting downwind. `Scale` is the cell size in metres and `Line Width` is measured in
  internal pixels, so lines stay crisp one-pixel strokes at any zoom or view angle. `Warp` bends
  the cell borders into wavy lines (0 gives a crisp cell network), and `Coverage`/`Breakup Scale`
  break the network into patches.
- **Shore foam** — a solid band hugging every bank and rock that laps in and out
  (`Shore Foam Lap Speed`/`Amount`), and thin ripple lines that leave it and travel outwards,
  breaking up as they go (`Shore Ripple Spacing`, `Speed`, `Range`, and `Width` in internal pixels;
  a range of zero turns them off).
- **Glints** — pixel sparkles on world-anchored points where the waves mirror the main light
  towards the camera. Each one is a single pixel that briefly stretches into a short dash. They
  take the light's colour and vanish in shadow, so sunsets give warm glints and the moon faint,
  cool ones. `Threshold` sets how closely a wave must mirror the light; `Density` and `Speed`
  control how many are lit and how fast they twinkle.
- **Waves** — two crossing directional sines displacing vertices on Y only. The analytic normal is
  what drives refraction, the reflection distortion, and the glints, so `Wave Normal Strength` is
  the useful dial even when `Wave Height` is near zero.

### Shoreline

Add `PixelWaterShoreline` to the water surface for the shore foam to work properly. It bakes the
horizontal distance to the shore, so the foam has the same width on every bank and around every
rock whatever the camera angle. The shore is taken from the visible meshes where they cross the
water plane; colliders are not needed, and overlapping solids such as a rock sunk into a ledge are
handled.

The bake takes a few milliseconds and reruns on its own when the surface moves or a component value
changes. Call `Bake()` or use **Rebake Shoreline** from the context menu after moving the geometry
around it. Without the component the foam falls back to a depth-based estimate that only follows
banks facing the camera.

### Planar reflection

Add `PixelWaterPlanarReflection` to the water GameObject and assign the settings asset. It mirrors
the target camera through the plane, keeps its projection, and renders into a point-filtered
texture at the internal pixel resolution. Because points on the plane land at the same screen
position in both cameras, the shader samples it with plain screen UVs.

- `Reflection Clips Below Surface` applies oblique near-plane clipping, which works for the
  orthographic projection this pipeline uses. It is skipped automatically once the camera looks
  within a few degrees of the surface, where that matrix degenerates and the water is an edge-on
  sliver anyway.
- Untick `Enable Planar Reflection` to drop the extra camera pass entirely; the shader falls back
  to depth colour and refraction alone.
- The mirrored camera is excluded from the pixel passes, so it renders straight into its own
  low-resolution target instead of being downsampled and upscaled a second time.
- In the editor the reflection is built for the game camera, so it will not line up in the Scene
  view.
- The reflection is published as a single global, so there is one reflecting plane per scene. Extra
  water surfaces at other heights still render, but they would reuse that plane's reflection, so
  turn their material's `Reflection Scale` down to 0.

### Render order

Water sits in `Transparent-100`, after the opaque colour copy that refraction reads. The URP asset
therefore needs **Opaque Texture** on with **Opaque Downsampling** at None. Outlines still come
from the opaque depth and normals, so the geometry around and beneath the water keeps its lines and
they draw over the surface.

## Grass

`GrassSpawner` draws GPU-instanced billboard grass with `Materials/MAT_Grass`
(`PixelPipeline/GrassBlade`).

Component:

- **Rendering** — **Grass Mesh** (a quad), **Grass Material**, **Grass Count**, **Shadow Casting
  Mode**, and **Receive Shadows**.
- **Placement** — **Spawn Area** is a local box. Each blade is placed by a ray cast straight down
  from the top of the box, over the box's height, onto colliders on **Terrain Layer**, so the box
  has to contain the ground vertically as well. **Random Seed** makes placement deterministic.
  **Attempts Per Blade** multiplied by **Grass Count** is the total number of rays tried; rays that
  miss place nothing, so fewer blades than **Grass Count** can come out. **Spatial Tile Size** is the size of the tiles blades are batched into, which is what
  lets whole patches be frustum culled.
- **Scale** — **Blade Width** and the **Minimum**/**Maximum Height** range.

Use **Regenerate Grass** from the component's context menu after changing placement values while
the game is running.

Material:

- **Base Color**, **Color 2**, and **Color 3** with their noise textures, scales, and thresholds —
  patch colours laid out by world-space noise.
- **Accent 1** / **Accent 2** — occasional blades drawn with a different sprite, each with its own
  colour, frequency, height offset, and scale.
- **Cuts**, **Steepness**, **Wrap** — the light bands on the grass.
- **Wind Noise** settings and **Wind Sway Angle** — how the shared wind moves the blades.
- **Fake Perspective Scale** — how much the sprite is squeezed towards its tip as the wind passes,
  which reads as the blade bending towards or away from the camera.
- **Use Dither** / **Dither Strength** — dithering between tones.

`Materials/MAT_GrassTerrain` is for the ground under the grass. It uses the same world-space noise
textures, scales, and thresholds as `MAT_Grass`, so the ground carries the same colour patches. Its
Base Color, Patch Color 2, and Patch Color 3 are edited independently.

## Tree foliage

`PixelPipeline/Foliage` renders tree crowns the way the grass renders the meadow: alpha-cut leaf
sprites that always face the camera, patch colours from world-space noise, toon light bands, and
sway from the shared wind.

`PixelFoliageCanopy` scatters **Card Count** leaf cards over the surface of the **Canopy Shape**
(any closed mesh with Read/Write enabled; a built-in sphere otherwise), sinks each one a random
distance into the crown (**Depth Spread**), and stores a **Normal Roundness**-blended normal plus
how deep the card sits.

Component:

- **Canopy Shape** — the crown volume, in the object's local space.
- **Card Count** — number of leaf cards; each is one quad.
- **Card Size** — random card width range in metres, before the object scale.
- **Depth Spread** — how far into the crown cards may sink, as a fraction of its radius. Deeper
  cards fill gaps and are shaded darker.
- **Normal Roundness** — 0 uses the shape's own surface normal, 1 a perfectly rounded normal from
  the crown centre. Rounder normals give broad, clean light bands.
- **Minimum Normal Y** — cards facing further down than this are dropped, which flattens the
  underside of the crown.
- **Random Seed** — changes the layout.
- **Base Flexibility** — wind flexibility of the lowest cards. The top of the crown always moves
  fully.

The shader lights every card with the rounded normal, so the crown gets broad, clean light bands
instead of per-card noise, and darkens deep cards with **Inner Darkness**. One shadow sample per
card, taken just outside the crown (**Shadow Sample Offset**), breaks the shadowed side into
leaf-sized clusters. In the shadow pass the cards face the light, so the crown casts a solid
shadow.

Material:

- **Leaf Sprite**, **Alpha Cutoff**, **Card Size Scale**, **Card Rotation Jitter**, and **Sprite
  Shading Detail** — the look of a single card.
- **Base Color**, **Patch Color 2**, **Patch Color 3** with their noise settings, and **Per-Card
  Variation** — colour.
- **Light Bands**, **Wrap**, **Steepness**, **Inner Darkness** — lighting.
- **Wind Sway**, **Flutter Strength**, **Flutter Speed**, and the wind noise settings — motion.
- **Exclude From Outline** — writes the same outline marker as the grass.

The mesh is regenerated on load and never written into the scene. Component values rebuild it on
their own; use **Rebuild Foliage** from the component's context menu after editing a canopy shape
mesh.

## Pixel-perfect camera

For an orthographic isometric camera:

1. Create an empty camera pivot and place the Camera as its child.
2. Add `PixelPerfectIsometricCamera` to the pivot.
3. Assign `PixelPipelineSettings`, the child Camera, and an optional follow target.

Camera input uses Unity's Input System: hold the right mouse button and drag to orbit, then use the
mouse wheel to zoom. Both controls can be disabled independently on the component.

The renderer reads the camera-specific pan offset directly from the controller; it does not use a
shared global offset, so unrelated cameras do not inherit stale camera state.

## Demo scene

The demo ships with the package as a sample. In **Window > Package Manager**, select **Pixel
Pipeline**, open the **Samples** tab, and import **Demo**. Unity copies it to
`Assets/Samples/Pixel Pipeline/<version>/Demo`, where it can be edited freely.

Open `PixelPipelineDemo.unity` from that folder to see the complete pipeline on plain URP Lit
materials, the full environment cycle, animated cloud shadows, god rays, rain, outlines,
GPU-instanced grass, tree foliage, and water.

The sample brings its own settings asset, `PixelPipelineDemoSettings`, which the scene's camera,
environment controller, and planar reflection use. The project's renderer still needs the Pixel
Renderer Feature (see the quick start in the README); assign `PixelPipelineDemoSettings` to that
feature to see the demo with the settings it was tuned for.

Demo controls:

- `1`: Sunny
- `2`: Cloudy
- `3`: Rain
- `T`: pause/resume the day/night clock
