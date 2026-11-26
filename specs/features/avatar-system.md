# Avatar System

| Field | Value |
|-------|-------|
| **Version** | 0.14.0 |
| **Status** | Draft |
| **Date** | 2026-11-25 |
| **Related Specs** | [Agent Profiles](agent-profiles.md), [Desktop DESIGN.md](../architecture/DESIGN.md) |

Purpose: define the shared `@dotcraft/avatar` system: the equipment slots an avatar exposes, the item
registry with rarity and series, external extension packs and outfit presets, zone-based compatibility,
deterministic name derivation, size-tier degradation, the boundaries for item-level visual effects,
and the Desktop pet built on top of it (the dressed default companion, its finds, bag, and exchange).
The design catalog in the DotCraft design repository owns the review surfaces, and this spec owns the
model that every host renders.

## 1. Scope

- One `Appearance` record shared by Desktop, Universe, the design catalog, and any SDK consumer.
- Five independent equipment slots, the built-in registry of items that fill them, and extension packs
  supplied by external hosts.
- Outfit presets that apply coordinated multi-slot looks without replacing per-slot composition.
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
type ExternalItemId = `${string}:${string}`

interface Appearance {
  version: 1
  palette: number        // 0–11 role palettes, -1 original brand, -2 ink, <= -3 pack palettes
  baseFace: number       // 0–4
  head: HeadId | ExternalItemId | 'none'
  face: FaceId | ExternalItemId | 'none'
  hand: HandId | ExternalItemId | 'none'
  back: BackId | ExternalItemId | 'none'
  skin: SkinId | ExternalItemId | 'none'
  antenna?: 'ball' | 'star'
}
```

`originalAppearance` is `palette: -1`, `baseFace: 0`, and every slot `'none'`. An empty normalized
name derives it. Desktop's default composer mascot renders it unless the person has dressed the
companion (section 11).

Ink paint (`palette: -2`), pack-registered negative palettes (`palette <= -3`), and the star antenna
(`antenna: 'star'`) exist for hosts that fix a brand or character look, such as Universe's own Agent.
Name derivation never produces them, so no derived robot can wear a host's brand. Ink is a near-black
gradient with ink face marks. The star antenna is a four-point star whose tips lie on a radius-84
circle and whose edges are radius-168 arcs: it floats 35 above a shortened neck inside the same white
outline, and takes the status light's colours. A head item still replaces whichever antenna is set.

The mascot draws a rendered name through a resolver that defaults to `deriveAppearance`, and a host may
pass its own. The resolver covers every name the mascot shows, including both sides of a profile
transition, so a host that fixes some Agents' looks keeps them through hand-offs and tints.

Built-in slot IDs are kebab-case strings serialized as-is. External extension items use
`<namespace>:<item-id>` so host packs never collide with built-in items or with each other. Slots are
independent fields so a host can override one slot without touching the others. When an `Appearance`
names an item missing from the active registry, rendering treats that slot as `'none'` without failing.

## 4. Slots

| Slot | Mounts | Notes |
|------|--------|-------|
| `head` | Head top (replacing the antenna and its status light), with an optional behind-body drape layer | Hats, novelty objects, hair silhouettes, and costume hoods. The optional behind-body layer renders in front of `back` items and behind the body outline so twin-tails, long hair, and hood collars frame the silhouette without covering moving arms or consuming `back`. |
| `face` | Brow band, a faceplate over the screen, or a rim site | Brow and rim items never paint on the screen. Rim items mount at one of two rim sites (section 6), combine with every hat, and are exclusive with brow items and faceplates because they share the slot. A faceplate replaces the native face with its own expression layers and stays visible at compact size because it defines the head silhouette. |
| `hand` | Screen-left hand | Follows the arm and stows for laptop and question-sign work props. A conducting item stays in hand while working (section 8.1). |
| `back` | Behind the body, with an optional front layer under the work props | Wings, capes, packs, ribbons, rings, orbits, and auras. An orbiting body renders its far half behind and its near half in front, switching layer only where it is clear of the robot silhouette. A creature that holds on to the robot draws its gripping parts in the front layer. |
| `skin` | Body and arm material | Overlay skins keep the palette paint, whereas paint skins replace it. Face marks keep the palette in both kinds. |

Slot precedence for derivation and conflict resolution is `head > face > hand > back > skin`.

## 5. Item Registry

Every item declares:

| Field | Meaning |
|-------|---------|
| `id` | Stable identifier (`<item>` for built-in items, `<namespace>:<item>` for pack items). |
| `slot` | One of the five slots. |
| `rarity` | `common`, `uncommon`, `rare`, `epic`, or `legendary`. |
| `series` | Thematic family: built-in `everyday`, `dev`, `tech`, `fantasy`, `nature`, `snack`, `critters`, or `costume`, or a pack-qualified `<namespace>:<series>`. |
| `zones` | Regions the item occupies, from `top`, `brow`, `rim`, `screen`, `hand`, `back`, `body`. |

Presentation metadata (display name, defining feature, object palette, mounting explanation) lives
next to the registry, keyed by `id`.

| Rarity | Chrome color | Intent |
|--------|--------------|--------|
| Common | Slate | Everyday objects, forming the bulk of every slot. |
| Uncommon | Green | A twist on an everyday object. |
| Rare | Blue | Distinct silhouettes or a small animated detail. |
| Epic | Purple | Glow, motion, or a paint-replacing material. An epic accessory moves visibly when effects are live, as a glow pulse alone is not enough. |
| Legendary | Gold | The signature item of a slot, with a signature beat (section 9). |

Series groups the catalog and has no sampling authority.

### 5.1 Extension Packs, Registries, and Outfit Presets

External applications extend `@dotcraft/avatar` without forking the package by composing an
`AvatarRegistry` (pure data in `@dotcraft/avatar`) and an `AvatarCatalog` (SVG renderers in
`@dotcraft/avatar/react`) from the built-in collection and one or more `AvatarPack` definitions:

- Each pack declares a kebab-case `namespace`, a display label, its slot items (every `id` prefixed
  with `<namespace>:`), optional custom series labels, optional custom negative palettes
  (`palette <= -3`), and optional `AvatarOutfitPreset` entries.
- Pack validation rejects duplicate IDs, unprefixed item IDs, invalid slot-to-zone assignments
  (section 6), and positive or reserved palette indices (`>= -2`).
- Pack items default to `derivable: false`, keeping deterministic name derivation identical to the
  built-in collection unless a pack or item explicitly opts into `derivable: true` for that host's
  `deriveAppearance(name, registry)` calls.
- An `AvatarOutfitPreset` groups a `palette`, optional `baseFace` and `antenna`, and a zone-compatible
  selection of slot items so a host can apply a complete character costume or brand look in one step
  while preserving per-slot overrides through `equip`.
- `@dotcraft/avatar/react` exports `<AvatarCatalogProvider>`, `createAvatarCatalog`, `defineAvatarPack`,
  and the shared SVG authoring primitives (`Silhouette`, `Plane`, `Detail`, `Glow`, `useClipId`, rig
  bounds, and head motion helpers) so external slot components inherit size-tier degradation,
  effect gating, reduced-motion rules, and prop stowing automatically.
- `<AvatarOutfitProvider pack wear>` dresses every `AppearanceAvatar` below it for one context, such
  as a role, without changing the stored appearance. Each `wear` item replaces its slot through
  `equip`, so conflicting slots clear as usual, and the pack joins the avatar's catalog. The palette,
  the other slots, expressions, and motion stay the appearance's own.

## 6. Zone Compatibility

Two items conflict when they share at least one zone. Zones are the only compatibility mechanism.

- Brimmed hats occupy `top` and `brow`; novelty head objects occupy `top` only.
- Brow-band items such as the headlamp occupy `brow`.
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

An avatar's face can follow the pointer. `useAvatarGaze` from `@dotcraft/avatar/react` writes the
direction to `--dca-gaze-x` and `--dca-gaze-y`, each from -1 to 1, on an element around the avatar,
and the stylesheet turns them into a bounded `translate` of the face, so gaze never competes with
gestures, which write the face's `transform`. A host enables it only while the avatar rests in its
idle pose. It follows anywhere within 600px of the avatar, as the desktop pet does, or only inside a
`within` element such as the tile around it. Touch devices and reduced motion follow nothing.

### 8.1 Working Story

While an avatar holds the `working` pose at standard or full size with motion on, it plays one
looping working story around its laptop. The story keeps its own clock: it does not follow which
items the agent runs, and leaving the pose ends it at once.

- The laptop stays on the lap. Scenes vary only the hands, gaze, body, and antenna: typing, focused
  typing, a pause with the hands off the keys while the eyes drift up and the antenna glows, and a
  short antenna flash before typing resumes.
- The face keeps the operator expression. Arms only swing outward from the shoulder, so the story
  never raises a hand to the head or carries a prop across the screen.
- Scene changes are authored transitions on one continuous timeline whose last frame matches its
  first. Motion between keys follows smooth curves without flat holds.
- The story starts after the laptop is revealed, so a short turn shows only the opening typing
  loop. On leaving the pose its parts blend back within 160 ms while the props retract.
- Hosts add no outer loop while the story plays; the composer keeps only its placement and event
  motion.
- A pose change never jumps a frame. When it starts or ends a keyframe loop, such as the wave or the
  working loop, the body and arms blend from where they are into the new motion. The CSS working
  loop is phased so the story takes over on its first frame.
- A click wave during a turn plays to its last frame, then the avatar returns to the laptop. The
  composer keeps the working pose's host motion for the length of the wave.

A hand item that declares `workStory: "conduct"` stays in hand while working and plays the
conducting story instead, with no laptop.

- The story opens with both arms down while the antenna glows as if listening, so a short turn
  shows only that. The holding arm then rises into a four-beat pattern of down, in, out, and up,
  drawn by the wrist turning the item. A quiet phrase grows into a wider crescendo in which the
  other arm cues, the item holds high for a fermata, and both arms lower before the loop repeats.
- Raised arms use the wave's hinge, so the shoulder end stays inside the body. The holding arm stays
  between 60° and 117° while raised, and an arm rises or lowers within 240 ms.
- The item may mark parts the story lights with `data-conduct-part`. `glow` is a light at the tip:
  soft through the quiet phrase, brighter and larger through the crescendo, full for the fermata,
  and gone as the arms lower. It follows the phrase and never pulses on single beats. `clef` is a
  figure that fades in above the held tip once per loop during the fermata, rising slightly, and
  fades before the arms lower. Marked parts are hidden at rest, and an item that marks none plays
  only the arm story.
- The other working-story rules apply: one continuous seamless timeline, the operator expression,
  a 160 ms blend on leaving the pose, and no outer host loop.

A hand item that declares `workStory: "craft"` stays in hand while working and plays the crafting
story instead, with no laptop. The item builds something in the avatar's hand, so it is drawn as
parts that the story moves.

- The story opens with the holding arm raising the item beside the head while the eyes study it,
  so a short turn shows only that inspection. The item then comes apart: its parts separate around
  an inner light while the other arm rises and makes small tuning motions, and the wrist turns the
  item to look at it from each side. The parts lock back together in one beat that flashes the
  light and dips the body, the holding arm presents the finished item overhead, and both arms lower
  before the loop repeats.
- While the parts are open, sparks circle the inner light and arcs crackle between the pieces. On
  the present, the finished item leaves the raised hand to hover above the antenna, radiating rays
  with the sparks still circling it, then settles back into the hand before the arms lower.
- The item marks the parts the story moves with `data-craft-part`: `top`, `left`, and `right` for
  the pieces that separate, `core` for the inner light, `burst` for the lock flash, `arc` and
  `orbit` for the open-core energy, `rays` for the overhead glow, and `lift` for the group that
  rises above the head. Parts it does not mark stay still, so a simpler item still plays the arm
  story.
- Raised arms use the wave's hinge and stay between 70° and 118°. The wrist keeps the item upright
  apart from its deliberate turns, and an arm rises or lowers within 240 ms.
- The other working-story rules apply, and the item's own ambient beats pause while the story plays
  so the two never move the same part.

The package drives each story with the Web Animations API from a single exported timeline and applies
the avatar's motion-off, reduced-motion, paused, and offscreen rules to it. The design catalog
reviews the same timelines.

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
- Head items rest on the body's upper edge and stay clear of both arms. When a head item such as
  twin-tails or a costume hood includes a behind-body drape layer, that layer stays behind the body
  and arm contours while its silhouette remains visible at compact size.
- Brow items stay inside the brow band and never enter the screen. The band is too shallow for an
  object to read inside it at standard size, so a brow item needs a strap across the full brow or a
  part that leaves the band, such as the snorkel's tube. Glasses do not qualify.
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

A find arrives as a gift on the composer mascot. A small box in the item's rarity color pops above
the mascot's head, hovers, and drops onto the composer edge beside the mascot's left foot, where
unopened gifts wait. The newest three show and a count marks the rest. Epic and legendary boxes
glow and wiggle now and then.

- Opening a gift reveals the item above the mascot with its name, rarity, and actions to keep it or
  wear it, and the mascot cheers.
- The item is already in the bag when the gift appears. Unopened gifts live only in the running
  window, so restarting the app drops them without losing anything.
- When no composer mascot is mounted, the find is announced with the replaceable toast instead.

### 11.4 Bag and Exchange

- The bag lists every owned item with its copy count, filterable by all, wearing, or slot. Hovering
  an item tries it on; selecting wears it, or takes it off when worn.
- The exchange trades exactly ten spare items of one rarity for one random item of the next rarity.
  A worn copy is never a spare, and legendary items cannot be traded up. The person picks the items
  from the rarity's spares, one copy per selection, or lets a fill action pick duplicates first, and
  may remove any before trading. When a trade spends the last copy of a worn item, it comes off.
- A trade's result is announced with a replaceable toast showing the item, its rarity, and an action
  that wears it.
- Hovering or focusing any item, in the bag, the spares, or the tray, shows its full name, rarity,
  slot, and feature.
