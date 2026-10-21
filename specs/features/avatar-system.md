# Avatar System

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Draft |
| **Date** | 2026-10-19 |
| **Related Specs** | [Agent Profiles](agent-profiles.md), [Desktop DESIGN.md](../architecture/DESIGN.md) |

Purpose: define the shared `@dotcraft/avatar` system: the equipment slots an avatar exposes, the item
registry with rarity and series, zone-based compatibility, deterministic name derivation, size-tier
degradation, the boundaries for item-level visual effects, and the Desktop pet built on top of it
(the dressed default companion, its finds, bag, and exchange). The design catalog in the DotCraft
design repository owns the review surfaces; this spec owns the model that every host renders.

## 1. Scope

- One `Appearance` record shared by Desktop, Universe, the design catalog, and any SDK consumer.
- Five independent equipment slots and the registry of items that fill them.
- Rarity tiers and series as item metadata, including how rarity weights name derivation.
- Zone occupancy as the single compatibility mechanism between items.
- Size tiers that decide which layers and effects render at a given size.
- Item-level visual effects implemented inside the SVG document.

Out of scope:

- Trading between people, limited-time availability, cloud or custom pets, and any server-side
  state. The Desktop pet (section 11) lives entirely in one machine's settings file.
- Per-host localization of item names. The package ships English catalog copy; hosts localize
  when they surface it and pass the localized name as the item swatch's accessible label.

## 2. Principles

- Names are the only identity input for Agent Profile avatars. The same normalized name renders
  the same avatar in every host; renaming changes the avatar. A host that gives some Agents a
  fixed look passes the mascot its own name resolver (section 3).
- The model has one version and no compatibility layer; changing the registry or the draw order
  changes existing name-derived avatars, and consumers derive from the current registry.
- Rarity is metadata and a sampling weight. It never grants behavior and never paints anything onto
  the avatar; rarity color appears only on catalog and settings chrome such as badges and frames.
- Effects belong to items. A skin, a halo, or a glowing blade carries its own effect; the avatar
  has no rarity aura.
- The expression channel is never lost. Brow and rim items stay off the screen. A faceplate may
  cover the screen only by supplying its own four expression layers (neutral, happy, operator,
  sleep) driven by the same expression state, blink, and gaze gestures.
- Paint stays flat and sparse: one silhouette, at most two color planes, and at most one accent.
  Gradients and filters are reserved for paint-replacing skins and glow effects.
- Every item must be recognizable when worn at standard avatar size and must not fight the robot's
  silhouette. Items that fail either test are removed rather than kept for count.

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

`originalAppearance` is `palette: -1`, `baseFace: 0`, and every slot `'none'`. An empty normalized
name derives it. Desktop's default composer mascot renders it unless the person has dressed the
companion (section 11).

The mascot draws a rendered name through a resolver that defaults to `deriveAppearance`; a host may
pass its own. The resolver covers every name the mascot shows, including both sides of a profile
transition, so a host that fixes some Agents' looks keeps them through hand-offs and tints.

Slot IDs are stable kebab-case strings serialized as-is. Slots are independent fields so a host can
override one slot without touching the others.

## 4. Slots

| Slot | Mounts | Notes |
|------|--------|-------|
| `head` | Head top; replaces the antenna and its status light | Hats and novelty objects. |
| `face` | Brow band, a faceplate over the screen, or a rim site | Brow and rim items never paint on the screen. Rim items mount at one of two rim sites (section 6), combine with every hat, and are exclusive with brow items and faceplates because they share the slot. A faceplate replaces the native face with its own expression layers and stays visible at compact size because it defines the head silhouette. |
| `hand` | Screen-left hand | Follows the arm; stows for laptop and question-sign work props. |
| `back` | Behind the body, with an optional front layer under the work props | Wings, capes, packs, rings, orbits, and auras. An orbiting body renders its far half behind and its near half in front, switching layer only where it is clear of the robot silhouette. A creature that holds on to the robot draws its gripping parts in the front layer. |
| `skin` | Body and arm material | Overlay skins keep the palette paint; paint skins replace it. Face marks keep the palette in both kinds. |

Slot precedence for derivation and conflict resolution is `head > face > hand > back > skin`.

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
next to the registry, keyed by `id`.

| Rarity | Chrome color | Intent |
|--------|--------------|--------|
| Common | Slate | Everyday objects; the bulk of every slot. |
| Uncommon | Green | A twist on an everyday object. |
| Rare | Blue | Distinct silhouettes or a small animated detail. |
| Epic | Purple | Glow, motion, or a paint-replacing material. An epic accessory moves visibly when effects are live; a glow pulse alone is not enough. |
| Legendary | Gold | The signature item of a slot, with a signature beat (section 9). |

Series groups the catalog and has no sampling authority.

## 6. Zone Compatibility

Two items conflict when they share at least one zone. Zones are the only compatibility mechanism.

- Brimmed hats occupy `top` and `brow`; novelty head objects occupy `top` only.
- Brow-band items such as goggles occupy `brow`.
- Rim items occupy `rim`. The rim has exactly two mount sites: the chin run below the screen
  (primary) and the screen-right temple (secondary). The side runs merge with the arms and the
  screen-left temple is where held props rise, so neither is a mount site.
- Only face-slot items may declare `rim`, so every hat combines with every rim item. A brow-band
  item may also declare `rim` when part of it runs up the temple.
- Faceplates occupy `screen`, so a hat and a faceplate combine.
- Hand, back, and skin items occupy their own zone and never conflict with other slots.

`conflicts(a, b)` is the only compatibility predicate. `equip(appearance, slot, id)` sets the slot
and clears every other slot whose item conflicts, returning the cleared slots so design tools can
explain the change. `canEquip` and `equip` require the item to belong to the named slot: `canEquip`
answers `false` for a mismatch and `equip` throws.

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

Rarity weights: common 55, uncommon 27, rare 12, epic 5, legendary 1. The tier is drawn before
compatibility is applied; when the drawn tier has no zone-compatible item the slot stays empty, so
rare combinations are never promoted by exclusion.

The hash is FNV-1a with an avalanche finalizer. Batch sample IDs encode `[seed, round, index]` as a
JSON tuple so any sample in a batch can be replayed.

## 8. Size Tiers

| Tier | Size | Renders |
|------|------|---------|
| `compact` | ≤ 20px | Head and back silhouettes, faceplates, and paint skins as static paint. Brow, rim, and hand items, overlay skins, work props, detail layers, and effects are hidden. No motion. |
| `standard` | 21–43px | Every equipped item. Effects show their static frame. |
| `full` | ≥ 44px with motion enabled | Effects animate. |

The avatar exposes `data-size-tier` and `data-effects` (`off`, `static`, `live`) so styles gate
effects without JavaScript. Reduced motion, paused, and offscreen states pause or disable effects
like all other avatar motion.

## 9. Effects

Effects live in the SVG document and are driven by CSS, so they inherit the avatar's pause,
reduced-motion, and motion-off rules. Allowed techniques are animated gradient stops, transforms on
item groups with a declared pivot, opacity keyframes, motion paths, and static SVG filters. Filters
are never animated or placed under an animated transform. No WebGL, canvas, SMIL, or per-frame
JavaScript. The decoration event clock continues to own head-item responses to done, greeting,
acknowledge, and blocked.

### 9.1 Signature beats

A legendary accessory performs a short signature beat on top of its ambient loop, repeating on a
fixed cycle; an epic accessory may carry a smaller periodic moment.

- Each slot has its own cycle length so a fully legendary avatar seldom fires two beats together.
- A beat starts and ends at the resting pose, and beat-only parts are hidden by default, so
  standard and compact sizes show the item at rest.
- A legendary held item may tint the face marks for the peak of its beat. Faceplates keep their own
  light, and the composer's reasoning energy takes priority over the tint.

### 9.2 Reasoning energy on paint skins

The composer mascot expresses reasoning effort and speed through attributes on its root. The default
body reacts with its gradient and palette-accent glow. A paint skin replaces that gradient, so it
owns its reaction:

- Each paint skin declares a material accent and shadow. `mascotPaletteOf(appearance)` substitutes
  them for the palette values, so glow and profile-transition accents follow the material while
  face marks keep the palette.
- The paint surface carries an energy wash in the material accent whose intensity rises with effort.
- Flowing materials speed up with effort rather than running a fixed loop. A signature material may
  switch to a hotter loop at high effort.
- A paint's hottest or lightest tone stays clearly off-white so the body never merges with the
  outline.

All of this is gated on `data-effects="live"`.

## 10. Paint Contract For New Items

- Recognizable objects with natural colors, rounded silhouettes, and a white outer contour. A
  creature is one silhouette; only limbs and props in front of it keep their own outline.
- Every item visibly attaches to the robot: resting on the top edge, in the brow band or on the rim,
  held at the hand, emerging from behind the body at a plausible anchor, or tied by a visible tether.
  Nothing floats beside the body. Objects made for people are adapted to the robot's geometry.
- Detail layers hide at compact size; the silhouette always renders.
- Head items rest on the body's upper edge and stay clear of both arms.
- Brow items stay inside the brow band and never enter the screen. Only objects with a strong
  silhouette such as goggles qualify.
- Rim items mount at one of the two rim sites and must still read at standard size, usually by
  overhanging the outline. Temple items stow in poses where an arm or prop crosses the site.
- Faceplates fill the screen inside a white frame and draw four expression variants. Cutouts and
  lenses never morph: armored plates express state through light over fixed slits; LED visors move
  straight or gently curved lids; segmented displays change only which fixed cells are lit.
- Palette paint and every skin use one material field across the torso and moving arms, composed
  once under a shared clip, so no seam or double pattern appears. Material stays fixed relative to
  the body while arms move through it. The antenna keeps its own paint and face marks keep the
  palette.
- Hand items meet the left hand at its resting tip and keep their marks upright at rest.
- Back items stay behind the body and may extend past its bounds; hosts reserve headroom.
- Overlay skins use translucent white or the palette shadow as ink so every palette reads through.
  Because the screen covers most of the body, overlay features are large planes or wide bands.

## 11. Desktop Pet

The default companion (the mascot in the Desktop composer, which can be detached onto the desktop)
can be coloured and dressed with items from this collection. Agent Profile mascots keep their
name-derived look; only the default companion is dressed. Desktop editing surfaces follow
[Desktop DESIGN](../architecture/DESIGN.md#pet). The composer mascot and the detached pet carry the
same paint variables (`mascotEnergyStyle`), so reasoning-effort energy recolours the body and face
marks from the companion's palette on both surfaces.

### 11.1 Settings Record

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

The record is Desktop-local personal state stored under `pet` in `settings.json`. It is normalized
on load and save: invalid fields fall back to defaults, counts are clamped to positive integers,
unknown keys are dropped, and a record equal to the defaults is not written. Unknown item ids are
dropped and the outfit is re-equipped through `equip`, so a persisted outfit never carries a zone
conflict.

### 11.2 Appearance Source

`appearanceOf(pet)` is the single derivation: customization off yields `originalAppearance`; on
yields `{ version: 1, palette, baseFace: 0, ...outfit }`. The composer mascot uses it only while no
profile name is rendered. The detached pet receives it too, except while the mascot represents a
named profile, when the pet derives from the name. The energy and glow palette follows the same
record through `mascotPaletteOf`.

Only items in the bag can be worn. Wearing follows `equip`; taking an item off leaves it in the bag.

### 11.3 Finds

A find is one item from the whole registry, drawn by rarity with the weights in section 7 and then
uniformly within the rarity. Two gates must both pass before the next find:

| Gate | Value | Counting |
|------|-------|----------|
| Runtime | 30 minutes | Time the app has been running since the last find; sleep and suspend do not count. |
| Tokens | 1 000 000 | Input plus output tokens from every `item/usage/delta` the main window receives since the last find, across all threads and workspaces. |

When both gates pass, the item is added to the bag, both counters reset, `found` increments, `last`
records the item, and the record persists so a restart continues where it left off. Customization
off pauses both counters. Timing, odds, and progress are neither settings nor shown in settings.

The find is announced with a single replaceable toast showing the item, its rarity, and an action
that wears it.

### 11.4 Bag and Exchange

- The bag lists every owned item with its copy count, filterable by all, wearing, or slot. Hovering
  an item tries it on; selecting wears it, or takes it off when worn.
- The exchange trades exactly ten spare items of one rarity for one random item of the next rarity.
  A worn copy is never a spare, and legendary items cannot be traded up. The person picks the items
  from the rarity's spares, one copy per selection, or lets a fill action pick duplicates first, and
  may remove any before trading. When a trade spends the last copy of a worn item, it comes off.
- A trade's result is announced the way a find is: the same toast with the item, its rarity, and an
  action that wears it.
- Hovering or focusing any item, in the bag, the spares, or the tray, shows its full name, rarity,
  slot, and feature.
