# UI Sprite Maker — guide for AI agents

This Unity 6 editor package bakes UI background sprites into PNG files. It supports rounded corners,
gradients, drop shadows, glows, inner shadows and strokes. The PNGs are imported as 9-sliced sprites
for UGUI `Image`. You describe a sprite as a **JSON spec**, and the package renders and imports it.
You don't need image editors or hand-drawn textures.

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
- Every spec needs `"output": "Assets/.../Name.png"`. Folders are created and existing files are overwritten.
- `-preview <dir>` also writes each sprite as a plain PNG to `<dir>/<Name>.png`. **View these images to check your work.**
- `-validateOnly` only checks the specs and writes nothing.
- `-quit` isn't needed. The method exits Unity itself: **exit code 0** means everything succeeded, **1** means at least one error.
- Read results from the log. Look for lines starting with `[UISpriteMaker]`:
  - `OK <path> <w>x<h>px @<scale>x border(L B R T)`
  - `PREVIEW <file>`
  - `VALID <path>`
  - `ERROR <json path>: <reason>`. Errors name the exact field, e.g. `sprites[1].fill.colors[0]: invalid color "#12345"`.
- To read back the spec of an existing sprite: `-executeMethod UISpriteMaker.Editor.UISpriteMakerCli.Describe -asset Assets/UI/X.png` (you can repeat `-asset`). The log prints `SPEC <path>` followed by the JSON.

The Unity executable is usually `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` (Windows) or
`/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity` (macOS).
Use the version in `ProjectSettings/ProjectVersion.txt`.

### B. C# API (`UISpriteMaker.Editor.UISpriteMakerApi`)

```csharp
using UISpriteMaker.Editor;

// Bake into the project (creates folders, imports as 9-sliced sprite). Returns size/padding/border info.
SpriteLayout layout = UISpriteMakerApi.Bake(specJson, "Assets/UI/Sprites/PrimaryButton.png");

// Render to any file without importing (for previewing / viewing the image).
UISpriteMakerApi.RenderPng(specJson, "C:/temp/preview.png");

// null when valid, otherwise an error message naming the field.
string error = UISpriteMakerApi.Validate(specJson);

// Spec JSON a sprite was baked with (null if not made by this tool). Edit it and Bake again to change a sprite.
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
| `output` | `"Assets/.../X.png"` | — | Used by the CLI only |
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
- **Keep text and icons out of the sprite.** This tool makes backgrounds only. Put TextMeshPro and icons on top.
- **Check before baking many sprites.** Run with `-preview` or `RenderPng` and look at the image first.
- **To change an existing sprite:** get its spec with `GetSpec` or `Describe`, edit the JSON, then bake again to the same path. GUIDs and references stay intact.
- For hover/pressed states, bake variants (e.g. darker fill, `innerShadow` for pressed) and use them in a `Button`'s **Sprite Swap** transition.
