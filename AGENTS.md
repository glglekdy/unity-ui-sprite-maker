# UI Sprite Maker — guide for AI agents

This Unity 6 editor package bakes UI background sprites into PNG files. It supports rounded corners,
gradients, drop shadows, glows, inner shadows and strokes, plus **layers**: shapes, images and text
inside the sprite, each with opacity, blend mode, rotation, clipping and its own effects (like Figma).
The PNGs are imported as 9-sliced sprites for UGUI `Image`, or a layered design can be exported as a
**UGUI prefab** (text becomes TextMeshPro, images become `Image`s). You describe a sprite as a
**JSON spec**, and the package renders and imports it. You don't need image editors or hand-drawn textures.

Package id: `com.glglekdy.ui-sprite-maker` · Namespace: `UISpriteMaker.Editor` (editor-only assembly)

## Choosing how to call it

| Situation | Use |
|---|---|
| Unity Editor is **closed** for this project, you have a shell | **A. Batch-mode CLI** |
| Unity Editor is **open** (batch mode would fail: project is locked) and you can run editor C# (Unity MCP `execute`/script tools, or by adding an editor script) | **B. C# API** |
| A human is designing by hand | Window **Tools › UI Sprite Maker**. Its **Spec JSON › Copy/Paste** buttons exchange specs with you |

### A. Batch-mode CLI

```sh
"<Unity.exe>" -batchmode -nographics -projectPath "<project>" \
  -executeMethod UISpriteMaker.Editor.UISpriteMakerCli.Bake \
  -spec "<path>/sprites.json" [-preview "<dir>"] [-validateOnly] \
  -logFile "<path>/unity.log"
```

- The spec file can hold one spec object, an array of specs, or `{ "sprites": [ ... ] }`.
- Every spec needs `"output": "Assets/.../Name.png"` and/or `"prefab": "Assets/.../Name.prefab"`. Folders are created and existing files are overwritten.
- `-preview <dir>` also writes each sprite as a plain PNG to `<dir>/<Name>.png`. **View these images to check your work.**
- `-validateOnly` only checks the specs and writes nothing.
- `-quit` isn't needed. The method exits Unity itself: **exit code 0** means everything succeeded, **1** means at least one error.
- Read results from the log. Look for lines starting with `[UISpriteMaker]`:
  - `OK <path> <w>x<h>px @<scale>x border(L B R T)`
  - `PREFAB <path> objects=<n> sprites=<n>`
  - `PREVIEW <file>`
  - `VALID <path>`
  - `ERROR <json path>: <reason>`. Errors name the exact field, e.g. `sprites[1].fill.colors[0]: invalid color "#12345"`.
  - `WARN <asset>: <reason>`: the asset was made, but something didn't come out as asked (a layer baked
    instead of becoming a prefab object, layers covering the 9-slice stretch area, an unreadable image, ...).
- To read back the spec of an existing sprite or prefab: `-executeMethod UISpriteMaker.Editor.UISpriteMakerCli.Describe -asset Assets/UI/X.png` (you can repeat `-asset`). The log prints `SPEC <path>` followed by the JSON.

The Unity executable is usually `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` (Windows) or
`/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity` (macOS).
Use the version in `ProjectSettings/ProjectVersion.txt`.

### B. C# API (`UISpriteMaker.Editor.UISpriteMakerApi`)

```csharp
using UISpriteMaker.Editor;

// Bake into the project (creates folders, imports as 9-sliced sprite). Returns size/padding/border info.
SpriteLayout layout = UISpriteMakerApi.Bake(specJson, "Assets/UI/Sprites/PrimaryButton.png");

// Build a UGUI prefab from a layered spec (sprites go to "<Name>_Sprites/" next to it).
PrefabBakeResult prefab = UISpriteMakerApi.BakePrefab(specJson, "Assets/UI/Prefabs/RewardCard.prefab");
// prefab.Objects, prefab.Sprites, prefab.Warnings

// Render to any file without importing (for previewing / viewing the image).
UISpriteMakerApi.RenderPng(specJson, "C:/temp/preview.png");

// null when valid, otherwise an error message naming the field.
string error = UISpriteMakerApi.Validate(specJson);

// Spec JSON a sprite or prefab was baked with (null if not made by this tool). Edit it and bake again to change it.
string spec = UISpriteMakerApi.GetSpec("Assets/UI/Sprites/PrimaryButton.png");

// Put the sprite on a UGUI Image: sets sprite, Sliced type, RectTransform size, raycast padding. Undo-able.
UISpriteMakerApi.ApplyToImage(image, "Assets/UI/Sprites/PrimaryButton.png");
```

The parsing methods throw `SpriteSpecException` for invalid specs.

## Spec reference

All lengths are in **UI units**, which are pixels at @1x. Only `size` is required.
**Effects you don't mention are off.** Unknown properties are errors, so check spelling.

| Key | Type | Default | Notes |
|---|---|---|---|
| `size` | `[w, h]` whole numbers 1–2048 | — (required) | Size of the shape itself, **without** shadow/glow room |
| `radius` | number or `[topLeft, topRight, bottomRight, bottomLeft]` | `0` | Clamped to half the short side. `radius ≥ h/2` gives a pill/circle |
| `scale` | 1–4 | `1` | Output resolution multiplier. Pixels Per Unit = 100 × scale, so the logical size stays the same. Use `2` for crisp UI on high-DPI screens |
| `nineSlice` | bool | `true` | Sets sprite borders for `Image.Type.Sliced` |
| `fill` | Fill | `"#FFFFFF"` | See below |
| `stroke` | `{ width, position, color \| fill }` | off | `width` default 2. `position`: `"inside"` (default) / `"center"` / `"outside"`. Use `color` for a solid stroke or `fill` for a gradient stroke |
| `shadows` | array of `{ color, offset, blur, spread }` (a single object also works) | none | Defaults: `#00000059`, `[0, 4]`, `12`, `0`. **Positive offset Y moves the shadow down** (like CSS/Figma). `blur` works like CSS blur radius. Negative `spread` shrinks the shadow |
| `glow` | `{ color, size, spread, intensity }` | off | Outer glow drawn behind the shape. Defaults: `#59BFFF`, `16`, `0`, `1` (intensity range 0–4) |
| `innerShadow` | `{ color, offset, blur, choke }` | off | Defaults: `#00000059`, `[0, 2]`, `4`, `0`. With offset `[0, 2]` the shadow appears along the **top** inner edge (a pressed/inset look) |
| `innerGlow` | `{ color, offset, blur, choke }` | off | Defaults: `#FFFFFF80`, `[0, 0]`, `10`, `0` |
| `layers` | array of layers | none | Shapes, images, text and groups drawn on top of the fill. See **Layers** |
| `clip` | bool | `false` | Clip layers to the frame's shape |
| `output` | `"Assets/.../X.png"` | — | Used by the CLI only |
| `prefab` | `"Assets/.../X.prefab"` | — | Used by the CLI only: also build a UGUI prefab. See **Prefabs** |
| `comment` | any | — | Ignored |

**Fill** can be written in these forms:
- A color string: `"#1E2233"`
- `{ "type": "solid", "color": "#1E2233" }`
- `{ "type": "linear", "colors": [...], "direction": "to top" }`. Instead of `direction` you can give `"angle": 90`.
  - Directions: `to right|top|left|bottom`, or diagonals like `to top right`.
  - `angle` is in degrees and **counter-clockwise from "to right"**: 0 = left→right, 90 = bottom→top. This is **not** the CSS convention. Prefer `direction` to avoid confusion.
  - Default: `to top`.
- `{ "type": "radial", "colors": [...], "center": [0.5, 0.5], "radius": 1 }`
  - `center` is normalized to the shape, with y going up: `[0.5, 0.5]` = middle, `[0.5, 1]` = top edge.
  - `radius` 1 = distance from the center to a corner.
- `colors`: 2–8 entries. Each entry is a color string (evenly spaced) or `{ "color": "#...", "at": 0..1 }`.
- If a fill has `colors` but no `type`, it is treated as linear.

**Colors:**
- Formats: `#RGB`, `#RRGGBB`, `#RRGGBBAA` (alpha last), `rgb(r, g, b)`, `rgba(r, g, b, a)` (r, g, b 0–255, a 0–1), or names like `white` or `black`.

The spec parser also accepts `//` line comments.

### Layers

The spec itself is the **frame**. `layers` lists what is drawn inside it, **bottom to top**. Shapes and
groups have their own `layers` (children), so layers nest.

**Coordinates:** every layer has a box. `position` is its top-left corner and `size` its width/height, in
UI units, **relative to the parent's box** (the frame's `size`, or the parent layer's box). **y grows
downwards** (like Figma/CSS): `[0, 0]` is the parent's top-left corner. `rotation` is in degrees,
**clockwise**, around the box center. Layers may stick out of the frame; the PNG grows to fit
(unless the frame has `"clip": true`).

Keys every layer accepts:

| Key | Type | Default | Notes |
|---|---|---|---|
| `type` | `"shape"`, `"image"`, `"text"` or `"group"` | — (required) | |
| `name` | string | type / text | Shown in the editor; used for prefab object names |
| `position` | `[x, y]` | `[0, 0]` | Top-left of the box in the parent's box, y down |
| `size` | `[w, h]` | see types | Box size. Numbers may be fractional |
| `rotation` | number | `0` | Degrees, clockwise |
| `opacity` | 0–1 | `1` | Applies to the layer and its children |
| `blendMode` | `normal`, `multiply`, `screen`, `overlay`, `darken`, `lighten`, `add` | `normal` | How the layer mixes with what is below it |
| `visible` | bool | `true` | Hidden layers aren't drawn |
| `locked` | bool | `false` | Editor only: can't be picked on the canvas |
| `constraints` | `{ horizontal, vertical }` | `left`, `top` | How the layer follows its parent when the parent is resized: `left`/`right`/`center`/`stretch`/`scale` and `top`/`bottom`/`center`/`stretch`/`scale`. Also sets prefab anchors |
| `export` | `auto`, `bake` or `object` | `auto` | Prefab export only. See **Prefabs** |
| `stroke`, `shadows`, `glow`, `innerShadow`, `innerGlow` | | off | Same as on the frame, and they work on **every** layer type: a shadow under text, a stroke around an image's opaque pixels, a glow around a group's content |

Per type:

| Type | Keys | Notes |
|---|---|---|
| `shape` | `size` (required), `radius`, `fill` (default white), `clip`, `layers` | A rounded rectangle, like the frame. `clip: true` cuts its children to its shape |
| `image` | `source` (required), `tint`, `fit` | `source` is an asset path to a Sprite or Texture2D (`"Assets/UI/Icons/Star.png"`; a sprite inside a multi-sprite texture: `"Assets/UI/Atlas.png#Star"`). `fit`: `stretch` (default), `contain`, `cover`, `sliced` (uses the sprite's 9-slice border). `size` defaults to the sprite's UGUI size |
| `text` | `text` (required), `font`, `fontSize` (default 24), `color` or `fill`, `align` (`left`/`center`/`right`), `verticalAlign` (`top`/`middle`/`bottom`), `lineHeight` (× the font's line height, default 1), `letterSpacing` (units), `wrap` | `font` is an asset path to a `.ttf`/`.otf` (or a TMP font asset with a source font). **Without `font`, Inter is used, which has no Korean/CJK glyphs**; the spec is rejected if the font lacks a character. Without `size`, the box fits the text (no wrapping). `fill` can be a gradient. Text may overflow its box |
| `group` | `size` (required), `clip`, `layers` | A box to arrange children in. `clip: true` cuts children to the box. A group with opacity, a blend mode or effects is composited as one unit |

**Paint order** inside a shape (and the frame): drop shadows, outer glow, fill, inner shadow, inner glow,
**children**, stroke.

### Prefabs

`"prefab": "Assets/.../Card.prefab"` (CLI) or `UISpriteMakerApi.BakePrefab` builds a UGUI prefab instead of,
or as well as, one flat PNG:

- The frame becomes an `Image` with a 9-sliced sprite. Layers that **bake** are drawn into it.
- Layers that become **objects** get their own GameObject: text → `TextMeshProUGUI`, image → `Image`,
  shape → `Image` with its own baked sprite, group → empty `RectTransform`. They are anchored by their
  `constraints`, rotated, and get `CanvasGroup` (opacity of a subtree) / `RectMask2D` (clip) as needed.
- `export: "auto"` (default): text and images become objects; shapes and groups become objects only when
  something inside them does. `"bake"` forces baking, `"object"` asks for an object.
- A layer is **baked anyway** (with a `WARN`) when UGUI can't show it: a blend mode other than `normal`,
  effects or a gradient on text, effects on an image or group, an image `fit` of `cover`, a Texture2D
  (not Sprite) source, or text when TextMeshPro isn't set up (import *TMP Essential Resources*). Baked
  layers are drawn below all object siblings.
- Text objects need a TMP font asset: one made from the same font file is reused, otherwise a dynamic one
  is created in `<Name>_Sprites/Fonts/`. Without `font`, TMP's default font asset is used.
- Sprites are written to `<Name>_Sprites/` next to the prefab. Baking again to the same path keeps the
  prefab's GUID.

### Example

```json
{
  "sprites": [
    {
      "output": "Assets/UI/Sprites/PrimaryButton.png",
      "size": [240, 72], "radius": 36, "scale": 2,
      "fill": { "type": "linear", "direction": "to top", "colors": ["#2F6BFF", "#6FA8FF"] },
      "shadows": [ { "color": "#1B3A8A66", "offset": [0, 6], "blur": 14 } ],
      "innerGlow": { "color": "#FFFFFF40", "blur": 6 }
    },
    {
      "output": "Assets/UI/Sprites/Panel.png",
      "size": [320, 200], "radius": 20,
      "fill": "#1E2233",
      "stroke": { "width": 1.5, "color": "#FFFFFF1F" },
      "shadows": [ { "color": "#00000080", "offset": [0, 12], "blur": 28 } ]
    },
    {
      "output": "Assets/UI/Sprites/GlowBadge.png",
      "size": [96, 96], "radius": 48,
      "fill": { "type": "radial", "center": [0.4, 0.6], "colors": ["#FFE27A", "#FF7A45"] },
      "glow": { "color": "#FF9A3C", "size": 18, "intensity": 1.2 },
      "stroke": { "width": 3, "color": "#FFFFFFCC" }
    }
  ]
}
```

### Layered example

```json
{
  "output": "Assets/UI/Sprites/RewardCard.png",
  "prefab": "Assets/UI/Prefabs/RewardCard.prefab",
  "size": [320, 120], "radius": 20, "scale": 2, "clip": true,
  "fill": { "type": "linear", "direction": "to bottom", "colors": ["#2B3150", "#1E2233"] },
  "shadows": [ { "color": "#00000080", "offset": [0, 8], "blur": 18 } ],
  "layers": [
    { "type": "shape", "name": "Icon", "position": [20, 28], "size": [64, 64], "radius": 18,
      "fill": { "type": "radial", "center": [0.3, 0.7], "colors": ["#FFE27A", "#FF7A45"] },
      "glow": { "color": "#FF9A3C", "size": 10 },
      "layers": [ { "type": "image", "source": "Assets/UI/Icons/Star.png", "position": [12, 12], "size": [40, 40] } ] },
    { "type": "text", "name": "Title", "text": "레벨 업!", "font": "Assets/Fonts/Pretendard-Bold.ttf",
      "fontSize": 28, "color": "#FFFFFF", "position": [100, 26], "size": [200, 40] },
    { "type": "shape", "name": "Badge", "position": [262, 12], "size": [44, 22], "radius": 11, "rotation": 8,
      "fill": "#FF4D6D", "constraints": { "horizontal": "right", "vertical": "top" },
      "layers": [ { "type": "text", "text": "NEW", "fontSize": 11, "color": "#FFFFFF",
                    "size": [44, 22], "align": "center", "verticalAlign": "middle" } ] }
  ]
}
```

## Using the result in UGUI

The PNG is larger than `size`, because it includes transparent room for shadows and glows (the padding).
The easiest route is `UISpriteMakerApi.ApplyToImage`. To set things up by hand:
- `Image.sprite` = the sprite
- `Image.type` = `Sliced` (use `Simple` if `nineSlice` is false)
- `Image.pixelsPerUnitMultiplier` = 1
- `RectTransform` size = PNG size ÷ scale. This makes the visible shape exactly `size`.
- `Image.raycastPadding` = padding ÷ scale, so clicks on the shadow are ignored. The padding values are in `SpriteLayout.Padding` and `ShapeRect`.

For a **resizable** element, e.g. a panel that stretches, keep `nineSlice: true` and give the RectTransform
the new size plus the padding. The corners and effects stay crisp.

## Tips and gotchas

- **Make one sprite per look, not per size.** A 9-sliced sprite stretches to any size, so bake a small
  base (e.g. radius 16 → `size` of about `[64, 64]`) and resize the Image.
- **Gradients don't 9-slice cleanly.** Only the middle stretches. For gradient fills, bake at the final size, or accept that the gradient will be uneven.
- Inner shadows and strokes widen the 9-slice border. That is expected.
- **Text and icons baked into a flat PNG can't change at runtime, and they stretch with 9-slicing.** For
  labels that change, or elements that must stay put when the sprite is resized, export a prefab
  (`"prefab"`), where text and images become separate objects, or put TextMeshPro/icons on top yourself.
- **Layers in a flat PNG and 9-slicing:** the stretch band is moved to columns/rows no layer covers.
  If layers cover all of them, the log warns: use the sprite at its designed size, give the layers that
  should stretch a `stretch` constraint, or export a prefab.
- Check text with `-preview`: fonts differ in size and spacing. Set `font` for anything but Latin text.
- **Check before baking many sprites.** Run with `-preview` or `RenderPng` and look at the image first.
- **To change an existing sprite:** get its spec with `GetSpec` or `Describe`, edit the JSON, then bake again to the same path. GUIDs and references stay intact.
- For hover/pressed states, bake variants (e.g. darker fill, `innerShadow` for pressed) and use them in a `Button`'s **Sprite Swap** transition.
