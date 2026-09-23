# Avatar System

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Draft |
| **Date** | 2026-09-23 |
| **Related Specs** | [Agent Profiles](agent-profiles.md), [Desktop DESIGN.md](../architecture/DESIGN.md), [Desktop Client](../clients/desktop-client.md), [TypeScript SDK](../sdk/typescript.md) |

Purpose: define the shared `@dotcraft/avatar` system. It covers the equipment slots an avatar
exposes, the item registry with rarity and series, zone-based compatibility, the deterministic
name derivation, size-tier degradation, the boundaries for item-level visual effects, and the
Desktop pet built on top of it: the dressed default companion, its finds, bag, and exchange. The
design catalog in the DotCraft design repository owns the review surfaces; this spec owns the model
that every host renders.

---

## 1. Scope

- One `Appearance` record shared by Desktop, Universe, the design catalog, and any SDK consumer.
- Five independent equipment slots and the registry of items that fill them.
- Rarity tiers and series as item metadata, including how rarity weights name derivation.
- Zone occupancy as the single compatibility mechanism between items.
- Size tiers that decide which layers and effects render at a given pixel size.
- Item-level visual effects implemented inside the SVG document.

Out of scope for this version:

- Trading between people, limited-time availability, cloud or custom pets, and any server-side
  state. The Desktop pet in section 12 lives entirely in the settings file of one machine.
- Per-host localization of item names. The package ships English catalog copy; hosts localize
  when they surface it in product UI.

---

## 2. Principles

- Names are the only identity input for Agent Profile avatars. The same normalized name renders
  the same avatar in every host; renaming changes the avatar.
- The model is a development-stage contract. It has one version and no compatibility layer;
  changing the registry or the draw order changes existing name-derived avatars, and the frozen
  fixtures in the package tests are updated with it.
- Rarity is a metadata and sampling concept. It never grants functional behavior, and it never
  paints anything onto the avatar by itself. Rarity color appears only on catalog and settings
  chrome such as badges and frames.
- Effects belong to items. A skin, a halo, or a glowing blade carries its own effect; the avatar
  has no rarity aura.
- The expression channel is never lost. Brow and rim items stay off the white screen. A faceplate
  may cover the screen only by replacing the native face with its own four expression layers
  (neutral, happy, operator, sleep) that the rig toggles through the same `data-expression` state
  and that blink and gaze gestures continue to drive.
- Paint stays flat and sparse. An item is one silhouette, at most two color planes, and at most
  one accent; line work, seams, rivets, and texture are omitted so items match the paper-cut
  mascot. Gradients and filters are reserved for paint-replacing skins and for glow effects.
- Every item must be recognizable when worn at 64px and must not fight the robot's silhouette.
  Items that fail either test are removed rather than kept for count.

---

## 3. Appearance Record

```ts
interface Appearance {
  version: 1
  palette: number        // 0–11 role palettes; -1 is the original brand mascot
  baseFace: number       // 0–4
  head: HeadId | 'none'
  face: FaceId | 'none'
  hand: HandId | 'none'
  back: BackId | 'none'
  skin: SkinId | 'none'
}
```

`originalAppearance` is `palette: -1`, `baseFace: 0`, and every slot `'none'`. An empty
normalized name derives it; Desktop's default composer mascot renders it unless the person has
dressed the companion (section 12), in which case `ComposerMascot` receives the equipped record as
`appearance`.

Slot IDs are stable kebab-case strings and are serialized as-is. Slots are independent fields so a
host can override one slot without touching the others.

---

## 4. Slots

| Slot | Mounts | Rig layer | Notes |
|------|--------|-----------|-------|
| `head` | Head top | Replaces the antenna and its status light | Existing hats and novelty objects live here. |
| `face` | Brow band, a faceplate over the screen, or a rim site | Brow and rim items sit in front of the body behind the face marks; a faceplate replaces the face marks | Brow and rim items never paint on the white screen. Rim items sit on the blue rim outside the screen at one of two mount sites (section 6), combine with every hat, and are exclusive with brow items and faceplates because they share the face slot. A faceplate carries its own expression layers and stays visible at compact size because it defines the head silhouette. |
| `hand` | Screen-left hand | Inside the left arm group | Shares the arm pivot; stows for laptop and question-sign work props. |
| `back` | Behind the body | First layer inside the rig, plus an optional front layer drawn over the face and under the work props | Wings, capes, packs, rings, orbits, and auras. An orbit around the body, with or without a visible ring, renders its far half behind and its near half in front; bodies on the orbit exist in both layers and the shared phase animation shows the matching copy. Flat rings such as the halo stay behind. |
| `skin` | Body and arm material | One material layer spans the torso and independently moving arms, under the screen and held props | Overlay skins retain the palette paint; paint skins replace it. Face marks keep the palette in both kinds. |

Slot precedence for derivation and conflict resolution is `head > face > hand > back > skin`.

---

## 5. Item Registry

Every item declares:

| Field | Meaning |
|-------|---------|
| `id` | Stable identifier. |
| `slot` | One of the five slots. |
| `rarity` | `common`, `uncommon`, `rare`, `epic`, or `legendary`. |
| `series` | Thematic family: `everyday`, `dev`, `tech`, `fantasy`, `nature`, `snack`, or `critters`. |
| `zones` | Regions the item occupies, from `top`, `brow`, `rim`, `screen`, `hand`, `back`, `body`. |

Presentation metadata (display name, defining feature, object palette, mounting explanation) lives
next to the registry and is keyed by `id`.

Rarity meaning:

| Rarity | Chrome color | Intent |
|--------|--------------|--------|
| Common | Slate | Everyday objects; the bulk of every slot. |
| Uncommon | Green | A twist on an everyday object. |
| Rare | Blue | Distinct silhouettes or a small animated detail. |
| Epic | Purple | Glow, motion, or a paint-replacing material. |
| Legendary | Gold | The signature item of a slot; effects are expected. |

Series does not influence sampling. It groups the catalog and is the hook for phase-two event
drops.

---

## 6. Zone Compatibility

Two items conflict when they share at least one zone. Zones replace per-item allow-lists.

- Brimmed hats occupy `top` and `brow`; novelty head objects occupy `top` only.
- Brow-band items such as goggles occupy `brow`.
- Rim items occupy `rim` only. The zone has exactly two mount sites on the blue rim: the chin run
  below the screen (primary) and the screen-right temple (secondary); section 10 gives their
  bounds. The side runs are not mount sites because they merge with the arm material, and the
  screen-left temple is where held props rise (staff orb, blade tip, magnifier lens).
- Only face-slot items may declare `rim`. Head items never do, so every hat combines with every
  rim item. A brow-band item may also declare `rim` when part of it runs up the temple, such as a
  snorkel mask's tube.
- Faceplates occupy `screen`, so a hat and a faceplate combine.
- Hand, back, and skin items occupy their own zone and never conflict with other slots.

`conflicts(a, b)` is the only compatibility predicate. `equip(appearance, slot, id)` sets the slot
and clears every other slot whose item conflicts with the new one, returning the cleared slots so
design tools can explain the change. Both `canEquip` and `equip` require the item to belong to the
named slot: `canEquip` answers `false` for a mismatch and `equip` throws, because a hand item stored
in the head slot would render as an empty slot without any signal.

---

## 7. Name Derivation

```text
seed = NFC(trim(name)); empty seed → originalAppearance
draw(dimension) = hash(JSON(['dotcraft-avatar', 1, seed, dimension])) / 2^32
palette  = floor(draw('palette') × 12)
baseFace = floor(draw('face') × 5)
occupied = ∅
for slot in [head, face, hand, back, skin]:
  if draw(slot + '-presence') ≥ presence[slot]: continue
  tiers  = rarities that have at least one item registered in this slot
  rarity = weighted pick over tiers with draw(slot + '-rarity')
  pool = items in slot of that rarity whose zones ∩ occupied = ∅
  if pool is empty: continue
  item = uniform pick over pool with draw(slot + '-item')
  occupied ∪= item.zones
```

Presence per slot: head 0.85, face 0.35, hand 0.40, back 0.25, skin 0.30.

Rarity weights: common 55, uncommon 27, rare 12, epic 5, legendary 1. The tier is drawn over the
weights of every tier that has an item registered in the slot, before compatibility is applied;
when the drawn tier has no zone-compatible item the slot stays empty. Rare combinations therefore stay rare instead of being promoted by exclusion
(a brimmed hat blocks brow items, so a common or uncommon face draw can find only rim items; when
that tier has none, the face slot stays empty instead of falling through to a faceplate).

The hash is FNV-1a with an avalanche finalizer. Batch sample IDs encode `[seed, round, index]` as
a JSON tuple so a wall of 100 samples is reproducible from its seed and any cell can be replayed.

---

## 8. Size Tiers

| Tier | Size | Renders |
|------|------|---------|
| `compact` | ≤ 20px | Head silhouette, back silhouette, faceplates, paint skins as static paint. Brow and rim items, hand items, overlay skins, and work props are not mounted; detail layers and every effect stay in the document and are hidden by the `data-compact` and `data-effects="off"` gates. Motion is off. |
| `standard` | 21–43px | Every equipped item. Effects render their static frame; effect animations do not run. |
| `full` | ≥ 44px with motion enabled | Effects animate. |

The avatar exposes `data-size-tier` and `data-effects` (`off`, `static`, `live`) so styles gate
effect animations without JavaScript. Reduced motion, `paused`, and offscreen states continue to
pause or disable animation exactly as before; effects follow the same attributes.

---

## 9. Effects

Effects are implemented in the SVG document and driven by CSS so they inherit the existing pause,
reduced-motion, and motion-off rules:

- Animated gradient stops on a skin's paint for flowing color (holographic, energy).
- CSS transforms on overlay groups clipped to the torso-and-arm silhouette for sheen sweeps.
- CSS transforms on item groups with a declared pivot for wing flaps, cape sway, and a blade that
  extends and retracts from its emitter; animated `fill` for a blade's color cycle.
- Opacity keyframes for glow pulses, flames, blinking nodes, and steam.
- Static SVG filters (`feGaussianBlur`) for glow halos. Filters are never animated.
- Opacity phase loops that swap the behind-body and in-front copies of an orbiting body at the
  half-orbit boundary.

No WebGL, canvas, SMIL, or per-frame JavaScript. The decoration event clock (lift, bounce, rock)
continues to own head-item responses to done, greeting, acknowledge, and blocked.

### Reasoning energy on paint skins

The composer mascot expresses reasoning effort, speed, and context mode through `data-mascot-*`
attributes on its root. The default body reacts by animating its gradient stops and glowing with
the palette accent. A paint skin replaces that gradient, so it owns its own reaction:

- Each paint skin declares a material `accent` and `shadow` in `paintMaterials`.
  `mascotPaletteOf(appearance)` substitutes them for the palette values, so the glow, fast echo,
  and profile-transition accent follow the material (gold glows warm, lava orange, galaxy violet)
  while face marks keep the palette.
- The paint surface carries an energy wash: a silhouette-clipped field in the material accent whose
  opacity pulses at medium (0.16), high (0.26), extraHigh (0.38), and context max (0.30), on the
  same periods as the default body animation.
- Flowing materials speed up with effort instead of running a fixed loop: the animated stops,
  sheens, scan lines, and blinking nodes of every paint skin, and the turning group of an overlay
  skin, shorten their periods as effort rises. A paint's hottest or lightest stop stays clearly
  off-white so the body never merges with the outline.
- A signature material, such as a thermal paint that runs cool at idle, may swap its idle loop for
  a second, hotter keyframe set at high and extraHigh effort and at context max. Changing keyframes
  restarts the loop, so the swap shows as a cut rather than a speed-up.

All of this is gated on `data-effects="live"`, so compact and standard sizes and motion-off hosts
show the same static paint as before.

---

## 10. Paint Contract For New Items

- Recognizable objects with natural colors, rounded silhouettes, and a white outer contour.
- Every item attaches to the robot. It rests on the top edge, sits in the brow band or on the rim,
  is held at the hand, emerges from behind the body silhouette at a plausible anchor (the arm roots
  are its shoulders, the lower white edge its base, the antenna its crown), or is tied to it by a
  visible tether such as a string, stem, strut, or beam. Nothing floats beside the body with a gap
  and no link. Objects made for people are adapted to this geometry rather than placed where a
  person would wear them.
- A `Silhouette` path carries the outline; `Detail` groups hide at compact size.
- Head items rest on the body's upper white edge and stay clear of both arms.
- Brow items stay inside the brow band (view-box y 408–464) and never enter the screen rectangle
  (x 295–729, y 464–779). The band is thin, so only objects with a strong silhouette such as
  goggles qualify; abstract bands and small strips do not read and are not added.
- Rim items mount at one of the two `rim` sites.
  - Chin site: x 400–624, y 784–846. Items may overhang the white outline below the rim. The
    orbit ring's front belt and the working-pose laptop draw in front of the site, so a chin item
    passes under them the way held props stow for the laptop.
  - Temple site: x 746–850, y 362–468, clear of the brow band, the widest hat brim, and the sign
    pole. Temple items stow in the hold-sign pose, where the sign and its arm cross the site; in
    the celebrate pose they draw over the raised arm like other accessories.
  - The chin run is 55 units tall, about 4.5px at 64px, so rim items carry their silhouette by
    overhanging and must still read at 64px. Rim items need a wider swatch viewBox than brow
    items.
- Faceplates fill the screen inside a 12-unit white frame and draw four eye variants with the
  shared face-layer classes. Following rigid-robot references (Iron Man, EVE, Cozmo), cutouts and
  lenses never morph: an armored plate expresses state through light (tint, intensity, a core
  line, a scan line, a breathing ember) over fixed slits; an LED visor uses a pair of thick eyes
  whose lids move on straight or gently curved edges (raised lower lid for happy, lowered upper lid
  for operator, dropped and closed to a line for sleep). Curved "^^" eye arcs on metal are not
  allowed. A segmented display with LCD or LED digits expresses state only by which of its fixed
  cells are lit, never by moving cells.
- Palette paint and every skin use one material field in the body's coordinate space. The torso
  and moving arm shapes form a union clip; the paint, overlay, effects, and inner shadow are
  composed once, so overlapping parts cannot introduce a color seam or double-painted pattern.
- Arm outlines and clip shapes retain the existing geometry, pivots, and CSS motion. Held items
  follow the same arm motion above the material; the screen, face, and work props keep their layers.
- Material stays fixed relative to the body while arms move through it. The accepted tradeoff is
  texture sliding across a moving arm. Do not rotate a second material with an arm or substitute a
  solid hinge color. The antenna retains its own paint and the face marks retain the palette.
- Hand items meet the left hand at its resting tip and keep their marks upright at rest.
- Back items stay behind the body and may extend past the body bounds; the avatar canvas is
  `overflow: visible` and hosts reserve headroom already.
- Overlay skins use translucent white or the palette shadow (`--dca-part-shadow-color`) as ink so
  every palette reads through. All skin layers, including energy washes, must cover the moving
  silhouette's bounds before the common clip is applied. New skins inherit this composition
  without per-skin shoulder corrections.
- The screen covers most of the body, so an overlay shows only on the rim (52–56 units wide) and
  the arms. Overlay features are therefore large planes or wide bands at least 50 units across,
  such as a half-body split, hoops, radial wedges, or a dipped lower body. Small motifs (dots,
  checks, hearts, camo, circuit traces, stars) were tried, read as nothing once equipped, and are
  not re-added.

---

## 11. Acceptance Checklist

- `deriveAppearance` is deterministic, normalizes names, and returns `originalAppearance` for empty
  names.
- Every derived appearance has no zone conflicts across its five slots.
- Over a large sample every slot's rarity distribution follows the weights within tolerance, and
  every item in the registry is reachable by name.
- Every item renders as a swatch and mounted on the rig in every pose; the arm geometry never
  changes.
- Compact renders omit brow/rim, hand, and overlay-skin markup; faceplates remain. Detail layers and
  effects stay in the document and are hidden through `data-compact` and `data-effects="off"`, the
  same CSS gates every other tier uses, so no slot needs a second compact rendering path.
- Rim items render at standard and full size, are omitted at compact size like brow items, and are
  allowed with every brimmed hat.
- Faceplates render all four expression layers and no native face marks.
- Paint skins keep the palette on the face marks, keep their material on raised arms, and overlay
  skins keep the palette body paint.
- All skins remain continuous at both shoulders at rest, during a full wave, celebration, laptop
  and sign poses, including pause/resume and animated material/energy phases.
- Every new item is checked worn at 64px on a design-catalog proof sheet and passes the
  recognizability test in section 2 before it is registered.
- The frozen fixtures are updated once per registry change.
- Desktop and Universe compile against the package without local artwork or model copies.

---

## 12. Desktop Pet

The default companion, the mascot that lives in the Desktop composer and can be detached onto the
desktop, can be coloured and dressed with items from this collection. Agent Profile mascots keep
their name-derived look; only the default companion is dressed. The Desktop surfaces that edit it
are described in [Desktop Client §6.13](../clients/desktop-client.md#613-desktop-pet) and drawn
under [Desktop DESIGN.md](../architecture/DESIGN.md#pet).

### 12.1 Settings Record

```ts
interface PetSettings {
  customization: boolean          // default true
  palette: number                 // -1 original brand paint, 0–11 role palettes; default -1
  outfit: Record<Slot, ItemId | 'none'>
  bag: Record<ItemId, number>     // copies owned, including the worn one
  drops: {
    runtimeMs: number             // app runtime since the last find
    tokens: number                // tokens agents used since the last find
    found: number                 // finds so far
    last: ItemId | null
  }
}
```

The record is Desktop-local personal state stored under `pet` in `settings.json`. Main normalizes
the shape on load and save with `resolvePetSettings`: invalid fields fall back to their defaults,
counts are clamped to positive integers, unknown keys are dropped, and a record equal to the
defaults is not written. The renderer drops ids the registry does not know and re-equips the outfit
through `equip` so a persisted outfit never carries a zone conflict.

### 12.2 Appearance Source

`appearanceOf(pet)` is the single derivation: customization off yields `originalAppearance`; on
yields `{ version: 1, palette, baseFace: 0, ...outfit }`. The composer mascot receives it as the
`appearance` prop of `ComposerMascot`, which the package uses only while no profile name is
rendered. The detached pet receives it in its snapshot; the owner window omits it while the mascot
represents a named profile, and the pet window then derives from the name as before. The paint
palette behind the composer's energy and glow effects follows the same record through
`mascotPaletteOf`.

Only items in the bag can be worn. Wearing follows `equip` and clears any slot whose item shares a
zone; taking an item off leaves it in the bag.

### 12.3 Finds

A find is one item from the whole registry, drawn by rarity with the weights in section 7 and then
uniformly within the rarity. Two gates must both pass before the next find:

| Gate | Value | Counting |
|------|-------|----------|
| Runtime | 30 minutes | Wall-clock time the app has been running since the last find, advanced by the main window in one-minute steps; a step never adds more than its own interval, so sleep and suspend do not count. |
| Tokens | 1 000 000 | Input plus output tokens from every `item/usage/delta` the main window receives since the last find, across all threads and workspaces. |

When both gates pass the find happens on its own: the item is added to the bag, both counters reset
to zero, `found` increments, `last` records the item, and the record persists so a restart
continues where it left off. Customization off pauses both counters. Nothing about timing, odds, or
progress is a setting or is shown in settings.

The find is announced through the Desktop toast stack with key `pet-find`, so a second find
replaces the first card: message `New find: <item>`, description the rarity label, the item's art in
the card's art slot, and one inline action, `Wear it`, which equips the item.

### 12.4 Bag and Exchange

- The bag lists every owned item with its copy count, filtered by all, wearing, or a slot. Hovering
  a tile tries the item on the companion; selecting wears it or, when it is worn, takes it off.
- The exchange trades exactly ten spare items of one rarity for one random item of the next rarity.
  A worn copy is never a spare, legendary items cannot be traded up, and a fill action picks
  duplicates first so single finds stay in the bag when possible; the person may remove items from
  the tray before trading. When a trade spends the last copy of a worn item, the item comes off.

### 12.5 Acceptance Checklist

- `resolvePetSettings` returns the defaults for missing or corrupt input, clamps the palette and
  counts, and drops unknown fields; the renderer drops unknown item ids before rendering or
  persisting.
- The composer mascot and the detached pet render the same appearance for the default companion and
  keep name-derived appearances for Agent Profiles.
- A find needs both gates, resets both counters, adds exactly one item, and survives a restart.
- The exchange consumes exactly ten spares of one rarity, never the worn copy, and yields the next
  rarity.
- Turning customization off restores the original paint and pauses finds without touching the bag.
