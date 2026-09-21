# Avatar Collection

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-09-21 |
| **Related Specs** | [Agent Profiles](agent-profiles.md), [Desktop DESIGN.md](../architecture/DESIGN.md), [Desktop Client](../clients/desktop-client.md), [TypeScript SDK](../sdk/typescript.md) |

Purpose: define the shared `@dotcraft/avatar` collection model. It covers the equipment slots an
avatar exposes, the item registry with rarity and series, zone-based compatibility, the
deterministic name derivation, size-tier degradation, and the boundaries for item-level visual
effects. The design catalog in the DotCraft design repository owns the review surfaces; this spec
owns the model that every host renders.

---

## 1. Scope

- One `Appearance` record shared by Desktop, Universe, the design catalog, and any SDK consumer.
- Five independent equipment slots and the registry of items that fill them.
- Rarity tiers and series as item metadata, including how rarity weights name derivation.
- Zone occupancy as the single compatibility mechanism between items.
- Size tiers that decide which layers and effects render at a given pixel size.
- Item-level visual effects implemented inside the SVG document.

Out of scope for this version:

- Unlocking, drops, inventories, persistence, or any user-equipped state. Phase two adds an
  equipped-appearance source for the default mascot; the record shape below already carries it.
- Trading, exchange, or limited-time availability.
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
normalized name derives it; Desktop's default composer mascot renders it until phase two supplies
an equipped appearance.

Slot IDs are stable kebab-case strings and are serialized as-is. Slots are independent fields so a
host can override one slot without touching the others.

---

## 4. Slots

| Slot | Mounts | Rig layer | Notes |
|------|--------|-----------|-------|
| `head` | Head top | Replaces the antenna and its status light | Existing hats and novelty objects live here. |
| `face` | Brow band or a faceplate over the screen | Brow items sit in front of the body behind the face marks; a faceplate replaces the face marks | Brow items never paint on the white screen. A faceplate carries its own expression layers and stays visible at compact size because it defines the head silhouette. |
| `hand` | Screen-left hand | Inside the left arm group | Shares the arm pivot; stows for laptop and question-sign work props. |
| `back` | Behind the body | First layer inside the rig, plus an optional front layer drawn over the face and under the work props | Wings, capes, packs, rings, and auras. A ring that passes around the body renders its far half behind and its near half in front; bodies on the ring exist in both layers and the shared phase animation shows the matching copy. Flat rings such as the halo stay behind. |
| `skin` | Body material | Overlay skins paint on the body surface under the screen; paint skins replace the body and arm paint | Face marks keep the palette in both kinds. |

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
- Rim items occupy `rim`; the zone is reserved for future items on the blue rim.
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
(a brimmed hat occupies the brow, so a common or uncommon face draw finds no candidate and the
face slot stays empty instead of falling through to a faceplate).

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
- CSS transforms on overlay groups clipped to the body for sheen sweeps and orbiting parts.
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
- The paint surface carries an energy wash: a body-clipped rectangle in the material accent whose
  opacity pulses at medium (0.16), high (0.26), extraHigh (0.38), and context max (0.30), on the
  same periods as the default body animation.
- Flowing materials speed up with effort instead of running a fixed loop: lava, galaxy, and
  holographic stops and the chrome/gold sheen shorten their periods at medium, high, and extraHigh;
  galaxy stars blink faster at high and extraHigh.

All of this is gated on `data-effects="live"`, so compact and standard sizes and motion-off hosts
show the same static paint as before.

---

## 10. Paint Contract For New Items

- Recognizable objects with natural colors, rounded silhouettes, and a white outer contour.
- A `Silhouette` path carries the outline; `Detail` groups hide at compact size.
- Head items rest on the body's upper white edge and stay clear of both arms.
- Brow items stay inside the brow band (view-box y 408–464) and never enter the screen rectangle
  (x 295–729, y 464–779). The band is thin, so only objects with a strong silhouette such as
  goggles qualify; abstract bands and small strips do not read and are not added.
- Faceplates fill the screen inside a 12-unit white frame and draw four eye variants with the
  shared face-layer classes. Following rigid-robot references (Iron Man, EVE, Cozmo), cutouts and
  lenses never morph: an armored plate expresses state through light (tint, intensity, a core
  line, a scan line, a breathing ember) over fixed slits; an LED visor uses a pair of thick eyes
  whose lids move on straight or gently curved edges (raised lower lid for happy, lowered upper lid
  for operator, dropped and closed to a line for sleep). Curved "^^" eye arcs on metal are not
  allowed.
- Paint skins keep their material on raised arms. The solid hinge color exists for single-hue
  gradients whose direction would otherwise break at the shoulder; multi-band materials read as
  reflection changes.
- Hand items meet the left hand at its resting tip and keep their marks upright at rest.
- Back items stay behind the body and may extend past the body bounds; the avatar canvas is
  `overflow: visible` and hosts reserve headroom already.
- Overlay skins are clipped to the body rounded rectangle and use translucent white or shadow ink so
  every palette reads through. Paint skins provide the replacement body paint plus solid colors for
  the raised-arm rule.

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
- Faceplates render all four expression layers and no native face marks.
- Paint skins keep the palette on the face marks, keep their material on raised arms, and overlay
  skins keep the palette body paint.
- Desktop and Universe compile against the package without local artwork or model copies.
