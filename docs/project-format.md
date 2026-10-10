# Compositor project format, versions 1–11 and Windows extensions 12–15

A `.comp` file is a macOS document package containing `manifest.json` and an `images/` directory of `<layer UUID>.png` assets.

The manifest identifies `com.compositor.project`, version `11` for new saves (versions `1`–`10` remain readable), and the sRGB working space. It stores document UUID, pixel dimensions, active layer UUID, and layers in bottom-to-top order. Each layer stores its UUID, name, visibility, transform (origin, size, clockwise rotation, flips, sampling), and optional image filename. Blank layers have no image asset.

Embedded PNGs preserve source pixels and transparency; transforms remain separate. Projects survive moving or deleting imported source photos. Saving uses a coordinated atomic package replacement. Unsupported versions, invalid metadata, missing assets, unsafe paths, and oversized data are rejected before replacing the live document.

Limits: 30,000 pixels per canvas/image side, 100 million total source pixels, 10,000 layers, 4 MiB manifest, 512 MiB per encoded asset. See `ProjectStore.swift` for validation.

Undo history and viewport are session-only. Opening fits the canvas, restores selection, and starts with clean history. Future editable features must extend the schema and round-trip tests. PNG export is a flattened derivative and does not mark project edits saved.

Image Size adds optional `resolution` (pixels/inch, 1–9600). Older manifests without it default to 72. This additive field retains version 1 compatibility. Both PNG and JPEG exports include document resolution metadata. Resampling stores the new layer pixels and bounds; undo retains the prior sources only during the current session.

Version 2 adds optional `parentID` and `isGroup` on layer records. A group has no image file. Root nodes have no parent; children refer to an existing group. Array order defines bottom-to-top sibling order; renderers traverse each group as a contiguous subtree. Visibility is inherited without changing child flags. Cycles, missing/non-group parents, image-bearing groups, and nesting beyond 64 ancestor levels are rejected. Group ancestors permit room for leaf nodes at the deepest level. Group metadata survives image/canvas resizing and cropping. Older app builds reject version 2 rather than misrender grouped documents. Collapse state is not serialized.

Version 3 adds optional per-layer `opacity` (finite 0–1) and `blendMode` (Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference, Color Dodge, Color Burn). Missing fields default to full opacity and Normal. Group records required those defaults until version 8, which lets a folder carry its own opacity; a folder's opacity multiplies into every layer inside it, while its blend mode stays Normal because folders are pass-through. Effects are applied during compositing and retained as metadata when resizing sources. Files declaring older versions cannot contain non-default appearance values.

Version 4 adds optional `maskFile` and `maskEnabled` fields to individual layers. Mask filenames must be `<layer UUID>.mask.png` under `images/`; enabled defaults to true when a mask exists. Records without masks omit both fields. Groups cannot carry masks in this version. Files declaring versions 1–3 cannot contain mask metadata.

Masks store 8-bit grayscale coverage without alpha (white reveals, black hides). Their normalized extent matches the image’s local rectangle, so the same layer transform applies to both. A uniform 1×1 mask is valid and avoids allocating full-resolution pixels before painting. Nonuniform mask pixels and a thumbnail are immutable assets shared by history. Image Size resamples them with the image transform; Canvas Size and Crop preserve their pixels. Up to 100 million mask pixels may be stored in addition to the existing 100 million image pixels; per-side and per-file limits also apply to masks. Disabled masks remain embedded and editable but do not affect compositing. Image-versus-mask target selection is session-only and reopens on image pixels.

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
- `shape`: a layer made with the Shape tool keeps its style (`kind`, `red`/`green`/`blue`, `cornerRadius` in document pixels, and for lines `lineWidth` plus `start` and `end` as fractions of the layer box) so it redraws cleanly when scaled. Its PNG is still an ordinary raster; once anything else changes those pixels the metadata is dropped.

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
