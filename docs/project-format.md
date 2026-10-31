# Compositor project format, macOS versions 1–19 and Windows extensions 12–28

A `.comp` file is a macOS document package containing `manifest.json` and an `images/` directory of `<layer UUID>.png` assets.

The manifest identifies `com.compositor.project`, version `19` for new saves (versions `1`–`18` remain readable), and the sRGB working space. It stores document UUID, pixel dimensions, active layer UUID, and layers in bottom-to-top order. Each layer stores its UUID, name, visibility, transform (origin, size, clockwise rotation, flips, sampling), and optional image filename. Blank layers have no image asset.


Embedded PNGs preserve source pixels and transparency; transforms remain separate. Projects survive moving or deleting imported source photos. Saving uses a coordinated atomic package replacement. Unsupported versions, invalid metadata, missing assets, unsafe paths, and oversized data are rejected before replacing the live document.

Limits: 30,000 pixels per canvas/image side, a memory-scaled document raster budget, 10,000 layers, 4 MiB manifest, 512 MiB per encoded asset. See `ProjectStore.swift` for validation.

Undo history and viewport are session-only. Opening fits the canvas, restores selection, and starts with clean history. Future editable features must extend the schema and round-trip tests. PNG export is a flattened derivative and does not mark project edits saved.

Image Size adds optional `resolution` (pixels/inch, 1–9600). Older manifests without it default to 72. This additive field retains version 1 compatibility. Both PNG and JPEG exports include document resolution metadata. Resampling stores the new layer pixels and bounds; undo retains the prior sources only during the current session.

Version 2 adds optional `parentID` and `isGroup` on layer records. A group has no image file. Root nodes have no parent; children refer to an existing group. Array order defines bottom-to-top sibling order; renderers traverse each group as a contiguous subtree. Visibility is inherited without changing child flags. Cycles, missing/non-group parents, image-bearing groups, and nesting beyond 64 ancestor levels are rejected. Group ancestors permit room for leaf nodes at the deepest level. Group metadata survives image/canvas resizing and cropping. Older app builds reject version 2 rather than misrender grouped documents. Collapse state is not serialized.

Version 3 adds optional per-layer `opacity` (finite 0–1) and `blendMode` (Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, Color Dodge, Color Burn). Missing fields default to full opacity and Normal. Group records required those defaults until version 8, which lets a folder carry its own opacity; a folder's opacity multiplies into every layer inside it, while its blend mode stays Normal because folders are pass-through. Effects are applied during compositing and retained as metadata when resizing sources. Files declaring older versions cannot contain non-default appearance values.

Version 4 adds optional `maskFile` and `maskEnabled` fields to individual layers. Mask filenames must be `<layer UUID>.mask.png` under `images/`; enabled defaults to true when a mask exists. Records without masks omit both fields. Groups cannot carry masks in this version. Files declaring versions 1–3 cannot contain mask metadata.

Masks store 8-bit grayscale coverage without alpha (white reveals, black hides). Their normalized extent matches the image’s local rectangle, so the same layer transform applies to both. A uniform 1×1 mask is valid and avoids allocating full-resolution pixels before painting. Nonuniform mask pixels and a thumbnail are immutable assets shared by history. Image Size resamples them with the image transform; Canvas Size and Crop preserve their pixels. A separate memory-scaled mask-pixel budget is available alongside the image-pixel budget; per-side and per-file limits also apply to masks. Disabled masks remain embedded and editable but do not affect compositing. Image-versus-mask target selection is session-only and reopens on image pixels.

Version 5 adds optional `maskSourceID`: the UUID of a non-group layer supplying live alpha in document coordinates. It multiplies the target’s alpha alongside its enabled raster mask. Source pixels, transform, opacity, raster mask and upstream live masks contribute coverage; visibility and RGB color do not. Sources remain independent layers. Missing references, self-links, cycles, group endpoints and chains over 256 nodes are rejected. Deletion can bake the live coverage into dependent image pixels (retaining their raster masks) or remove the links, as one undoable operation. Links survive image/canvas resize and crop. Older versions default to no live mask; older app builds reject v5.

UI terminology: these alpha links are clipping masks. Option-click assigns the lower sibling’s base or releases the connection. Multiple clipped layers share one base, show indented above it, and release when moved outside the contiguous stack. The underlying `maskSourceID` representation is unchanged.

Version 6 allows `maskFile` and `maskEnabled` on group records. A folder has no image, so its mask covers the folder's own transform rectangle (the canvas size when the folder was created); Image Size resamples it through that transform, and Canvas Size and Crop preserve its pixels, exactly as for layer masks. Groups are pass-through, so an enabled folder mask multiplies the coverage of every descendant layer, together with that layer's own mask and any enclosing folders' masks; clipping-mask coverage is unaffected. Files declaring versions 1–5 cannot give a group a mask, and older app builds reject v6.

Version 7 adds adjustment layers: a layer record with an optional `adjustment` object and no `imageFile`. Adjustment layers cannot be groups and cannot carry `text`; like any layer they take a transform, opacity, blend mode, raster mask and clipping link, and they affect everything composited below them within their folder. `kind` is one of `Hue/Saturation`, `Levels`, `Curves`, `Exposure`, `Gradient Map`, `Grain`, `Invert`, `Black & White` and `Color Balance` (version 9 adds three more). The record carries the settings of every kind, each optional and defaulting to an identity adjustment: `hue` (±360), `saturation` and `lightness` (±100), `colorize` and `hsvSettings` for Hue/Saturation; `levels` (four channel ranges, RGB then red, green, blue); `curves` (four channel point lists); `exposureSettings`; `gradientMapSettings` (`shadows`, `highlights`, `reversed`); `grainSettings`; `blackWhiteSettings`; `colorBalanceSettings`. Out-of-range or non-finite values are rejected. Files declaring versions 1–6 cannot contain adjustment records, and older app builds reject v7. See `LayerAdjustment.swift` for the exact ranges.

Version 8 lets a folder carry its own `opacity`, which multiplies into every layer inside it; a folder's blend mode stays Normal because folders are pass-through (files declaring 1–7 require folders at full opacity). It also adds an optional top-level `guides` array of alignment guides, each with `id`, `axis` (`horizontal` or `vertical`) and `position` in document pixels (finite, at most 1,000,000 in magnitude). At most 1,000 guides are stored; files declaring 1–7 cannot contain guides. Guides survive Canvas Size and Crop by offsetting with the canvas.

Version 9 adds three adjustment kinds that sample neighboring pixels: `Gaussian Blur` (`blurRadius`, 0.1–250 document pixels), `Motion Blur` (`motionAngle`, −90 to 90 degrees, and `motionDistance`, 1–2000) and `Add Noise` (`noiseAmount`, 0.1–400, `noiseGaussian`, `noiseMonochromatic` and `noiseSeed`, so the pattern is stable between sessions). Files declaring 1–8 cannot contain these kinds; the earlier adjustment kinds remain valid at version 7 and up.

Version 10 lets a text layer color some of its letters differently: optional `colorRuns` in its `text` metadata (see Editable text). Files declaring 1–9 cannot contain it.

Version 11 lets those letters use different faces too: optional `fontRuns` in the same metadata. Files declaring 1–10 cannot contain it. `colorRuns` stays valid from version 10.

### Additive layer fields

Later fields are optional and not gated on the version, so older readers ignore them and keep the pixels or the linked mask as they were:

- `maskPlacement` and `maskLinked`: an unlinked mask (`maskLinked` false; missing means linked) keeps its own transform in `maskPlacement`, a document-space rectangle like the layer transform, and no longer follows the layer when it moves. Both require a `maskFile`.
- `shape`: a layer made with the Shape tool keeps its style (`kind`, `red`/`green`/`blue`, `cornerRadius` in document pixels, and for lines `lineWidth` plus `start` and `end` as fractions of the layer box, and optional `lineStyle` and `lineCap` (version 15)) so it redraws cleanly when scaled. Its PNG is still an ordinary raster; once anything else changes those pixels the metadata is dropped.

### Editable text

Pixel layer records may include optional `text` metadata: content, PostScript font name, font size in pixels, RGB color, alignment, tracking, line spacing and optional `boxSize` paragraph bounds. Text wraps inside these bounds; changing them reflows the text without scaling the font. The PNG remains the display and export fallback. Older readers ignore this metadata. Transforms, duplication, masks, canvas-size changes, crop, trim and resolution-only image-size changes preserve it; destructive pixel operations, resampling included, rasterize text and omit the metadata on the next save. Missing fonts use the system font when edited, while the saved PNG preserves the original appearance until then. From version 10, optional `colorRuns` lists letters painted in another color than the text's own `red`/`green`/`blue`: each run has `location` and `length` in UTF-16 units of the content, plus `red`, `green` and `blue` (0–1). From version 11, optional `fontRuns` lists letters set in another face than `fontName`: the same `location` and `length`, plus `fontName`. Runs of either kind are sorted, do not overlap, have a positive length and end within the content; letters outside every run use the text's color or face.

### Layer effects

An optional `effects` record contains independent `stroke`, `shadow`, `colorOverlay`, `innerShadow`, `outerGlow` and `innerGlow` records. Stroke carries a size (0–500 layer pixels), a color, an opacity and an `inside` flag choosing which side of the edge it sits on; drop shadow and inner shadow each carry an angle, a distance, a blur, a color and an opacity; color overlay carries a color and an opacity; outer glow and inner glow each carry a size (0–500 layer pixels), a color and an opacity. Each supports optional `enabled` visibility (missing means visible); hidden effects keep all parameters and remain listed under their layer. Effects, including their visibility, are saved and participate in document undo. They stay with their layer through pixel edits; Image Size scales their sizes, distances and blurs with the image. Canvas previews run on a serial background worker with a shared pixel budget; exports render the full-resolution effects. A record omitting an effect means that layer does not have it, so older readers see the effects they understand and ignore the rest.

## Windows extension version 12

Windows 1.4.8.1 reads versions 1–12. The retained macOS implementation still reads/writes versions 1–11. Windows saves v12 only when a layer uses non-default `locks`, `fillOpacity`, `linkID`, or the document has alpha `channels`; otherwise it writes v11. Readers limited to v11 reject v12 rather than silently dropping these features.

- Layer `locks`: optional integer bitmask, transparency = 1, image pixels = 2, position = 4, all = 8; 0–15 accepted, omitted means unlocked. Group flags also protect descendants. Visibility remains editable. Lock changes participate in undo.
- Layer `fillOpacity`: optional finite number 0–1, omitted means 1. Scales layer content separately from layer effects; parent group fill scales children.
- Layer `linkID`: optional UUID shared by linked layers; linked members move and transform together unless protected.
- Root `channels`: optional ordered array of up to 64 alpha channels. Each contains `id` (unique nonempty UUID), `name` (nonempty UTF-8, max 16 KiB), `imageFile` (`channel-<UPPERCASE-UUID>.png`). Images live under `images/`, are 8-bit gray PNGs exactly the canvas dimensions, and count toward the mask pixel budget. White means selected, black unselected; intermediate gray values preserve partial coverage.

RGB visibility, active editing channel, layer search and collapsed rows are viewport state and are not serialized. Alpha channels are preserved by document undo and transformed with canvas crop, resize, rotation and flips. Selections themselves remain transient, as in earlier versions; save one to an alpha channel to retain it in a project.

## Windows extension version 13

Windows 1.4.8.2 introduced version 13, which reads versions 1–13. It writes v13 when any layer has non-default mask appearance, a vector mask path, or the text options below. Otherwise it writes v12 when the version-12 fields are needed, or v11. Windows 1.4.8.1 rejects v13; the retained macOS implementation still supports only versions 1–11. The original macOS format constant is intentionally unchanged.

New optional layer fields require `maskFile` and version 13:

| Field | Validation and behavior |
| --- | --- |
| `maskDensity` | Finite 0–1, default 1. Coverage becomes `1 - (1 - coverage) * density`, including outside the placed mask. |
| `maskFeather` | Finite 0–1000, default 0, in document pixels. Gaussian feathering uses a padded coverage surface so adjacent render tiles agree. |
| `maskVectorPath` | Nonempty SVG path string, at most 1,000,000 characters, parsed by Skia. Coordinates are in the saved mask raster's pixel grid, then placed through `maskPlacement` or the layer transform. Rendering redraws the path at the target scale. The required `.mask.png` remains its grayscale fallback. |

New optional `text` booleans `bold`, `italic`, `underline`, `strikethrough` require v13; absent means false. Bold and italic are synthetic font treatments. The editable metadata and layer PNG both carry the resulting appearance. The property panel displays point size using `fontSize * 72 / resolution`; persisted `fontSize`, `tracking`, and `leading` remain layer pixels.

Mask state and metadata are copied independently for history snapshots; pixel assets remain shared until edited. Linking an offset mask carries its existing placement along with the layer. Unlinking freezes its placement while moving the layer. A selected unlinked mask can transform without moving layer pixels. Group masks move when the group is selected, not when one child is moved independently.

Replacing mask pixels clears `maskVectorPath` unless the pixels are unchanged; image resizing transforms retained path coordinates to the new raster grid. Pixel operations such as inversion, brush painting, smoothing and edge shifting produce a raster mask. Density and feather remain separate metadata until applying the mask to layer pixels.

Properties panel position, active image/mask target, navigator visibility, history steps and snapshots are session state and are **not saved** in `.comp`. A document can have either one raster or one vector mask per layer, not both simultaneously. To preserve a selection between sessions, use the v12 alpha-channel mechanism. New copies from history receive a new document ID and independently owned pixel assets.

## Windows extension version 14

Windows **1.4.8.2 Preview** reads versions 1–14 and writes v14 only when a text layer uses the advanced typography below, or a shape layer is an arbitrary outline. Otherwise it writes the lowest version the document needs (v13, v12 or v11). Windows builds older than this section reject v14; the retained macOS implementation still supports only versions 1–11.

New optional `text` fields require v14; all are absent by default:

| Field | Validation and behavior |
| --- | --- |
| `smallCaps` | Boolean. Draws lowercase letters as reduced capitals (a synthetic treatment). |
| `allCaps` | Boolean. Draws every letter as a capital. |
| `superscript` | Boolean, and not together with `subscript`. The letters are reduced and raised above the baseline. |
| `subscript` | Boolean. The letters are reduced and lowered below the baseline. |
| `ligatures` | Boolean. Opens or closes the font's standard `liga` feature while shaping. |
| `kerning` | Boolean. Turns kerning on or off while shaping. |
| `features` | Array of up to 64 four-letter OpenType feature tags (for example `dlig`, `onum`, `frac`, `ss01`), each unique and alphanumeric, applied while shaping. |
| `direction` | `Auto`, `LeftToRight` or `RightToLeft`. `Auto` follows the first strong character. |
| `complexShaping` | Boolean. Whether HarfBuzz shapes the run, which is what gives Arabic, Hebrew and the Indic scripts their joined and contextual forms and their right-to-left order. |
| `language` | Optional shaping language, 1–64 characters, for example `ar` or `ja`. |
| `dynamic` | Boolean. Fills the `{name}`, `{width}`, `{height}`, `{resolution}`, `{layers}`, `{date}`, `{time}` and `{datetime}` tokens when the text is drawn. |

A `shape` record may now have `kind` `Path` with a `path` field: an SVG path outline, normalized to the unit square, filled into the layer box and redrawn at any scale. Text-to-vector writes it, and the `.png` remains the display and export fallback. `path` is required, nonempty and parseable for a `Path` shape, and needs version 14.

Non-linear history and the history brush are session state like every other history feature: neither is saved. A saved history brush source is not retained when the document is reopened.

## Windows extension version 15

Windows **1.4.9.2 Preview** reads v1–v15. A document requires v15 when it contains `blending`, `effects.items`, globally disabled effects, a group with effects or non-Normal blending, a color label, a frame/artboard, or an embedded smart object. Otherwise the existing lowest-required-version rule (v11–v14) applies. Older Windows builds and the original macOS app reject unsupported versions rather than silently discarding these features.

New optional layer fields:

| Field | Representation |
| --- | --- |
| `blending` | Advanced blending record described below. |
| `label` | `None`, `Red`, `Orange`, `Yellow`, `Green`, `Blue`, `Violet`, `Gray`. Absence means None. |
| `container` | Group-only: `Group`, `Frame`, `Artboard`; absence means ordinary group. Frame/artboard bounds use the group transform. An artboard paints white and clips descendants; a frame clips descendants and may also have a vector mask. |
| `smartObjectFile` | Exactly the uppercase layer UUID followed by `.SMART.ZIP`, in `images/`. Requires a non-group layer with a cached `imageFile`. |
| `smartObjectID` | Nonempty source UUID, paired with `smartObjectFile`; copies with the same ID share source updates. Independent copies get a new ID. |

`effects` retains its six legacy slots for older projects. New fields are `enabled` (default true), `globalLightAngle` (degrees, −360…360, default 120), and optional `items` (ordered list, maximum 40). When `items` exists it is authoritative; legacy slots are converted when edited in the new dialog. Each item has a `kind`: `BevelEmboss`, `Stroke`, `InnerShadow`, `InnerGlow`, `Satin`, `ColorOverlay`, `GradientOverlay`, `PatternOverlay`, `OuterGlow`, `DropShadow`.

Item fields: `enabled`, `blendMode`, `opacity`, `red`/`green`/`blue`, `red2`/`green2`/`blue2`, `size`, `distance`, `angle`, `useGlobalLight`, `spread`, `noise`, `contour`, `contourEnabled`, `invert`, `centerSource`, `position`, `fillType`, `gradient`, `scale`, `offsetX`/`offsetY`, `alignWithLayer`, `pattern`, `patternPng`, `textureEnabled`, `textureDepth`, `bevel`, `technique`, `depth`, `soften`, `altitude`, `highlightOpacity`/`shadowOpacity`, `highlightMode`/`shadowMode`. All numeric values must be finite. RGB and opacities are 0…1; size 0…500, distance 0…5000, angle −360…360, spread/noise 0…100, scale 1…1000 percent, offsets ±100000, texture depth ±1000, bevel depth 0…1000, soften 0…100, altitude 0…90. `fillType` is 0 color / 1 gradient / 2 pattern; `technique` is 0 smooth / 1 chisel hard / 2 chisel soft. Other modes use the enum strings in `LayerStyle.cs`. Invalid values are rejected.

`patternPng` is base64 PNG, maximum 2,800,000 characters and 1024×1024 pixels, at most 8-bit channels. The UI scales imports to 512 pixels on their longest edge. Pattern `Image` requires valid image bytes; malformed encoded assets are rejected. Settings JSON presets use `LayerStylePreset` version 1, independently of the project version, and are not Adobe ASL files.

`blending` contains `red`/`green`/`blue` (all true by default), `knockout` (`None`, `Shallow`, `Deep`), `blendInteriorEffectsAsGroup` (false), `blendClippedLayersAsGroup` (true), `transparencyShapesLayer` (true), `layerMaskHidesEffects`/`vectorMaskHidesEffects` (false), and `ranges` (up to four unique channels). Each range contains `channel` (`Gray`, `Red`, `Green`, `Blue`), `source` and `underlying`. Each band stores `black`, `blackSplit`, `whiteSplit`, `white` with `0 ≤ black ≤ blackSplit ≤ whiteSplit ≤ white ≤ 255`; defaults 0,0,255,255. Split ranges linearly fade coverage. Band restrictions multiply. These CPU semantics are documented in the [layer-style guide](../windows/LAYER-STYLES.md), not a claim of Adobe pixel parity.

An embedded smart ZIP contains its own `manifest.json` and the source image/mask/channel/smart files at the ZIP root under their manifest filenames. It is never extracted to disk. Each source and the aggregate source data in a project are limited to 128 MiB compressed; opening a source checks for duplicate entries, at most 30,001 entries, and at most 512 MiB total expanded bytes, plus the normal manifest and image limits. Nested sources remain lazy until opened. The parent PNG is the display fallback; opening contents restores editable layers. Saving contents updates matching source IDs; saving the parent persists the new source and cache together. No Adobe PSB, linked external file, or PSD smart-object round-trip is implied.


## Windows extension version 16

Windows **1.4.9.3 Preview** reads v1–v16. A document uses v16 when a text layer has a non-null `vertical` field, or a `shape.kind: path` has a non-null `lineWidth` (open pen-path stroke). Other documents continue to choose the lowest required version from v11–v15.

- `text.vertical`: the optional `vertical` boolean, absent for horizontal text. When true, upright Unicode text elements are laid out top-to-bottom in columns running right-to-left. Font, color runs, tracking, leading, paragraph bounds and text content retain their existing semantics.
- `shape.lineWidth` on `kind: path`: positive pixel width for an open SVG path stroke, with round caps/joins; null keeps a filled path. The stored `path` remains normalized to the layer unit square, with the cached PNG as raster fallback.
- Placement mode, pivot, warp grid and toolbar settings are session state, not manifest fields. Confirmed distortion/warp becomes image pixels and a regular transform.
- The layer clipboard captures a self-contained package of selected records/assets. Paste remaps layer, parent, clip and link identifiers and owns fresh decoded images. It does not alter the on-disk clipboard or project schema.

Version 15 and older readers reject v16. This is a Windows extension; it does not imply macOS or PSD write-back compatibility.

## Windows extension version 17

Windows **1.4.9.4 Preview** reads v1–v17. Nonempty document `marks` requires v17; a document without annotations still uses the lowest required v11–v16 format. Old Windows readers and the upstream macOS reader reject unsupported versions.

`marks` is an optional array of non-printing document annotations, at most 10,000. Each record has:

| Field | Meaning / default |
| --- | --- |
| `id` | Required, unique nonempty UUID. |
| `kind` | Required enum string: `Slice`, `Note`, `Count`, `Sampler`, `Measure`. |
| `x`, `y` | Required finite document-pixel coordinates of the anchor. |
| `width`, `height` | Default 1. Slice dimensions must be positive; for a measure these are signed endpoint offsets. Other kinds use the anchor. |
| `name`, `text` | Name up to 256 characters; note text up to 8,192. Default empty strings. |
| `group` | Count group name, up to 256 characters; default `1`. Numbering follows record order within a group. |
| `url` | Slice metadata, up to 2,048 characters; default empty. It is never executed or automatically requested. |
| `color` | Unsigned 32-bit ARGB display color; default `0xFFFFB74D` (stored as a JSON number). |
| `visible` | Overlay visibility, default true. Does not affect exported art. |

Coordinates and dimensions have absolute values at most 1,000,000, and the existing 4 MiB manifest limit still applies. Invalid enum values, null strings, invalid coordinates or duplicate IDs are rejected. Missing `marks` means an empty list. Annotation records are immutable for undo snapshots. Canvas/image geometry also transforms the annotations; slices remain axis-aligned bounding rectangles, while measurements keep transformed endpoints.

Marks never enter composite image/PDF output or the layer clipboard. Slice export renders the intersection with the canvas as PNG plus a JSON manifest of filenames, rectangles and URL metadata. Full document copies keep marks. Tool settings, source-sampling points for brushes, in-progress crop/selection gestures and cursor state remain session-only. These annotations are not PSD metadata and do not imply Photoshop interchange support.

## Upstream macOS version 12

Upstream 1.4.10 lets a layer image be a 16-bit PNG, displayed through a dithered 8-bit preview. Its PNG bit depth identifies the precision without new manifest fields. Masks stay 8-bit. This shares the version number with the earlier Windows extension 12; readers inspect image precision and optional Windows fields independently.


## Windows extension version 18

Windows 1.4.10.1 reads versions 1–18 and writes the lowest version needed. Version 18 is required for deep layer data or any of the metadata below. Upstream v12 16-bit PNG and v13 EXR can be read; normal Windows document saves use v18 PNG plus float sidecars for HDR. Raw ProjectSnapshot save preserves a v13 EXR payload as EXR. An EXR and a floatImageFile sidecar on the same layer are rejected. Version numbers 12 onward have distinct Windows and macOS feature meanings. macOS cannot open Windows v18.

| Scope / field | Representation |
| --- | --- |
| manifest `imageSettings` | Optional object: `mode` = Bitmap/Grayscale/Duotone/Indexed/RGB/CMYK/Lab/Multichannel (default RGB), `depth` = 8/16/32 (default 8), optional `palette` of 2–256 ARGB uint values, `ink` ARGB uint (default 0xFF352818), nullable `background` ARGB canvas fill. Bitmap, Indexed and Duotone require depth 8. Working pixels remain sRGB. |
| layer `floatImageFile` | Optional `float-<UPPERCASE-UUID>.rgba` file within images, permitted only alongside a layer image PNG. This is zlib-compressed little-endian row-major RGBA float32, unassociated alpha, exactly width × height × 16 bytes. Dimensions come from the PNG. No header or row padding. Values must be finite; RGB in ±65536, alpha 0–1. Decompressed data must fit the asset budget. |
| manifest `variables` | Up to 10,000 `{name, layerID, kind}` objects; kind Visibility/Text/PixelReplacement. Names unique case-insensitively, nonempty, at most 128 characters; layer ID must exist. |
| manifest `dataSets` | Up to 1,000 `{name, values}` objects; values maps variable names to strings. Name max 128, at most 10,000 values, each value max 100,000 characters. Existing total manifest budget still applies. |
| manifest `measurementScale` | `{pixels, units, unit}`; finite positive pixels/units ≤ 1,000,000; unit nonempty ≤ 64 characters. Defaults 1 pixel = 1 px. |
| manifest `measurements` | Up to 10,000 records: `name`, `time` (ISO 8601), `area`, `perimeter`, `width`, `height`, `length`, `angle`, `count`, `mean`, `unit`. Measurements finite; name ≤ 1,024, unit ≤ 64. |
| manifest `measurementFields` | Up to eight names from Area/Perimeter/Width/Height/Length/Angle/Count/Mean, controls CSV output. |
| text `layout` | Optional editable `TextLayoutOptions` object as below. Omitted retains previous layout behavior. |

16-bit layers store an actual 16-bit RGBA PNG in the existing `imageFile`. 32-bit layers additionally store `floatImageFile`; the PNG is a 16-bit compatibility proxy and does not replace the float resource. Masks and Alpha channels remain 8-bit coverage. PNG bit depth identifies deep images without an additional layer-depth field. Float files have the same path containment and resource budget checks as other package resources. Exporting a composite is distinct from saving original layer precision; complex compositing may use an 8-bit renderer.

The text `layout` fields are `horizontalScale` / `verticalScale` (percent, default 100, 1–1000), `baselineShift` and `firstLineIndent` (default 0, ±10,000), `leftIndent`, `rightIndent`, `spaceBefore`, `spaceAfter` (default 0, 0–10,000), `justification` (None/LastLeft/LastCenter/LastRight/All), `smoothing` (None/Sharp/Crisp/Strong/Smooth, default Smooth), `kinsoku` (default true), and `tsume` (percent 0–100, default 0). Layout distances are layer pixels; font size UI converts to points using document resolution. These fields supplement the existing language, direction and OpenType tags, not an Adobe paragraph engine.

Document duplication and undo preserve these fields. Source sampling points, three-dot expansion state, plugin installation state, active plugin processes and measurement tool gestures are not project metadata. Plugin results become ordinary image layers; external plugin binaries are never embedded in `.comp` packages.

### Upstream macOS version 13

Version 13 lets a layer's image file be OpenEXR, named `<layer UUID>.exr`: an HDR layer, whose pixels hold light brighter than white as half floats in extended sRGB (white is 1). The display preview clips to white; current upstream edits normalize and restore above-white highlights where supported. An EXR whose values all stay within white loads as a 16-bit layer. Masks stay 8-bit PNG. Files declaring versions 1–12 cannot contain EXR images, and older app builds reject v13.

## Windows extension version 19

Windows 1.4.10.2 reads versions 1–19 and writes the lowest necessary version. Version 19 adds `adjustment.kind = "Extended"` with a required `extended` settings object. Existing adjustment kinds remain unchanged; an `extended` object on another kind is invalid. Earlier Windows and current macOS builds reject v19.

`extended.kind` is one of BrightnessContrast, Vibrance, PhotoFilter, ChannelMixer, ColorLookup, Posterize, Threshold, SelectiveColor. These are pointwise operators supported by editable adjustment layers; global-statistics operations such as Equalize are rejected for this layer type.

| Field | Representation and validation |
| --- | --- |
| `values` | Non-null object, at most 128 numeric parameters. Names retain case (for example `Brightness`, `Contrast`, `Density`); values finite with absolute value at most 10,000. Missing parameters use the operator defaults. |
| `color`, `replacement` | ARGB uint colors. Defaults 0xFFEC8A00 and 0xFF3399FF. |
| `preserveLuminosity`, `monochrome`, `absolute`, `range` | Boolean, boolean, boolean and integer 0–8; defaults true, false, false, 0. Operator-specific settings are retained on reopen. |
| `cube` | Optional embedded color lookup: `size` 2–65; `values` exactly size cubed `{r,g,b}` finite samples, red index varies fastest; `minimum` and `maximum` finite RGB domain points with minimum strictly below maximum per channel. No external LUT path is saved. Existing manifest size limits still apply. |
| `referenceMean`, `referenceDeviation` | Optional three finite numbers; deviations nonnegative. Shared adjustment serialization fields, unused by the eight v19 layer operators. |

Example inside a layer record: `"adjustment": {"kind":"Extended","extended":{"kind":"BrightnessContrast","values":{"Brightness":25,"Contrast":10}}}`. Blend, opacity, clipping and mask fields keep their existing meanings. A project with these layers writes v19 even if no other Windows extension is used. Pixel-only application of the same operation does not itself require v19.

UI preferences, GPU cache limits, cached display scenes, recovery scheduling and cursor settings are not project fields. GPU display acceleration does not change the saved format or replace original layer pixels.

## Windows extension version 20

Windows 1.5.1.1 reads versions 1–20 and writes the lowest version required. These additions require v20. Previous Windows and macOS versions cannot read v20 projects.

| Field | Representation |
| --- | --- |
| layer `maskVectorEvenOdd` | Optional boolean alongside `maskVectorPath`. True selects even-odd filling, used for PSD subpaths/holes. The grayscale fallback is retained. Absent/false preserves the existing winding rule. |
| text `styleRuns` | Optional nonempty ordered list of `{location,length,style}`. Indices are UTF-16; lengths positive; ranges must not overlap or exceed `content`. Each `style` is a flat `LayerTextStyle` with empty content, no nested runs, color runs, font runs or frame. Runs can override font, size, color, decorations, OpenType and character layout. Uncovered characters inherit the layer style. The editor moves/deletes ranges with text and selects by grapheme boundaries. |
| text `geometry` | Optional `{shape,bend,horizontalDistortion,verticalDistortion}`. Shape Block/Circle/Arch/Bowtie; the three numbers are finite percentages in −100…100. Geometry remains editable and is rendered into the text layer's pixel fallback. |
| text `layout.bottomLeading`, `layout.tateChuYoko` | Boolean defaults false. Bottom-leading changes baseline placement; tate-chu-yoko groups a styled run horizontally inside a vertical column. |
| text `layout.smoothing` | Adds WindowsLcd and Windows to the earlier enum values. These select Skia font edging/hinting, not a DirectWrite/Photoshop engine. |
| layer `label` | Adds Teal, Indigo, Magenta and Purple; existing enum values retain their order and meaning. |
| `imageSettings.cmykProfile` | Optional base64-encoded CMYK ICC bytes, 128 bytes to 4 MiB, with CMYK color-space and acsp header signatures. The profile describes proof/output inks; saved layer pixels remain sRGB. The existing overall manifest budget still applies. |

PSD TySh/EngineData and vector masks are translated into these existing text/path fields. Unsupported PSD metadata is reported and keeps a stored pixel fallback where possible. A layer with both pixel and vector masks is combined into the current single-mask representation; enabled flags determine the combined coverage. This does not claim complete PSD round-trip interchange.

Font favorites, font-preview size, named character/paragraph styles, mask display color/opacity, remembered refinement settings, thumbnail sizes/cropping and active proof/gamut toggles are editor state, not per-project fields. Select and Mask applies pixel coverage, optional color decontamination and the selected output action. Progressive Blur is a destructive pixel filter, so its parameter bag does not increase the project format version.

## Windows extension version 21

An optional guide `artboardID` identifies the artboard selected when that guide was created. Coordinates remain document-space pixels; guide drawing is not clipped to that artboard. Missing or null values identify canvas guides. An owner that no longer exists is treated as a canvas guide. This permits artboard deletion without discarding its guides. The field is preserved by history, independent document copies, rotation, save and reload. Non-null ownership requires version 21; ordinary guides retain their previous version requirements. Old Windows builds reject v21. Upstream macOS does not implement this Windows extension.

Guide selection, hover color and floating window/view state are not document fields. Named workspaces and pasteboard preferences are user settings. Shared document windows reference one document/history; closing a view does not dispose the other view's pixels.

## Windows extension version 22

An artboard layer can store optional `artboardBackground`, an unsigned 32-bit ARGB integer. Missing or null means opaque white (`4294967295`); zero means transparent. Non-white backgrounds require v22. Default white omits this field and retains the minimum format version required by the other document features. Rendering, history, independent copies and project round trips preserve the value. Only layers with `container` set to the artboard enum use it. Windows reads versions 1–22; this extension is not implemented by upstream macOS. Tool presets and workspace placement remain user settings outside the project manifest.

## Upstream macOS version 14

Version 14 adds optional `smoothing` to a layer’s `shape` style. True draws rectangle corners with Figma-style 60% smoothing (the iOS squircle match); false or missing keeps circular corners. Radius stays in document pixels, and smoothing reduces when the corners have insufficient room. Drawing previews and scaled redraws use the same geometry. Files declaring versions 1–13 cannot enable smoothing, and older app builds reject v14.


## Windows extension version 23

Windows **1.5.3.0 Preview** reads versions 1–23. Optional rectangle `shape.smoothing` is a Boolean: true uses the upstream 60% continuous-corner construction; null or false uses circular rounded corners. A zero corner radius disables smoothing. Windows writes v23 when a live shape stores a non-null value. The same field is also read from upstream macOS v14 projects. Other Windows v14 extensions are independent and are not macOS-compatible.

Slice `marks` add `noImage` (default false), `target`, `altText` (default empty strings), and nullable `background` (unsigned ARGB, null means transparent). Existing `name`, `url`, and `text` carry the slice name, URL and message text. Non-default extended slice options require v23. URLs and targets are metadata only; loading/exporting never visits them. Automatic slices are derived from canvas geometry and user slices, not stored until promoted, edited or divided. PNG/JSON export includes automatic slices and metadata; no-image slices omit PNG output.

Tool options, crop presets, object-finder overlays and workspace visibility are user settings outside the manifest. Selection coverage and crop drafts remain session state. Saving other documents continues to choose the lowest required format. Older Windows builds and macOS reject v23; use flattened exports when sharing with those versions.

## Windows extension version 24

Windows **1.5.3.2 Preview** reads versions 1–24. The optional document-level `workPath` is an SVG path-data string in document coordinates. It stores one working path with any number of subpaths; open and closed cubic contours remain editable. It contributes no pixels to rendering or export. Missing or null means no work path. The reader rejects empty, malformed, nonfinite, or over-1,000,000-character data, and rejects this field on versions below 24.

The work path is included in document clones, undo/redo, recovery snapshots and saves. Canvas resize/crop, image resize, rotation, flip and perspective crop transform it alongside document geometry. Pen draft points, selected nodes, path appearance, tool settings and selection coverage are not serialized in this field. A document containing a work path writes v24; other documents continue to use the lowest required version. Older Windows builds and macOS cannot read v24. Converting to a shape and deleting the working path allows the usual lower-version rules to apply; flattened export remains available.

## Windows runtime resource policy (2026-10-22)

This implementation revision does not change the manifest schema or version. Encoded assets remain limited to 512 MiB; float32 sidecars also retain their 512 MiB decompressed limit, now checked before creating a 32-bit asset. Save encoders write one asset at a time into the existing staging package and verify encoded size before publication. Cancellation preserves the previous package. Runtime admission additionally accounts for bytes per pixel and process private memory; a schema-valid file can therefore be refused when the process budget is insufficient. PSD/PSB input uses seekable 64-bit offsets and row decoding; the package asset limit no longer acts as a limit on the entire external PSD file. See [runtime performance policy](../windows/PERFORMANCE.md).

## Upstream macOS versions 15–16

Version 15 lets a line carry `lineStyle` (`Solid`, `Dashed`, `Long Dash` or `Dotted`, with dashes measured in multiples of the line's width) and `lineCap` (`Square` or `Round`, applied to the line's ends and to every dash and dot) in its `shape` style. Missing means solid with round caps, as every line was before version 15 (new lines start square-capped); other shape kinds cannot carry them. Files declaring 1–14 cannot contain them, and older app builds reject v15 rather than failing to read the unknown values.

Version 16 adds three adjustment kinds from the Filter menu: `Progressive Blur`, `Bloom / Glow` and `RGB Split`. Their settings are in the adjustment's optional `filterLayerSettings`: `progressiveFrom` and `progressiveTo` (0–250 document pixels), `progressiveStart` and `progressiveEnd` (document points), `progressiveRadial`, `progressiveInverted`; `bloomAmount` (0–100) and `bloomRadius` (1–150); `rgbSplitDistance` (0–200), `rgbSplitBlur` (0–400), `rgbSplitAngle` (−180 to 180 degrees), `rgbSplitRadial`, `rgbSplitCenter` (a document point) and `rgbSplitReach` (document pixels from the center at which Radial's full distance applies). Files declaring 1–15 cannot contain these kinds or `filterLayerSettings`, and older app builds reject v16.

## Windows extension version 25

Optional `lineStyle` (`Solid`, `Dotted`, `Dashed`, `Long Dash`) and `lineCap` (`Square`, `Round`) on a Line shape match upstream macOS v15. Missing fields retain the old solid/round rasterization. New Windows lines default to Solid/Square. Widths are in document pixels; dash/gap multiples are 1/1, 3/2 and 7/2 respectively. The Windows writer chooses v25 when either field is present, preventing older Windows builds from silently changing their appearance. The reader also accepts these fields in upstream v15 and v16. Windows and macOS version numbers are independent extension sequences: v25 is not readable by the upstream v16 writer/reader.

Windows 1.5.5.1 reads upstream v16 `filterLayerSettings` adjustment kinds (Progressive Blur, Bloom, RGB Split). The Windows writer uses its independent v26 extension when retaining them. Unknown adjustment kinds are still rejected rather than discarded. These three filters currently use the 8-bit CPU compositor; deep source resources remain stored, but their composite output is not an all-float HDR pipeline.

## Windows extension version 26

Windows 1.5.5.1 adds the following optional fields. Existing files default to the prior behavior. The writer chooses v26 only when these features are present, otherwise retaining the lowest supported Windows version required by the document.

- Layer `frameStyle`: `shape` (0 rectangle, 1 ellipse, 2 triangle, 3 hexagon, 4 custom), nullable `stroke` (ARGB integer, null means none), `strokeWidth` (0–1000 px), `strokeAlignment` (0 inside, 1 center, 2 outside), `cornerRadius` (0–30000 px), `customPath` (normalized SVG path, finite coordinates and bounds within [0,1], at most 100000 characters). The frame remains a group with its existing vector clipping mask; the style supplies its editable outline.
- Document `countGroups`: array of `name` (1–256 characters), `color` (ARGB), `visible`, `markerSize` (1–10 px), `labelSize` (8–72 px). Defaults are name "1", cyan 0xFF9AFFFF, visible, 2 px and 8 px. Count marks retain their `group` name; empty groups can persist without marks.
- Mark `author` (0–256 characters, default empty). Measure marks may carry paired `secondX` and `secondY` for the second arm endpoint in document coordinates. Both must be present together, finite and within ±1000000. Other mark kinds cannot use a second arm. These fields, like other marks, do not enter composite export.
- Adjustment `filterLayerSettings` and the three upstream v16 kinds documented above. Their existing field names and ranges are retained, with all coordinates in document pixels; Windows defaults and validation are defined in `FilterLayerSettings.cs`. `rgbSplitReach` is 1–100000 px. V16 upstream and v26 Windows are accepted; Windows does not claim that its independent v26 additions are readable by upstream v16.

Brush tip, retouch options, clone source slots, removal drafts, cursor and overlay state are not document fields. Serializable tool choices may be written to the separate tool preset JSON; source document references and native bitmap objects are excluded. Notes commit active text before saving or switching documents. Never lower a manifest version by hand to force an older reader to accept unsupported fields.

Version 17 lets an image layer be an animated GIF, through an optional `gif` object (groups, adjustments, text and shapes cannot carry it). `sourceFile` is `<layer UUID>.gif` under `images/`, the original GIF kept unchanged, and `selectedFrameIndex` is the frame shown, whose pixels the layer's ordinary `imageFile` holds; the layer's transform, opacity, effects and mask apply to every frame. Optional fields: `geometry` (Image Size's output `width` and `height` and the affine map `a`, `b`, `c`, `d`, `tx`, `ty` from the source's normalized coordinates to the output's, with optional `sampling`), `framePixels` (edited frames, each `{ "index": 1, "imageFile": "<layer UUID>.frame-1.png" }`; other frames decode from the source), `deletedFrameIndices` (source frames removed), `frameOrder` (frames in playing order, each `{ "index": 4, "sourceIndex": 1 }`; a copied frame takes an index from the source's frame count up, below 8,192) and `timing` (`mode` `Frame delay` or `Same for all`, `uniformDelay` in seconds, and `frameDelays` overrides `{ "index": 4, "duration": 0.16 }`; delays are 0.02–655.35 seconds). A GIF holds at most 4,096 frames. Files declaring 1–16 cannot contain `gif`, and older app builds reject v17.



## Windows extension version 27

Windows 1.5.6.0 writes version 27 when a document contains an animated GIF layer. Older documents continue to use the lowest Windows version required by their fields. The Windows loader also accepts the upstream version-17 GIF representation described above; the other Windows v17 fields keep their existing meanings.

- `gif.sourceFile` is exactly `images/<LAYER-UUID>.gif` relative to the package, with the manifest field containing only `<LAYER-UUID>.gif`. The immutable encoded source is preserved. The ordinary `imageFile` remains a PNG preview of the selected frame.
- `gif.selectedFrameIndex` identifies the frame restored on opening. `frameOrder` contains stable `{index, sourceIndex}` identities; it does not duplicate encoded source data. `deletedFrameIndices` records removed original frames. Duplicate identities occupy 0–8191, with at most 4096 surviving frames.
- `framePixels` contains `{index, imageFile}` entries for edited frames, with filenames `<LAYER-UUID>.frame-N.png`. Each edit matches the source dimensions or `geometry.width` / `geometry.height`. Unedited full-size frames decode on demand. Editing never overwrites the source GIF.
- `geometry` preserves upstream normalized affine fields `a`, `b`, `c`, `d`, `tx`, `ty`, raster dimensions and `sampling`; image resizing composes this geometry and resamples edited frames.
- `timing.mode` is `Frame delay` or `Same for all`. `uniformDelay` and `frameDelays` entries `{index, duration}` are seconds in 0.02–655.35. Export rounds to GIF hundredths, merges visible layer clocks and holds shorter tracks on their final frame. Playback loops tracks without changing editable pixels or undo history.
- Reject invalid paths, duplicate identities, missing source frames, removed-frame references, invalid geometry and nonfinite timing. Source bytes, decoded surfaces and edited-frame pixels remain subject to document/process memory budgets. Atomic staging covers GIF sources and frame PNGs together with the existing manifest assets.

Brush settings and tool presets are stored separately in the user's settings directory, not in `.comp` pixels. Brush presets may carry `Brush`, `CaptureSize`, `IncludeTool`, `IncludeColor` and `Tool`, with the serializable `Painting` object containing dynamics, texture, dual-tip, transfer, pose, symmetry and tool options. Runtime pen samples, native bitmaps and mixer reservoirs are excluded. Presets use Compositor JSON; Adobe ABR/PAT interchange is not implemented.

## Windows extension version 28

Windows 1.5.8.0 writes v28 only when one of these features is used. It continues to read versions 1–28, including upstream v15, v16 and v17. Older Windows/macOS builds do not read the v28 extension.

- A layer's `gradient` stores `startX`, `startY`, `endX`, `endY` in layer pixel coordinates and a `paint` object. `paint` contains `shape` (Linear, Radial, Angle, Reflected, Diamond), `method` (Perceptual, Linear, Classic, Smooth, Stripes), `reverse`, `dither`, and 2–64 sorted `stops`, each containing `position` (0–1) and `color` (unsigned packed ARGB). The cached PNG/deep asset remains the rendering representation; raster edits invalidate live gradient identity. Gradient layers cannot simultaneously be groups, adjustments, live text, live shapes or GIFs.
- Shape styles may include nullable `fillEnabled`, `strokeColor` (packed ARGB), `strokeWidth` (0–1000 pixels; zero disables it) and `strokePattern` (Solid, Dotted, Dashed, Long Dash). Missing fields retain legacy shape rendering.
- Extended adjustments accept `ClarityDehaze`, whose Values dictionary contains `Clarity` and `Dehaze` percentages. The clarity operator samples a halo of 26 pixels; render regions include that margin.

- `imageSettings.colorProfile` stores optional base64 RGB/GRAY ICC bytes (132 bytes–4 MiB, valid declared size and `acsp` header). It describes managed output; cached layer pixels remain normalized sRGB/extended sRGB. CMYK output continues using the existing `cmykProfile` field.
- `imageSettings.ocioConfiguration` and `ocioWorkingSpace` are an optional pair of nonempty strings, shorter than 32768 and 1024 characters respectively. They record the selected config URI/path and space. They never cause stored project pixels to be transformed again on loading; external config/LUT files are not embedded. Application color-engine/policy preferences remain separate from the document.

These fields are rejected when declared in a manifest older than v28. Brush, tool and view preferences are application settings, not project fields. Gradient preset exchange uses a separate JSON library with stable IDs, names, categories and the same stop representation.

## Upstream macOS versions 18–19

Version 18 adds three adjustment kinds from the Filter menu: `Vignette`, `Lens Effects` and `Tonal Contrast`. Each keeps its settings in an object of its own inside the adjustment's `filterLayerSettings`: `vignette` (`amount` 0–100, `color` with `red`, `green`, `blue` 0–1, `midpoint` 0–100, `roundness` −100–100, `feather` 0–100, `highlights` 0–100; it frames the whole canvas and recolors only pixels that are there), `tonal` (`amount` 0–100, `radius` 1–100 document pixels, `shadows`, `midtones`, `highlights` −100–100) and `lens` (`effect`: `Anamorphic Flare`, `Starburst`, `Mist`, `Prism` or `Bokeh`, with that effect's settings: `threshold` 50–100; `flareAmount` 0–100, `flareLength` 1–100 and `flareBlue` 0–100; `starAmount` 0–100, `starLength` 2–40, `starPoints` 4, 6, 8, 12 or 14, `starAngle` −180–180; `mistStrength` and `mistSize` 0–100; `prismAmount` 0–100, `prismCopies` 1–8, `prismOffset` 0–50, `prismAngle` −180–180, `prismBothWays`, `prismBlur` 0–100; `bokehRadius` 0.5–6, `bokehBrightness` and `bokehRing` 0–100, `bokehShape` `Round`, `Hexagon`, `Oval` or `Cat's Eye`). Lengths given as percentages are of the document's longer side. Any `lens` setting may be left out and takes its default. Files declaring 1–17 cannot contain these kinds or objects, and older app builds reject v18.

Version 19 adds the `Sharpen` adjustment kind: an unsharp mask, its settings in a `sharpen` object inside `filterLayerSettings`, with `amount` 0–300 (percent of the detail added back) and `radius` 0.1–20 document pixels. Files declaring 1–18 cannot contain it, and older app builds reject v19.


## Windows extension version 29

Windows **1.5.10.0 Preview** reads versions 1–29 and writes the lowest Windows extension required. The following optional features require v29; earlier Windows builds reject v29. Upstream macOS v18/v19 filter settings are accepted, but the independent Windows v29 extension is not macOS-compatible.

- Document `layerComps` is an array of at most 1000 property snapshots. Each entry has unique `id`, `name` (1–256 characters), `comment` (up to 10000), `capture` flags (Visibility=1, Position=2, Appearance=4), and `layers` (up to 10000 unique layer IDs). Each state holds `layerID`, `visible`, `transform`, `opacity`, `fillOpacity`, `blendMode`, `effects` and `blending`. Applying a comp never replaces assets, order or layer identities. Deleted layers are ignored. Runtime last-document-state and selected comp are not serialized.
- Document `timeline` stores `mode` (Video=0, Frames=1), `frameRate` (1–120), `workStart`, `workEnd`, `loop`, `allowFrameSkipping`, `audioEnabled`, `shortcuts`, `autoGroup`, `onionSkin`, `onionOpacity` (0–1), `thumbnailSize` (0–3), `frameNumbers`, `previewPercent` (25, 50, 100), `displayFilter` (0–2) and `showNotes`. Times are seconds, finite and within 0–86400; work end must exceed start. Rate changes preserve elapsed time.
- Timeline `clips` (up to 10000) hold unique `id`, `layerID`, `start`, `duration` (at least .001), `sourceStart`, `speed` (.01–100), `fadeIn`, `fadeOut`, `favorite`, optional `visibility`, optional `sourceFile`, optional comp `state` and sorted, unique `keys`. Each key holds clip-local `time`, `transform`, `opacity`, and `interpolation` (Linear=0, Hold=1); at most 10000 keys per clip. Fades and key times cannot exceed duration. A saved state must target the same layer.
- Timeline `frames` (up to 4096) hold unique `id`, `duration` (.001–600 seconds), and a property-comp `state`. `notes` (up to 10000) hold unique `id`, `time`, `text` (up to 10000 characters). `audio` (up to 128) holds unique `id`, `file`, `start`, `duration`, `sourceStart`, `volume` (0–4), and `muted`. External paths are at most 32768 characters and cannot contain NUL; they remain links, are not packaged, and loading a project does not execute them. Optional FFmpeg is invoked only for explicit preview/import/render operations. Timeline preview is transient and does not replace editable assets.
- `imageSettings.profileEncoding` is optional base64 ICC data describing the pixel encoding before profile assignment. It uses the same size/signature/model validation as `colorProfile`. Assign Profile changes the interpretation metadata, preserves layer assets, and transforms the final composite for display/output. It is not native-channel ICC blending. Unmanaged assignment clears the output tag; RGB is then interpreted as sRGB while the fixed encoding metadata remains available.
- Text `layout` adds `fractionalWidths` (default true), `noBreak`, `characterAlignment` (0–5), `kinsokuMode` (0–2), `mojikumi` (0–4), `hyphenate`, `hangingPunctuation`, `multiLineComposer`, `wordSpacing` (0–1000, default 100), `letterSpacing` (−100–1000, default 0), and `hyphenMinimum` (2–100, default 6). Other booleans default false. Nondefault extended values on the layer or a style run require v29. These use Compositor/Skia typography and do not imply Adobe layout equivalence.
- A layer style effect may contain `contourSamples`, 2–256 finite samples in [0,1]. The effect retains its own copy independently of preset-library deletion. The shared preset library is user state, with a version-2 envelope for local storage and JSON-array exchange; it is not embedded in the project.
- Adjustment kinds Vignette, Lens Effects, Tonal Contrast and Sharpen persist the upstream nested `filterLayerSettings.vignette`, `.lens`, `.tonal`, `.sharpen` objects described above. Windows writes v29 for these kinds. Pixel caches are regenerated by CPU operators; deep source assets remain stored, while these filters currently composite at 8-bit precision.

Missing fields use prior defaults. Invalid identities, enum values, array limits, timing, profiles or geometry are rejected. These records participate in clone, undo/redo, recovery and atomic project save. Runtime playhead, selected clips, audio-player processes, preview images and open dialogs are not saved. Existing manifest/asset byte budgets also apply, regardless of schema validity.

## Windows extension version 30

Windows 1.5.12.0 supports the optional Prism Fringe control in lens filter settings. The `fringe` value is finite, ranges from 0 to 100, and defaults to 30 when omitted. A nondefault value requires v30. This extends the independent Windows format; it does not change upstream macOS format compatibility.

## Windows extension version 31

Windows **1.5.12.0 Preview** reads versions 1–31 and writes the lowest version required by document contents. Optional document `metadata` requires v31 when nonempty. Its string fields are `title`, `author`, `description`, `copyright`, `contactEmail`, `website`, `keywords`, `cameraMake`, `cameraModel` and `dateTaken`. Missing fields default to empty strings. Each field is limited to 32768 characters, the total to 131072 characters, and values must contain valid XML characters for XMP export. Invalid values or metadata in an older schema are rejected.

Metadata is copied during document cloning, undo/redo, recovery and project save. Assigning metadata does not alter pixel values. Raster import reads the supported XMP fields; it does not preserve arbitrary EXIF or proprietary metadata. Export policies may omit all metadata, keep copyright, keep copyright and contact, keep all supported fields except camera information, or keep all supported fields. Export policy is a workflow setting, not a document color profile.

## Windows extension version 32

Windows **1.5.14.0 Preview** reads versions 1–32 and writes the lowest version required by document contents. A smart-object layer may carry `smartObjectLink` (an absolute path of at most 32768 characters with no control characters) and `smartObjectLinkHash` (the file's SHA-256 as 64 hexadecimal digits). Both are required together, only on a layer that also has `smartObjectFile`, and require v32. The embedded package remains the layer's content, so a project opens and draws when the linked file is missing; Update Modified Content replaces the package when the file's hash differs. Editing the contents inside the app or Embed Linked removes both fields. This extends the independent Windows format; it does not change upstream macOS format compatibility.
