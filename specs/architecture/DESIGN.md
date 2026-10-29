---
version: "0.9.6"
name: "DotCraft Desktop"
description: "Quiet operational desktop UI for repeated agent work."
colors:
  bg-primary: "var(--bg-primary)"
  bg-secondary: "var(--bg-secondary)"
  bg-tertiary: "var(--bg-tertiary)"
  bg-active: "var(--bg-active)"
  bg-elevated: "var(--bg-elevated)"
  text-primary: "var(--text-primary)"
  text-secondary: "var(--text-secondary)"
  text-dimmed: "var(--text-dimmed)"
  text-tertiary: "var(--text-tertiary)"
  border-default: "var(--border-default)"
  border-active: "var(--border-active)"
  accent: "var(--accent)"
  accent-hover: "var(--accent-hover)"
  success: "var(--success)"
  warning: "var(--warning)"
  error: "var(--error)"
  info: "var(--info)"
  success-bg: "var(--success-bg)"
  warning-bg: "var(--warning-bg)"
  error-bg: "var(--error-bg)"
  info-bg: "var(--info-bg)"
  success-text: "var(--success-text)"
  warning-text: "var(--warning-text)"
  error-text: "var(--error-text)"
  info-text: "var(--info-text)"
  ref-skill: "var(--ref-skill)"
  git-merged: "var(--git-merged)"
  permission-full-access: "var(--permission-full-access)"
  glass-surface-strong: "var(--glass-surface-strong)"
  background-activity-dock-background: "var(--background-activity-dock-background)"
  composer-top-accessory-separator: "var(--composer-top-accessory-separator)"
  composer-input-rest-border: "var(--composer-input-rest-border)"
  main-surface-edge-glow: "var(--main-surface-edge-glow)"
  scrollbar-thumb: "var(--scrollbar-thumb)"
  scrollbar-thumb-hover: "var(--scrollbar-thumb-hover)"
  scrollbar-thumb-active: "var(--scrollbar-thumb-active)"
typography:
  ui:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-ui-size)"
    fontWeight: 400
    lineHeight: "var(--type-ui-line-height)"
    letterSpacing: "0"
  ui-small:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-secondary-size)"
    fontWeight: 400
    lineHeight: "var(--type-secondary-line-height)"
    letterSpacing: "0"
  ui-hint:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-hint-size)"
    fontWeight: 400
    lineHeight: "var(--type-hint-line-height)"
    letterSpacing: "0"
  panel-heading:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-heading-size)"
    fontWeight: 600
    lineHeight: "var(--type-heading-line-height)"
    letterSpacing: "0"
  page-heading:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-page-title-size)"
    fontWeight: 600
    lineHeight: "var(--type-page-title-line-height)"
    letterSpacing: "0"
  detail-heading:
    fontFamily: "var(--font-ui)"
    fontSize: "var(--type-detail-title-size)"
    fontWeight: 500
    lineHeight: "var(--type-detail-title-line-height)"
    letterSpacing: "0"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  2xl: "32px"
rounded:
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "10px"
  identity-hero: "16px"
  full: "999px"
components:
  turn-activity-status:
    color: "{colors.text-secondary}"
    typography: "conversation body; regular; tabular numerals"
    padding: "no vertical padding; label flush with tool-row and reply text"
    divider: "full-width 1px border-default, 4px below the label whether working, worked, or stopped"
    spacing: "16px from the boundary to activity or final response content"
    behavior: "Working/worked/stopped share one top boundary before activity; final answer enables collapse; cancellation keeps output expanded"
  primary-action:
    background: "{colors.text-primary}"
    color: "{colors.bg-primary}"
    border: "1px solid {colors.text-primary}"
  menu-overlay:
    border: "none"
    rowHover: "var(--sidebar-control-hover)"
  nav-icon-motion:
    trigger: "hover or keyboard focus on a sidebar destination; plays once"
    duration: "340-720ms"
    behavior: "one glyph part moves; starts and ends on the static drawing; the row never moves"
  selection-row:
    border: "none"
    hoverBackground: "var(--bg-tertiary)"
  hover-annotation:
    cardPlacement: "inline axis — right, mirroring to left"
    controlPlacement: "block axis — top, mirroring to bottom"
    cardSurface: "var(--glass-surface-strong)"
---

# DotCraft Desktop Design

This document is the source of truth for DotCraft Desktop visual design: the
product-level intent, shared visual rules, and review checklist that new and
changed Desktop UI must follow. Token values live in the Desktop token stylesheet;
this file names the roles those tokens play.

The file runs from foundations (color, type, layout, surfaces, shape, motion) to
the shared controls built on them, then to overlays, inputs, and the conversation,
and ends with page grammars and feature surfaces.

## Overview

DotCraft Desktop is a quiet operational tool for repeated agent work. It should
feel calm, compact, legible, and consistent across conversation, settings,
skills, automations, plugins, channels, detail viewers, and modal flows.

The design posture is neutral-first:

- Use neutral surfaces, text, borders, spacing, and elevation as the default UI
  language.
- Use emphasis only when it clarifies current state, next action, risk, or
  selected context.
- Prefer existing Desktop tokens and shared components over one-off colors or
  bespoke control treatments.
- Avoid decorative gradients, page-specific palettes, and large color washes
  unless a feature owns a documented media or visualization surface.

The brand accent is intentionally conservative. It is not the default
call-to-action color.

## Window chrome

On Windows, the main window uses native caption controls in a 36px title-bar
overlay. Navigation and menus may use the remaining title-bar area but must
reserve the overlay's safe area. The overlay is transparent and its symbols follow
the active theme. When the top-level menus no longer fit, they collapse to one
entry that opens the same application menu. Linux uses custom caption controls;
macOS keeps its native traffic lights.

## Color

### Neutral roles

Neutral tokens carry most UI structure.

- `--bg-primary`: app and main content background.
- `--bg-secondary`: cards, panels, menus, secondary controls, and repeated
  items.
- `--bg-tertiary` / `--bg-active`: hover, pressed, selected, nested, or active
  neutral states.
- `--text-primary`: primary copy, headings, and inverted action backgrounds.
- `--text-secondary`: secondary copy, labels, inactive icons, and metadata.
- `--text-dimmed` / `--text-tertiary`: low-priority helper text and empty-state
  detail.
- `--border-default`: a control's, input's, or card's own edge. It does not
  divide a page into regions.
- `--border-active`: hover, focused, or active neutral boundaries.
- `--border-subtle`: the quietest rule, for separating stacked groups (see
  Detail sections).

The text ramp is `--text-primary` → `--text-secondary` → `--text-dimmed` →
`--text-disabled`. `--text-tertiary` is an alias of `--text-dimmed`, not a fifth
step.

### Accent

`--accent` and `--accent-hover` are reserved for restrained brand or navigation
emphasis:

- keyboard focus-visible outlines and accessibility affordances;
- selected navigation or live-state accents when neutral inversion is not
  appropriate;
- links or small inline affordances where product recognition helps;
- setup or onboarding moments where DotCraft is intentionally presented as the
  product.

Do not use `--accent` as the default primary button background for ordinary
actions such as create, start, save, submit, continue, or configure. Selection
inside a segmented control, a pressed toggle, and an open menu trigger are
neutral states, not accent ones.

### Semantic colors

Semantic colors communicate state, not decoration:

- `--success`: completed, healthy, connected, applied.
- `--warning`: caution, pending review, risky but recoverable.
- `--error`: destructive action, failed state, blocked state.
- `--info`: informational status when neutral text is insufficient.
- `--permission-full-access`: the full-access / auto-approve permission state, a
  warmer orange than `--warning`, used only on the composer approval pill and small
  option icons.

Semantic colors normally appear in icons, compact badges, borders, small text, or
alert surfaces. They do not take over an entire view.

Each hue has two companions. `--success-bg` and its siblings are tinted surfaces
mixed off the hue, used behind status badges and notice strips. `--success-text`
and its siblings are reading colours for anything readable on a tint: a status
colour chosen for a 16px glyph does not clear AA as 13px text on a tint of itself.
Use the `-text` token wherever a status colour becomes prose.

### Inverse surface

`--bg-inverse` is the opposite theme's tertiary tone, with `--text-on-inverse`,
`--text-on-inverse-muted`, `--border-on-inverse`, and `--fill-on-inverse` on top.
It is reserved for tooltips, the one transient, non-interactive label. Menus,
popovers, dialogs, and hover cards are places the pointer goes and keep the
ordinary elevated surface. Do not use the inverse pair to make a control stand
out.

### Identity and data colors

Feature, channel, and provider colors are allowed only as small identity accents
inside icons, avatars, badges, media previews, or charts. They must not become a
view theme.

`--chart-series-1` through `--chart-series-4` and `--chart-series-other` are the
data-visualization palette: four hues in a fixed order, stepped per theme and
validated for color-vision deficiency, plus a neutral for the folded remainder.
Assign them in order, fold everything past the kept series into the neutral, and
keep chart text, axes, and legends on `--text-*` and `--border-*` tokens.

### Code token colors

Syntax highlighting is the one place where color does not come from product
tokens. Desktop ships one vendored TextMate theme pair and lets the highlighter
emit both theme colors per run, so one tokenization serves both themes and
switching appearance repaints without re-highlighting. The color scheme is set on
the code surface, never on the document.

The boundary is the code text. Everything framing it stays on product tokens:
gutters and line numbers, block and pane backgrounds, diff fills and accent bars,
selection, search highlights, and every surrounding control. Features do not read
or redefine the highlighter's token colors.

## Typography

Desktop typography is compact and readable. Card and panel headings use modest
weight increases rather than display-scale type; hero-scale type is reserved for
true entry surfaces. Letter spacing is `0` unless a technical label style
documents otherwise.

### UI type scale

Sizes come from the `--type-*` tokens. Do not write a raw font size in a
component: a literal cannot be retuned by context, and every literal is a new
tier nobody agreed to.

| Token | Size / leading | Use |
| --- | --- | --- |
| `--type-title` | 28 / 34 | entry surfaces only |
| `--type-detail-title` | 20 / 27 | the named subject in an identity-led detail header |
| `--type-page-title` | 18 / 23 | panel page heading |
| `--type-heading` | 15 / 20 | card, group, and dialog heading |
| `--type-body` | 14 / 21 | conversation and document body |
| `--type-ui` | 13 / 18 | ordinary UI text, row labels, inputs |
| `--type-secondary` | 12 / 16 | supporting text; use `--type-secondary-prose-line-height` (18) when it wraps |
| `--type-hint` | 11 / 16 | supporting text nested under an already-labelled control |

Size expresses nesting depth, colour expresses role. Copy that describes a
control is `--text-secondary` at whichever size its depth calls for; only
incidental metadata (version strings, timestamps) drops to `--text-dimmed`. Do
not pair the smallest size with the dimmest colour.

### Conversation type scale

The transcript has its own four tiers. Code follows the code font size setting;
the other three derive from `--conversation-font-size`.

| Token | Size / leading | Use |
| --- | --- | --- |
| `--conversation-font-size` | 14 / 1.5 | messages, markdown body, card titles, and every process line: the activity divider, tool rows, thinking and reasoning, system notices |
| `--conversation-secondary-size` | 13 / 1.5 | content nested under a process line: expanded tool output and commands, card subtitles and counts, reference chips, error blocks |
| `--text-code-size` | 12 / 1.5 | code blocks, diffs, inline code, file content |
| `--conversation-meta-size` | 12 / 16 | timestamps, message origin, key hints, counters |

A process line ranks against the answer by colour, not size: it stays at
conversation size in `--text-secondary` or `--text-dimmed`. Markdown headings
scale from the body in `em` (1.5, 1.25, 1.125, then 1 for `h4`–`h6`) at weight
600. Composer controls outside the input are UI chrome and use `--type-ui`.

### Retuning the scale by context

A surface may override the `--type-*` tokens for the subtree it owns instead of
changing components. The settings surface lifts the two smallest tiers one step
for CJK languages, whose glyphs carry far more strokes per em. Lift tiers in pairs
so the gap between them survives, and keep the override scoped: a global lift
reflows fixed-height rows in the composer and sidebar.

### Fonts

UI fonts are system-first and must not require bundled web fonts. `--font-ui`,
`--font-body`, and `--font-sans` may switch by document language for CJK locales
while preserving the same weight and spacing scale.

## Appearance preferences

The palette is stated as a per-variant seed (surface, ink, accent, and a 0-100
contrast) from which the surface, text, and border ramps derive. `surface` is the
base plane: the page in dark, the card in light, so both variants move away from
it by mixing in ink. A default seed writes nothing.

The Appearance settings expose:

- Theme mode `system | light | dark` (`system` resolves from the OS).
- A custom accent, from which `--accent`, `--accent-hover`, and the on-accent
  foreground derive. A custom accent stays restrained per the Accent rules; it is
  never promoted to a primary-action fill.
- Code font size, which overrides `--text-code-size`.
- Diff markers (`color` vs `+/-`) for every diff view.
- Reduced motion (`system | on | off`), which gates animations, and pointer
  cursors on interactive elements.
- The [Welcome backdrop](#welcome-backdrop) on or off.

Animated, accent-driven, or code-sized UI relies on these tokens and preferences
rather than hardcoding colors, sizes, or unconditional animations.

## Layout

### Density and stability

Desktop surfaces favor dense but organized operational layouts.

- Keep common workflows ergonomic for repeated use.
- Use stable dimensions for fixed-format controls such as rows, toolbars, icon
  buttons, counters, tabs, and menus.
- Constrain content so hover states, labels, icons, loading text, and dynamic
  content do not resize or shift the layout.
- Avoid nested cards and decorative section cards. Page sections are unframed
  layouts or full-width bands with constrained inner content. Cards are for
  repeated items, modals, or genuinely framed tools.
- A notice that interrupts a surface takes that surface's content column: the
  same width and centering as the rows, cards, or grid it sits above, never the
  full content box. Elements that share a column read its width from one place.

### View color assignment

| Surface | Main visual color | Emphasis |
|---------|-------------------|----------|
| Conversation | Neutral surfaces | Neutral inversion for send/primary actions; small semantic/tool status colors |
| Automations | Neutral catalog/list surfaces | Neutral primary action; semantic status badges |
| Skills / Plugins / Catalogs | Neutral cards and rows | Neutral management actions; small provider/icon colors |
| Settings | Neutral grouped rows | Subtle selected navigation and focus states |
| Channels | Neutral cards/forms | Small channel identity icons; semantic connection state |
| Detail viewers | Content-native when needed | Neutral viewer chrome |
| Modals and dialogs | Neutral elevated surfaces | One neutral inverted primary action |
| Setup / onboarding | Neutral product surface | Restrained brand accent is allowed |
| Release highlights | Neutral modal surface | Media previews may contain their own colors |

### Catalog pages

- Catalog browse, manage, and detail surfaces share one 48px top control band:
  navigation (tabs or breadcrumb) left, page-level management actions right, on
  one vertical center. Item-scoped actions on a detail page may sit beside the
  title block.
- Every full-page catalog surface shares one browse frame below that band: a
  pinned header (`28px 64px 16px`) carrying the hero title and search row, then a
  scrolling body (`28px 64px 48px`), both centered on one 760px column.
- A group is a `16px`/`700` title over one grid of `58px` rows: two columns at
  `34px`/`18px` gaps for items the user can add, one column at `4px` for records
  the user already owns. Every row is a borderless `8px` box with a `40px` leading
  mark, a `--type-ui` title and one truncating `--type-secondary` line, and fills
  with `--bg-tertiary` on hover.
- Owned records compared by several attributes use aligned columns under one
  quiet `--type-secondary` header line with no rules. The name column comes first
  and takes the remaining width; trailing row actions sit in a reserved slot and
  appear on hover or focus without shifting columns. As the page narrows, the
  least important columns hide first.
- Group titles name scope (installed by the user versus provided by the
  workspace); the band and the hero never carry workspace scope.
- An item from a user-added source carries a small origin mark after its name, in
  secondary text, centered on the name's cap height, with a tooltip naming the
  source. Host-provided and user-owned items carry no mark.
- Controls and groups separate with space and heading weight, not rules.
- Top bars prefer icon-only actions with tooltips for repeated management
  commands such as Refresh and Manage, keeping the labelled action for the one
  principal command.

## Surfaces and elevation

Use depth instead of color variety.

- Main content: `--bg-primary`.
- Panels, cards, menus, secondary controls, and repeated items:
  `--bg-secondary`.
- Nested, hovered, or active surfaces: `--bg-tertiary` / `--bg-active`.
- Elevated overlays use a solid, opaque surface (`--glass-surface-strong`,
  resolving to `--bg-elevated`) plus shadow tokens. A translucent overlay tints
  differently depending on what sits behind it; an opaque one renders identically
  everywhere.
- Ordinary menu overlays are borderless and separated by surface plus shadow.
  When an overlay overlaps another elevated overlay (a submenu over its parent),
  add a single `1px` `--glass-border` hairline on the overlapping edge only. This
  is the only border an ordinary overlay carries.
- Larger dialogs, inspectors, viewers, and non-menu popovers may use subtle
  neutral boundaries when contrast requires it.
- Workspace resize dividers rest as a `1px` hairline and answer hover and drag
  with one shared `1.5px` neutral vertical gradient (`--main-surface-edge-glow`),
  brightest at centre. It is a functional affordance, so the rule below does not
  reach it.

Do not use glow rings, highlighted borders, accent borders, or decorative
gradients to make ordinary controls "stand out." Use placement, hierarchy,
weight, spacing, and neutral inversion first.

## Shape

Ordinary controls and cards use 8px radius or less unless an established
component family uses another token.

- Text buttons: the `--button-text-radius` pill at every size (see Buttons).
- Icon buttons, fields, and selects: `8px` (`--button-radius`) on the standard
  band, `10px` on the compact bands (settings, catalog top bars, browser
  toolbar).
- Small transcript icon actions and the small icon-button size: `6px`.
- Cards and repeated items: 8px.
- Dialogs and elevated popovers: 8px to 10px.
- Pills, badges, and toggles: `999px` when the shape is semantically pill-like.

Large rounded rectangles are not decoration.

### Identity marks

Product, plugin, skill, channel, provider, and connected-app artwork uses one
identity-mark family. Avatars, status dots, thumbnails, favicons, action glyphs,
and illustrations keep their own shape rules. Choose a role instead of deriving
corner geometry from an arbitrary size:

| Role | Standard size / radius | Use |
| --- | --- | --- |
| Compact | `24px / 6px` | Dense metadata, prompt prefixes, and connection rows. Inline marks may reduce to `18px` while retaining the compact radius. |
| List | `40px / 8px` | Catalog, management, dialog-header, and repeated identity rows. Local layouts may use `30–40px` without creating another radius tier. |
| Hero | `60px / 16px` | One prominent identity at the start of a detail or setup surface. Rare. |

Artwork is contained, never cropped; cropping belongs to thumbnails. Shells are
transparent, and each asset owns any background it needs for contrast. Hero
shells carry only a near-invisible `1px` `--identity-mark-hero-border` hairline;
compact and list shells are unframed unless interaction requires a boundary.
Apply optical padding to the artwork, not the shell. Reserve circles for people,
presence/status, toggles, and circular actions.

An item without artwork gets the shared neutral fallback mark, never a generated
initial: one shape per kind (a cube for a Skill, a plug for a Plugin, a message
bubble for a Channel), shaded from `--text-primary` over the neutral fallback
ground at 68% of the shell. Each mark is one connected silhouette; only the cube
carries more than one tone. One kind, one shape, at every size: a Skill is a cube
wherever it appears, filled at identity sizes and drawn as line art inline.
`Puzzle` belongs to Plugins and Extensions only. Overlapping marks separate with a
ring in the page colour.

Identity marks use a squircle: on engines that support
`corner-shape: superellipse(1.5)`, the radius is scaled by `1.25` to keep the
perceived corner equivalent. Never apply the scaled radius without the corner
shape.

Hero-led detail headers place the identity row `16px` below the mark. The subject
uses `--type-detail-title` (weight `500`); the subtitle uses `--type-body` in
`--text-secondary`, `6px` below. Actions align to the row's top.

### Agent avatars

Agent avatars take their complete visual identity and interaction rendering from
the shared avatar package. Hosts pass name, state, expression, gesture, and
hand/work-prop intent without copying or overriding its artwork. At `20px` and
below, keep the head, back, and arms but hide face, hand, overlay-skin, and effect
layers. Item effects animate only at `44px` and above with motion enabled. A
left-side hold owns the left hand. Native work props take priority over
decorative hand poses. Rarity is catalog and settings chrome; it never paints onto
an avatar in product lists.

## Motion

Ordinary controls remain geometrically stable through hover, focus, open, and
pressed states. Use color, surface, border, or shadow changes for feedback; do
not translate, scale, rotate, bounce, or spring a control on press. Transform
motion is allowed only in the documented cases below, and always honors the
shared reduced-motion preference.

### Disclosure

Every expand/collapse toggle uses the shared disclosure chevron (`14px`, stroke
`1.8`) and rotates it on `--duration-expand` rather than swapping glyphs: inline
for a row or section that opens in place (points right, turns `90deg`), reveal
for content that opens below the control (points down, turns `180deg`). A chevron
that is its own target sits in an icon button that exposes its expanded state.
A labelled toggle that reveals more fields below it ("Advanced options") is the
shared disclosure button: plain secondary text with a reveal chevron, no padding
or fill, aligned with the field labels.

### Rail magnification

The turn navigation rail is the one growing mark. The target marker extends to
full length and its neighbours to 70%, 40%, and 20% of the difference, like a
dock. Markers grow horizontally from their leading end over `160ms` on a slightly
overshooting curve; the bookmark dot travels with the line end. While scrubbing,
markers follow the pointer without a transition. Reduced motion removes the
transition, not the state.

### Mode toggles

A mode toggle may reveal its label when it turns on. The control keeps its height
and icon position and grows only in width (over `--duration-expand` on
`--ease-expand`, the label fading in), taking the space from the row's flexible
neighbor. The in-browser Annotate control is the reference case. Ordinary toggles
stay icon-only and show their pressed state with the neutral `active` tone.

### Sidebar selection

The current sidebar destination looks the same in the expanded rows and the
collapsed rail: `--sidebar-control-active` behind it and `--text-primary` on its
icon and label. Accent never marks the current destination.

### Navigation icon motion

Sidebar destinations answer hover and keyboard focus with one short glyph motion,
in the expanded rows and the collapsed rail alike. The row and its hit target
never move; one part of the glyph does, for 340–720ms:

| Destination | Motion |
| --- | --- |
| New chat | The pen scribbles one small loop about its nib |
| Search | The lens turns edge-on about its handle and back |
| Channels | Typing dots appear one after another inside the bubble, hold, and leave together |
| Agents | The eyes glance toward the label and back |
| Automations | The minute hand sweeps one hour |
| Plugins | The lid rises straight off the box and drops back with one settle |
| Settings | The gear advances one tooth |
| Oratorio (plugin) | The mark dips into one downbeat about its ring and rebounds |

- Every motion starts and ends on the static drawing, so leaving mid-motion
  returns the glyph to rest and reduced motion leaves it still.
- A glyph plays once per hover or focus; sweeping across the sidebar moves only
  the glyph under the pointer.
- Disabled rows stay still; the current destination still answers. Content rows
  (projects, threads, chats) keep static icons.
- A plugin destination may join under the same contract. The Host marks every
  destination row and rail button with `data-nav-icon-host`, and the plugin's
  stylesheet keys its motion off that ancestor's hover and focus-visible states.
- Motion moves parts; it does not reshape them. Judge it at 16px: a part that
  appears arrives on opacity rather than hopping pixel rows and is at least 2.5
  units wide, and two strokes a unit or two apart are never drawn together.

### Welcome backdrop

The welcome composer may sit on a field of small monospace characters that light
up as gusts of wind pass over them. The field is decoration, never content: hidden
from assistive technology, never focusable or blocking the pointer, and nearly
empty behind the heading, composer, and suggestions, so it moves in the margins.

- The wind keeps no time: it surges and slackens, gusts change shape and arrive
  off the beat, and nothing repeats on a fixed rhythm. A gust lights first on the
  side it is heading for.
- Characters use the tertiary-text ink. Strength is set by contrast, not opacity:
  a fully lit character reaches about 1.45:1 against the background in dark
  themes and 1.32:1 in light ones. Only a click brings in `--accent`, up to three
  times the plain light.
- The field fades in over a few seconds, runs at about 25 frames a second, settles
  after three minutes without input, and stops while the window is hidden or
  unfocused.
- The pointer lights the characters beneath it. A click off any control sends a
  front outward from that point that pushes the wind, carries the accent briefly,
  and leaves a faint afterlight; the front is never a drawn circle.
- Reduced motion draws one still frame. Without hardware WebGL2, with increased
  contrast, or with forced colors, nothing is drawn.

## Controls

### Action hierarchy

Decide the hierarchy before choosing a treatment. Each immediate decision area
has at most one primary action.

Primary actions use neutral inversion: a `--text-primary` fill and `1px` border,
`--bg-primary` text, weight 600.

Secondary actions use a neutral frameless fill: a subtle `--text-primary` tint
(~6%, hover ~11%), a transparent border reserved in the box model, and
`--text-primary` text. Ordinary management actions (Manage, Configure, Refresh,
repeated row controls) are secondary unless they are the one immediate
submit/continue action. They never use decorative gradients, accent-tinted
borders, glow rings, or provider colors.

Tertiary actions are transparent text/icon controls with neutral hover feedback,
for inline affordances, low-frequency commands, and compact toolbars.

Destructive actions use explicit copy such as Delete, Remove, Discard, or Stop.
The danger affordance is a frameless `--error` fill (~10% tint, hover ~18%) with
`--error` text, not a bordered outline. Keep surrounding chrome neutral and
require confirmation where appropriate. An icon-only delete uses the icon-button
danger tone; a reversible remove (a blocked domain, a download record) stays
neutral.

The hierarchy follows the surface, not the verb:

- A dialog footer sits on the standard band and carries at most one `primary`;
  Close is `primary` only when it is the footer's sole action.
- Retry is `secondary`; it becomes `primary` only as the sole action of an error
  state.
- A decision inside a card or review footer makes the accepting choice `primary`
  and the others `outline`, matching the approval composer.
- An app banner pairs an `outline` principal action with a `ghost` dismissal.
- A settings page or segment has one principal action (see Settings groups).
- A small mutually exclusive choice is the shared segmented control, never a row
  of hand-styled toggles. Its selected segment is a raised neutral surface, not an
  accent fill.

Quiet actions are the one control with no hover feedback. Reserve them for text
that is primarily a label and only secondarily a target (an agent name, a
provenance line). A quiet action inherits the surrounding size, weight, and line
height rather than moving the text onto the button band. Focus must stay visible,
and the affordance lives elsewhere: a chevron beside it, or a tooltip naming the
action. A row keeps at most one quiet action.

Inline references and subagent names are not quiet actions. They answer hover by
lifting their text to `--text-primary` (see Inline reference chips), the one hover
treatment a text-only target may carry; never a fill, border, or pill around the
words.

### Buttons

Labelled actions use the shared button; icon-only actions use the shared icon
button (see Icon buttons). A button has a variant (hierarchy) and a size
(footprint):

- Variants: `primary` (neutral inversion, the one immediate action), `secondary`
  (frameless neutral fill, the common action), `ghost` (transparent tertiary),
  `danger` (frameless semantic fill, with explicit Delete/Remove/Stop copy),
  `accent` (restrained brand, never the default create/save/manage), `outline`
  (bordered on a `--bg-secondary` fill), `outlineGhost` (the same frame without a
  fill).
- Sizes: `default`, `sm`, `toolbar`, `prominent`, `icon`, `iconSm`.

Buttons are frameless by default. Every variant reserves a `1px` border but only
`outline` and `outlineGhost` paint it, so switching between fills and frames never
shifts height or alignment. A visible frame is reserved for special or important
framed actions:

- `outline` for open-target controls in the file viewer (even when icon-only) and
  for the non-accepting choices of a card or review decision.
- `outlineGhost` for a framed action inside a card that already has its own
  surface, such as a transcript card's open, preview, and review actions at the
  `toolbar` size. Undo beside them stays `ghost`.

Buttons that share a row share one control band. A surface picks its band once,
by size or by rescoping the band tokens (`--button-height`, `--button-height-sm`,
`--button-radius`, `--button-padding-x`); nothing opts in per control.

| Band | Text buttons | Icon buttons, fields, selects | Where it applies |
| --- | --- | --- | --- |
| Standard (`default`) | `32px`, pill, `13px`, `12px` inline padding | `32px` / `8px` | every surface not named below, including dialogs |
| Compact (`sm`) | `28px`, pill, `12px`, `10px` padding | — | a denser row inside a standard-band surface; the plugin Install action across browse, manage, and detail |
| Catalog top bar (`toolbar`) | `28px` / `10px` / `13px`, `8px` padding | `28px` / `10px` | every control in a catalog or Builder top bar, including compound triggers |
| Settings surface (token scope) | `28px`, pill; `default` and `sm` share the height | `28px` / `10px` | everything inside the settings surface |
| In-app browser toolbar (token scope) | — | `28px` / `10px`, including the address field | the browser toolbar |
| Prominent (`prominent`) | `38px`, pill, `13px`, `18px` padding | — | one standalone high-emphasis call to action |
| Viewer chrome | — | `16px`, `24px`, or `28px` | a viewer tab slot or toolbar that already reserves that footprint |
| Transcript row actions | — | `24px` / `6px` | copy, fork, and edit on transcript rows and blocks |

Text buttons use the `--button-text-radius` pill at every size; only `toolbar`
keeps the `10px` band radius. The `icon` size is a `--button-height` square on
`--button-radius`; `iconSm` is a fixed `28px / 6px` square. Icon+label controls
keep a `6px` gap. Compact bands are shorter because they run a strip of many small
controls rather than one decision, which also lets a settings group's header
action fit inside its header. Dialogs open outside the settings surface and keep
the standard band.

The prominent size is a deliberate exception: the single primary button in a
focused setup or install dialog, or a lone full-width confirm. When one shares a
row with other buttons, raise the others to its height.

Action labels trim to cap height so text centres optically; icons sit outside
the label. Loading overlays the spinner without changing the control's footprint
or accessible name.

Settings and catalog header actions carry their glyph when the verb has one (Plus
for create, trash for Delete, refresh for Refresh) at `15px` with the `6px` gap.
In the Builder toolbar, Create is `primary` and Preview/Edit is `secondary`.

### Icon buttons

Icon-only controls use the shared icon button, frameless by default:

- the footprint and radius of the band it sits in (see Buttons), overridden only
  where the band table names another footprint;
- transparent surface with a reserved `1px` transparent border;
- `--text-secondary` icon, with a neutral hover fill (`--bg-tertiary` +
  `--text-primary`);
- a pressed toggle (code-block wrap, file-review wrap, the catalog filter, an
  explorer toggle) uses the neutral active tone; the accent tone is kept for live
  states such as a running screen view and never becomes an accent border;
- an open menu or popover trigger shows a neutral fill; opening ordinary chrome is
  not a selected accent state;
- destructive icon-only actions use the danger tone rather than a painted red
  border.

The shared hover, focus, disabled, open, and danger treatments apply at every
footprint. A visible neutral frame (`--bg-secondary` + `1px` `--border-default`)
is opt-in and reserved for special or important icon controls.

Thread List icon actions answer on the foreground alone. The thread or project
row already owns the hover and current-state surface, so its compact actions and
section-header actions stay transparent through every state, moving their icon
from the quiet foreground to the primary one; a second rounded surface inside a
highlighted row reads as a box on a box. Focus-visible keeps the shared outline.
Pin may use its filled icon instead of an active background. Archive stays
neutral because archived threads are recoverable. These rows carry a details card,
so their tooltips take the block axis (see Hover annotations).

### Shared icon actions

Recurring icon actions are single shared components, so every surface draws them
the same way:

- **Copy** is frameless on every surface, including error blocks and diagram
  toolbars. It confirms in place by swapping to a `--success` check and
  relabelling its tooltip, with no toast. A labelled copy action (Copy link in a
  dialog) is an ordinary button.
- **Dialog close** is a `30px` borderless button with a `16px` X and neutral hover
  feedback, top-right, aligned with the header badge row. A dialog may place it
  but never recolor or fill it.
- **More actions** is the horizontal ellipsis at `16px`, announced as a menu
  trigger with its expanded state and a tooltip repeating its label. It takes the
  footprint of its band and opens the shared context menu; a feature does not draw
  its own bordered popover.

### Compound triggers

Compound triggers combine a principal action with a menu of related commands. Both
segments share one intent and one size and meet flush: the group clips the outer
corners while each segment drops the radius and border on the shared edge, so the
pair reads as one control without a doubled seam. The outline variant reads as one
neutral outer frame. Hover changes only the hovered segment. The menu glyph sits at
reduced opacity so the chevron reads as an affordance rather than a second action.

Emphasis is carried by intent: `primary` for the principal action of a surface
(the catalog create control is the reference), `secondary` for quiet compound
triggers among other chrome.

Use the shared split button rather than composing a pair, chevron, and menu per
feature, so geometry, keyboard navigation, outside-click dismissal, and focus
restoration stay identical. A compound trigger takes the height of its band.

### Status menu buttons

A compact status menu button combines a current-state label with an overflow menu
when a repeated row would otherwise expose several competing actions. It is a
state affordance, not a second primary action:

- the trigger takes its surface's band and carries the shared status indicator, a
  concise neutral label, a trailing chevron, and a persistent `1px`
  `--border-default` outline;
- hover and open may strengthen the neutral fill and border together, never into
  an accent border;
- it opens the ordinary shared menu; destructive commands remain explicit danger
  items and require confirmation when they revoke durable authority or delete
  data;
- a required next step such as Install, Connect, Add, or Review stays a direct
  button instead of hiding in the menu;
- loading disables the control and shows one in-control progress signal;
- keyboard open/close works and focus returns to the trigger after the menu
  closes.

The visible frame is a deliberate exception to the frameless rule because the
trigger combines status and menu. Workspace-level app connection rows are the
reference: `Connected` combines status with Reconnect and Disconnect. A
reversible on/off choice uses a pill switch instead.

### Scrollbars

A scrollbar is a control, sized by what the pointer must catch rather than by how
much ink it spends. `--scrollbar-size` is the grab target and
`--scrollbar-thumb-inset` insets the painted slider inside it, so the bar reads
quiet while staying easy to grab. This matters most at a window edge, where a
frameless window reserves a resize border; a target no wider than that strip is
caught by the window, not the thumb. The thumb carries a minimum length so it
stays grabbable on long documents.

Three neutral states: `--scrollbar-thumb` at rest, `--scrollbar-thumb-hover`
under the pointer, `--scrollbar-thumb-active` while dragging. Tracks and corners
stay transparent.

Features do not set `scrollbar-width`: Chromium treats it as overriding the shared
geometry entirely. Use it only to hide a scrollbar deliberately (`none`); a region
that needs to reserve the gutter uses the shared stable-gutter treatment.

## Status and loading

### Status indicators

A status indicator is one glyph that says what state something is in, read with
the label beside it. Every surface uses the same one.

The shape never varies: an 8px non-shrinking circle centred in a fixed 14px box.
The box does the alignment, so indicators in adjacent rows line up without
per-use nudging, and a spinner or icon can take the indicator's place without
moving the text.

The indicator precedes and belongs to the label it describes. Colour lives in the
indicator alone: the label stays `--text-secondary`, and a failure reason follows
as ordinary neutral text.

Five fills carry every state:

- `--success` for the state worth noticing: running, in use, connected where being
  connected is the point;
- `--warning` for degraded but recoverable, and for work waiting on a decision
  the reader has to make;
- `--error` for failed or blocked;
- `--info` for a step the reader confirms rather than judges;
- `--text-dimmed` for every quiet state (ready, idle, offline, unknown), which the
  label distinguishes in words.

There is no hollow, dashed, or translucent variant. A transitional state
(connecting, testing, restarting, a turn still running) shows the shared spinner
in the indicator's box rather than a colour, because a colour would claim a result
that does not exist yet; the spinner reads at the row's own text colour, the same
in a background project's rows as in the foreground's, never the dimmed tone of a
row's timestamp. When the
label does not already name the state, the indicator carries its own accessible
name.

### Badges

When the state is the whole content rather than an attribute of a row, use a
badge: the tinted surface tokens (`--success-bg` and siblings) with the reading
ink (`--success-text` and siblings), and no indicator inside. A badge and an
indicator never appear together for the same fact.

The tint is the whole badge; it carries no border, because a frame makes a state
look pressable. In a dense list the badge takes the compact size (the row's
secondary type at ordinary weight in an `18px` pill), sits inside the row's
height, and truncates rather than pushing the title. While the row is hovered or
selected the badge goes neutral. A state's hue comes from the fixed status tokens,
never `--accent`.

### Reporting only trouble

A settings surface says nothing when everything is fine. An all-clear badge, a
Status section that only ever reports health, or a confirmation the reader never
acts on does not earn its room. Show trouble instead: a notice at the top of the
page, before the settings it explains, naming what is wrong and the way out.
Merely quiet states stay in their row, as an indicator or plain secondary text.

Words that classify rather than report (default, custom, customized, a tier name)
are labels, not status: no pill, border, or fill. Set them in the hint size on
`--text-dimmed` beside the title they qualify, and drop the ones a section heading
already says.

### Loading and progress

Loading is communicated by a placeholder shaped like the content that will
arrive, not by a generic spinner or a "Loading…" label. The shared skeleton is a
`--bg-tertiary` block on a pulse; the pulse is the running signal.

- Known-shape content (a plan, a list, a card grid) gets a shape-matched skeleton.
  Reserve the spinner for shapeless, indeterminate waits inside a control: a busy
  button, an inline refresh, a connection check, a running turn in a list row.
- There is one spinner: a track ring carrying a three-quarter arc, both in the
  current text colour at a twelfth of its diameter, turning once per
  `--animate-spinner`. It borrows the ink of whatever it sits in, so no wait claims
  the accent. Size it to its box; never give it a hue, a thicker ring, or a second
  animation.
- Partial content renders as it arrives. Keep pulsing skeleton rows only for what
  is still streaming; never hold arrived content behind a spinner.
- One running signal per surface. If a surface already shows it is working (a
  shimmering label, growing diff text, a streaming caret), do not add a spinner or
  an elapsed-time counter beside it. The running tool row keeps only its shimmering
  label and, for file edits, its diff stats; elapsed time belongs to the turn
  activity line.
- A shimmering label dims to `--shimmer-base` and, every four seconds, carries one
  highlight that reaches `--shimmer-peak` only at its centre. The band is half the
  label's width up to 80px and crosses at a steady 450px per second, taking at least
  one second. Under reduced motion the label stays dimmed and still.
- Loading regions are marked busy; content-free skeletons announce the loading
  state, and the skeleton blocks themselves are hidden from assistive technology.
- Reduced motion stops the pulse but never the meaning: the skeleton still reads as
  a placeholder.
- A page load in the in-app browser runs a 2px accent bar along the toolbar's
  bottom edge, pulsing on opacity and hidden from assistive technology; the
  Reload/Stop control carries the state. Under reduced motion it is a static rule.
- Opening a thread before its record arrives shows only a centred 20px spinner in
  `--text-secondary`, hidden for its first `400ms` so a quick switch paints
  nothing. It has no caption.
- An unloaded stretch of transcript history is a `144px` blank gap with no skeleton
  or spinner. It loads well before it reaches the viewport, and a skeleton of turns
  not yet read would promise a shape it cannot keep.

The workspace launch transition has no caption: the brand mark alone is the
running signal. Light rises up its body gradient as one wave every 3.6 seconds,
leaving the face colours alone; the mark never changes size or moves. The wave
animates opacity only, and the loop rests on the plain mark at its ends, so the
handoff from the Welcome logo never pops and reduced motion leaves the mark still.

## Overlays

Every overlay sits on the opaque elevated surface and follows the border rules in
Surfaces and elevation. Tooltips are the one exception: they take the inverse pair.

### Menus, popovers, and pickers

Floating menus, context menus, select dropdowns, compact popovers, and command
palettes share one overlay language:

- one solid, opaque elevated surface with no gradient or translucency, so every
  menu looks identical regardless of backdrop;
- menus and popovers share one stacking layer (`--z-popover`); whichever opened
  last sits on top, and only tooltips (`--z-tooltip`) rise above it;
- a popover anchored near a panel edge opens toward the panel's interior so it is
  never clipped;
- frames are borderless and separated by shadow; a submenu, flyout, or stacked
  overlay carries one hairline on the overlapping edge only;
- a submenu opens beside its row on whichever side fits, stays inside the window,
  and stays open while the pointer travels toward it through the triangle between
  the pointer and the submenu's near corners;
- rows are borderless at rest; hover, open, highlighted, and selected rows use
  neutral background elevation;
- focus-visible rings remain available for keyboard accessibility.

The thread sidebar and thread-header overflow menus are the reference treatment.
Active channel bindings show as a quiet pill beside the thread title (channel icon
and chat name).

Ordinary text-only field selects may widen toward the left when opened so the
longest option is readable without a tooltip. The trigger finishes its width
transition before the menu appears, so option text never reflows while visible;
the width is capped to the viewport and extreme labels wrap. Rich options with
icons or descriptions, and frameless toolbar selects, keep a fixed width. Reduced
motion skips the staged animation. A rich menu may set a minimum width so each
description stays on one line, opening past its trigger; one at the trailing edge
of a settings row keeps its right edge on the trigger's.

### Tooltips

A tooltip is the one overlay that is pure annotation: it takes no pointer events,
disappears on the next pointer move, and is never a place the user travels to.

- The surface is the inverse pair (`--bg-inverse` / `--text-on-inverse`). A label
  does not belong to the plane it labels; a same-tone tooltip in the light theme
  separates from the app at about 1.05:1.
- No border; the inversion is the boundary and the shadow carries the lift.
- Type is `--type-secondary` (12/16); the keycap chip is sized to its 16px line.
- Copy stays short. The single-line form clamps at 320px with an ellipsis; only a
  genuine explanation takes the multiline form, which wraps to 32rem.
- A tooltip inside a row that carries a details card takes the block axis (see
  Hover annotations).

### Keyboard shortcuts

Shortcuts, in tooltips and everywhere else, use one continuous chip:

- one chip per shortcut, keys joined by `+` (`Ctrl+Shift+M`), matching menu rows;
- alternate shortcuts stay separate chips divided by `/`, so "Enter or Shift+A"
  never reads as one chord;
- the chip is a flat 16px box in the UI face at `--type-hint`, with no keycap
  emboss or per-key frames;
- each key remains its own keyboard element for assistive technology; the joins
  are drawn, not typed.

### Hover annotations

A tooltip names the control the pointer is on. A details card describes the row's
subject: its project, its branch, when it last ran. Metadata that only identifies
a row lives in the card instead of in a column the list must reserve (see
Selection rows). Both are true at the same moment, so they coexist: closing the
card when the pointer reaches the row's actions would blank out the context the
reader is acting on.

Each annotation owns one axis:

- a row's details card owns the **inline** axis: `right`, mirroring to `left`
  when the card would leave the viewport;
- every hover-revealed control inside that row owns the **block** axis: `top`,
  mirroring to `bottom`.

Mirroring happens inside an axis, never across it. A row's action rail sets the
axis once for all its actions. Menus and popovers opened by those controls are
places the pointer travels to and keep their own positioning. Any menu, including
a row's context menu, is a layer above the row: opening it closes the row's
details card and tooltips, and they stay closed until the menu does.

A tooltip is placed against the control's drawn box. An out-of-flow rail action
must still anchor to what is drawn (or reserve its footprint), not to a collapsed
wrapper that would place the label at the row's centre.

The card is an ordinary elevated surface, not the inverse pair, because the
pointer can travel to it. It tucks under the row's trailing edge with the single
hairline on that overlapping edge, opens on a delay, and closes after a short
grace period.

### Toasts

Toasts are transient cards in the top-right stack. A toast is either present or
gone: there is no remaining-time bar, hovering or focusing the stack holds every
card, and a repeated identical notice replaces its twin.

- Levels are `info`, `success`, `warning`, and `error`. Errors auto-dismiss like
  everything else; a toast that must persist says so explicitly.
- `info` is the neutral card. The other three tint the whole card (surface,
  border, title, icon, close, action), so an outcome read in passing does not
  depend on a small glyph. The tint is mixed into the elevated surface, keeping
  the toast opaque. Readable content uses the level's `-text` colour.
- A toast reporting the outcome of something that already showed an in-flight
  toast replaces that notice.
- An arrival is not an outcome. A toast offering something newly possible (a
  plugin just installed, offering Try now) stays the neutral card with that
  thing's identity mark in the leading slot. Tinted levels report a finished
  outcome and keep the level glyph. Either way the title names the subject.
- A card is as wide as its content against the stack's right edge, capped at the
  column width.
- The title is one medium-weight line in a 24px box, matching the icon, action,
  and close, so a one-line toast is a 42px card. A description adds a quieter
  second line, and the actions then move to their own row below.
- The action is a filled `secondary` pill, tinted to the level on a tinted card;
  it is the one thing on the card that may carry a fill. Close stays a frameless
  icon button.
- Offer inline Undo only when a compensating server call exists (archive and
  unarchive). Perform the change immediately and let Undo reverse it; never defer
  it until the toast expires. Permanent deletes keep their confirmation dialog and
  offer no Undo.
- Offer a forward action such as Try now only when the notice itself made that
  destination reachable in one press.
- The stack is one polite live region; individual cards do not raise alerts.

### Dialog headers

Dialogs with an identity icon share one header treatment:

- The icon sits in a neutral rounded badge: a ~36px square, `8–9px` radius,
  `--bg-tertiary`, glyph at `18px` in `--text-secondary`. A bare icon is not used.
  When the subject has its own artwork (a skill or plugin avatar), that artwork
  takes the badge's footprint instead of nesting inside it.
- The title sits below in `--type-heading` (weight `600`, `--text-primary`); never
  hero-scale type.
- An optional one- or two-line description follows in `--text-secondary`.
- Close is the shared dialog close (see Shared icon actions). A dialog-level
  overflow menu sits to its left, not beside the title.
- A dialog previews its subject; it does not double as a place to change its
  state. Enable/disable switches stay in the manage surface that owns them.

The badge stays neutral unless the dialog's whole purpose is a semantic state.

### Dialog layout

Modal dialogs share one frame: the scrim, a neutral elevated frame, and a `24px`
inset on every side. The header and footer stay fixed; only the body between them
scrolls. The body reserves a stable gutter on both edges so its content keeps the
same `24px` inset as the header and footer while the scrollbar sits in the frame's
right inset, never against the content. A bounded list inside a dialog may scroll
on its own; the frame itself never scrolls. Command palettes and media viewers are
not dialogs and keep their own shells.

### Dialog actions

A dialog footer follows the Action hierarchy.

Transient choice dialogs (short-lived destination and branch/changelist choices)
may omit a visible Cancel when backdrop click and Escape both dismiss safely and
no operation is running. Destructive confirmations, long forms, edit modes, and
running/error recovery flows keep an explicit Cancel or Close.

Workspace onboarding keeps its circular step navigation and selection cards,
which express progress or choice; only its regular actions use the shared button
variants.

### Reply selection overlays

Reply text selection actions form a content-sized horizontal strip with a subtle
internal separator and no reserved action slots, on the opaque elevated surface
with neutral hover and compact controls. Its comment editor is a separate width
mode. One active overlay per window follows the selected range and stays inside
the viewport. Reply text context menus use native platform chrome and editing
commands.

## Inputs

Text inputs, textareas, selects, search boxes, and picker triggers stay neutral:

- use `--bg-primary`, `--bg-secondary`, or dedicated input tokens such as
  `--composer-input-background`;
- use `--border-default` for the rest state;
- use `--border-active` for the focus border, never `--accent`;
- use `--text-primary` for values and secondary/dimmed tokens for placeholders;
- do not use brand or semantic fills for ordinary input backgrounds.

### Focus and hover

A field's focus indicator is its own border, and nothing else. Focus moves the
border to the neutral `--border-active`; it never turns accent and never adds an
outline, ring, or glow, which would read as a second frame. Typing is not an
accent moment. The global focus-visible outline remains only for elements with no
focus treatment of their own. This holds for every field shape below and for
composed fields.

Fields carry no hover state. The pointer usually rests inside the field it just
focused, so a hover border would hide the focus border exactly when it matters.
Whatever a field's resting shape, the focus border wins while it has focus.

### Field shapes

Three shapes exist, the first two `--bg-primary` with an `8–10px` radius:

- bordered at rest with `--border-default`, moving to `--border-active` on focus.
  This is the default, and dense multi-field forms use it.
- frameless at rest, showing the `--border-active` border only on focus. A simple action
  dialog's single message or objective input (commit message, thread goal) is the
  reference.
- bare: no frame, fill, or sizing of its own, for the inner field of a composed
  control (a search row, a combo box, an input with a trailing action). The shell
  paints the frame and owns the focus state. A bare field never appears alone.

Validation combines copy, border/icon treatment, and semantic tokens without
taking over the form.

### Shared field components

Desktop-owned text fields, textareas, selects, checkboxes, and editable
suggestion fields use the shared field components, so height, radius,
placeholder, focus, invalid, disabled, menu, and keyboard treatments stay
identical. A field owns its height; callers place it and pass only genuine
deltas. A native control remains only when it is visually hidden and supplies
semantics, or is an inline editor in a canvas surface that already follows the
focus rule. Third-party content in sandboxed views is outside this rule.

Desktop-owned colour choices use the shared colour picker dialog, never the native
colour input. It contains a Hex field, a saturation/value plane, a Hue control,
and explicit Done/Reset actions: edits preview, Done commits, Reset clears the
override, and every other close path cancels. Opened from a control, it anchors
beside that control, previews live in it, and restores the original value on
cancel (including on scroll or resize). The system file chooser stays native.

### Search and filters

Search is one composed control: a pill-radius shell with the search glyph and a
bare field. When filters exist, one compact filter icon sits beside the shell at
its height and opens filter dimensions as submenus; pages do not put a row of
selects beside search. While any filter applies, the icon shows the neutral
active tone. Filter dimensions use leading icons only when every peer has one;
the selected option uses a trailing check.

### Editable titles

An always-editable page or detail title reads as text rather than a form box. It
stays frameless while focused; the caret and selection communicate editing.

### Headings that name a selection

An entry heading may name the place the next action runs and let that name be
changed in place, rather than repeating the choice in a control beside it:

- the name keeps the heading's family, size, weight, colour, and spacing, and
  gains no pill, chevron, border, or background;
- a `1px` dotted underline in `--text-tertiary` at a `4px` offset is the only
  resting affordance; hover and open move text and underline to
  `--text-secondary`;
- punctuation stays outside the underline in every locale;
- the heading and any control offering the same choice open the same menu;
- the menu opens above the heading, away from the input below, and flips only when
  there is no room;
- with nothing to name, the heading drops the clause and the control beside the
  input asks for the choice. The heading never names a place that does not exist.

An entry heading carries no second line repeating what the input's placeholder
and state already say.

## Composer

- At narrow widths, the input card keeps one action row. It first reduces
  secondary labels and model details to icons, then removes optional context,
  model, goal, and mode controls as space requires. Command, permission,
  microphone, and send or stop stay available. The context row below the card
  stays on one line the same way, dropping secondary labels and status before
  project and location, with accessible names kept for icon-only controls.
- The primary composer uses `--composer-input-rest-border`, so the light theme
  has a subtle frame while the dark theme can stay effectively frameless. Focus and
  hover leave that frame unchanged; the caret is the focus cue.
- Approval, question, and plan-confirmation composers take the composer's place
  with the same frame.
- The action row holds model, context-window, and send. The context row below the
  card holds project, Run on, work location, branch or changelist, and provider
  subscription status (right after the branch). Context-row chips share one 28px
  footer pill in `--composer-footer-text` and open their menus upward.
- Every capsule in either row is transparent at rest, takes
  `--sidebar-control-hover` on hover and keeps it while its menu is open, so
  opening a menu does not change the fill, and shows an accent outline only for
  keyboard focus. The label keeps its
  colour; there is no pressed fill and no opaque fill.
- Send is one 32px button whose glyph follows the turn: Send at rest or with a
  draft to steer or queue, Stop while a turn runs with an empty draft, a spinner
  while stopping. The glyphs crossfade on opacity in one cell, so the control
  never moves or scales.
- The `+` trigger and microphone are round, frameless icon controls with the
  composer hover fill and a neutral fill while open or recording, at the height of
  their neighbours.
- Voice duration and its microphone/stop button form one non-shrinking, centred
  group with an 8px gap. The duration keeps a 32px minimum width, tabular
  numerals, right alignment, and no wrapping, independent of toolbar gaps. The
  waveform takes the remaining width; the primary action stays outside the group.
  Voice buttons are 32px standard and 28px compact.
- Footer mode pills (Plan, Goal, a custom agent) are frameless 24px pills with an
  8px inset on both sides. At rest they show their mode icon; on hover or focus
  they take the hover fill and the icon becomes a circled clear mark. The
  current-goal status pill shares the inset and radius.
- Line comments and annotations each ride one attachment pill above the input:
  hairline border on the elevated surface, tertiary icon, and the count as a medium
  label, with a remove control revealed on hover or focus. Hovering or clicking
  opens its list above, start-aligned, 384px wide and at most 320px tall, with
  hairline-divided rows. A comment row shows its file reference, side, and line on
  one secondary line above the text; annotation rows lead with a tertiary ordinal.
  Row actions appear on hover or focus.
- Composer-adjacent activity docks use `--background-activity-dock-background`,
  close to the composer background with soft translucency; attached to the
  composer, they omit the bottom border on the shared edge, and the composer draws
  a `--composer-top-accessory-separator` hairline there.

## Conversation

The transcript uses the Conversation type scale. Icon actions on its rows take the
transcript row footprint, and framed actions on its cards use `outlineGhost` (see
Buttons).

### Floating transcript controls

Controls that float over the transcript bottom, the jump-to-latest button and the
running changes indicator, share one quiet surface: `--bg-primary` at 70% over a
soft blur with a `--border-default` hairline and no drop shadow, so they read as
the conversation's own background rather than raised chips.

### Message markers

A user bubble holds only what the person wrote. Everything the client knows
*about* the message lives outside it.

Origin goes above the bubble: a right-aligned line of small icon plus label in
`--text-tertiary`, with no pill, border, or fill, naming where the turn came from
(another thread, an automation). A message that steered a running turn carries
no origin line; its place in the turn already says so. When the origin has a
destination the line is a quiet action that lifts to `--text-secondary` on hover;
otherwise it stays inert.

A turn with no bubble, started because a background task ended, carries its origin
on its activity boundary instead: a 13px icon of the task's kind (terminal for a
command, the workflow glyph) 6px before the timing label, in its colour and
neutral whatever the outcome. A subagent shows its own 14px avatar there, so the
boundary names which agent woke the turn without adding words. On hover the icon and timing name the task
and how it ended; the icon is a quiet action that reveals the originating tool call,
while the timing still toggles the disclosure. The boundary keeps its single row,
so a woken turn reads as a peer of the turn above it.

Special state goes into the message action row below the bubble, after the
actions, as the same small icon plus label. State is information, so it stays
visible at rest while the timestamp and copy controls stay hover-revealed.

Tooltips on these markers carry only what the visible line does not already say,
so the part that matters (the originating thread, the job name) is not pushed past
the clamp. A marker with no extra detail has no tooltip. Accessible names keep the
full sentence.

### Block labels

The label above a transcript block (`Plan` on a plan card, `Created` on a
scheduled task, `Loaded` on a skill) names what the block is; it is not a status
chip, so it carries no pill, border, or fill. A label above a title stays below it
in weight. While the block runs, the label shimmers on its own text rather than
gaining a badge.

A label that shares its row with the block's controls forms one header row: label
left, actions right, on one vertical centre, rather than controls floating over
the card.

### Tool rows

Each tool row leads with a 16px activity icon, 6px before its summary, flush with
the reply's text edge. A group row takes the icon of the first kind it names, and
its expanded rows sit flush beneath it. The icon takes the row's text colour,
resting dimmed and lifting with the row on hover; only a declined command tints it,
in `--warning`. A failed call's row stays neutral; the failure reads in the
expanded output. Rows that already lead with an identity mark (subagents) and
cards with their own surface take none. The summary stays one line and ellipsizes.

A Thought row takes the same slot with a line icon at the tool icons' stroke
weight: three drops on an orbit around the core. It stays static while the
reasoning streams, because the turn's working line already shows that the turn is
thinking.

The working line leads with the thinking glyph, drawn in the row's text colour:
while the turn works, three separate drops gather, fuse into a core that swells,
and burst apart on a 3s loop; reduced motion shows one still round dot. When the
turn stops, the glyph fades out at once. Its liquid edge softens with the rendered pixel size so a 16px glyph
keeps an antialiased outline.

A running-task count on the working line reads as a quiet action in the line's
colour, lifting to `--text-primary` on hover. It
leads the label with a `·` between them so the two never read as one phrase; the
label ellipsizes and the count never does. On a stopped turn the count stands
alone behind a 6px dot that pulses in the glyph's slot and holds still under
reduced motion.

| Activity | Icon |
|---|---|
| Command run | terminal |
| Interrupted command | stop |
| Stopped background task | stop |
| Declined command | shield, `--warning` |
| File or skill read | open book |
| Text search | magnifying glass |
| File listing | folder |
| File or skill edit | pencil |
| Memory update | brain |
| Viewed image | image |
| Web search or fetch | globe |
| Discussion board read or change | speech bubbles |
| Any other tool | wrench |

Tool output images never nest in a tool group. The calls that produced them keep their rows in the group, while one `Viewed an image` / `Viewed N images` disclosure sits below the run, collapsed to its summary.

A web search row is one two-tone line: the action in tertiary text at 90% (under
the running shimmer while it runs), then the query, URL, or pattern in tertiary at
80%, both lifting to `--text-primary` on hover. A completed search with results
expands to a quiet source list, one line per source with the domain in
`--text-secondary` before the title in `--text-dimmed`, with no table, header, or
borders. A search without results does not expand; a failed one expands to its
error.

### Inline reference chips

File, command, skill, link, scheduled-task, and profile references (in the
composer, sent bubbles, markdown, and tool rows) are quiet inline content: a type
icon and a tinted label, with no border, fill, or pill at rest or on hover. An
interactive reference lifts to `--text-primary` on hover, and one that navigates
adds a 1px dashed underline offset 2px. A composer pill instead reveals its
neutral remove affordance in the same fixed icon slot, swapping on opacity, so
hover never changes chip width, baseline, caret, or surrounding text. Inert
references do not react to hover. Use vector icons, not font-dependent Unicode
glyphs.

The tint is not a per-type code. Everything that goes somewhere carries one
reference colour (`--ref-base`) and lets the icon name the type; skills, a
capability rather than a destination, take `--ref-skill`; an inert value carries
no tint. Both sit near the text lightness and clear 4.5:1 on their surface.

A click leaves nothing behind. Keyboard focus is the only state that draws a ring,
through the shared focus-visible outline; a mouse click never paints one.

### GitHub references

A link to a GitHub pull request or issue is a reference like any other link, with
two differences. The icon names the item's state (open, draft, approved, changes
requested, merge conflicts, merged, or closed for a pull request; open, closed, or
not planned for an issue) and is the one part drawn in that state's colour. The
label is `#N · title` for the workspace's own repository and `owner/repo#N ·
title` otherwise, with the title cut at a word boundary near 32 characters at
regular weight. Author-written link text is kept and gains only the state icon.
Until the state is known the chip shows a neutral icon and the number.

State colours reuse the status reading tokens: open and approved
`--success-text`, closed and changes requested `--error-text`, merge conflicts
`--warning-text`, draft and not planned `--text-dimmed`, merged `--git-merged`.
They never spread past the icon and the card's state badge.

Resting on the chip opens a hover card below it (mirroring above when needed),
with the details card's open delay and close grace: an ordinary borderless 360px
elevated overlay the pointer can enter. Its header carries a state badge (icon and
label on a 16% mix of the state colour; draft and not planned on `--bg-tertiary`),
`owner/repo #N`, and the item's compact age. The title follows at body size,
semibold, clamped to two lines; the footer names the author with a 16px avatar and,
for a pull request, diff stats and file count. Loading draws the card's shape in
skeletons; any failure (missing or signed-out CLI, rate limit) collapses the card to
one muted line saying what to do.

A branch's pull request uses the same vocabulary away from prose: a pill of the
state glyph and `#N` after the composer's branch control, the glyph alone in a
sidebar thread row's status slot, and a `#N · title` action row in the thread's
details card. Checks ride on the glyph as a dot cut into its lower trailing corner
(`--success-text` passed, `--error-text` failing, `--warning-text` running, none
when there are no checks or the pull request is merged or closed), kept inside the
glyph's own box. The card adds one line naming the checks' state with
`passed/total` dimmed at the trailing edge, led by the shared spinner while checks
run.

### Link previews

A web link in prose keeps its ordinary reference chip. Resting on it opens the same
card as a GitHub reference (same delays, placement, surface, and 360px width,
never wider than the window less 8px a side), so a reader learns where the link goes
without leaving the conversation. The card replaces the link's URL tooltip.

The header is one 12px `--text-secondary` row: the site's 16px favicon (a globe
when there is none), the host without `www.`, and a quiet `Open ↗` action at the
trailing edge. A preview image follows full-bleed at 1.91:1, cropped to fill; a
page without one draws no image area rather than a placeholder. The title closes
the card at body size, semibold, clamped to two lines. While loading, the header is
already real (the host comes from the URL) and the image and title are skeletons. A
failed or refused lookup keeps the header and adds one muted line saying why; a
resource with no title shows its URL in that muted style.

Keyboard focus opens the card at once. Its one tab stop is Open: Tab from the link
enters it, Tab again continues after the link, and Shift+Tab or Escape return to
the link. The pointer may cross another link on its way into the card; the card
switches only when the pointer rests there.

### Markdown tables

A markdown table is frameless: a rule under the header and hairlines between rows.
Each column takes a minimum width from its longest cell (4, 6, 8, or 14
twenty-fourths of the reading width for up to 40, 100, 160, or more characters),
and a table wider than its column scrolls sideways rather than breaking short
words.

### Math

Formulas use KaTeX typesetting in the surrounding text colour. A display formula
is centred on its own line and scrolls sideways when wider than the column, never
wrapping or shrinking.

### Visualization

Assistant visualizations are conversation-native media, not tool cards. Their host
is transparent and unframed, with no header or attribution row. Host actions (Copy
as image, Open fullscreen, and Design controls when the view registers any) are
borderless tertiary 24px icon buttons stacked 4px apart in a narrow rail just
outside the content edge, never covering the media and never behind an overflow
menu. They appear on hover, keyboard focus, coarse-pointer devices, or while the
design controls are open, and are excluded from the copied image.

A wide visualization (a full application shell or side-by-side small multiples)
breaks out of the reading column up to 1024px of content plus its rail, centred
where there is room. It never covers the conversation's side gutters, the turn
navigation rail, or an occupied aside; with no free room it fills the column.
Everything else keeps the reading width.

Visualizations load lazily near the viewport. Before that, reserve the expected
shape without animation; from the first runtime request until the view is ready,
show one animated, shape-matched skeleton and no spinner.

Desktop gives the visualization document the active surface, text, border, focus,
accent, font, control-band, and state tokens, so every sandboxed control mirrors
the shared Desktop control it stands for (button, field, select, checkbox and
radio, pill switch, slider, status badge, data table, tooltip) and follows theme
and appearance changes. Selects and colour inputs open the shared Desktop menu and
colour picker beside them, not platform popups. Scrollbars follow
[Scrollbars](#scrollbars); a view that scrolls in the fullscreen viewer reserves
the gutter on both edges. Feature colors remain available for charts and diagrams,
not ordinary controls: the named hues follow the chart series order, so a series
color never disagrees with its named hue. A visualization popover is an annotation
and uses the inverse tooltip surface.

- Tooltips follow [Tooltips](#tooltips), sit 6px from the trigger, prefer the
  author's side, flip when there is no room, and stay 4px inside the view.
- Tabs are quiet pills with the ghost button's geometry: muted at rest, neutral
  hover fill, the selected tab in the active neutral at weight 500, no track.
- A color input is a 24px swatch with a 6px radius and a hairline border.
- A selected tile keeps its fill and gains a 2px accent ring inside its edge,
  never a solid inversion.
- A progress bar is a 6px rounded neutral track with an accent fill.
- The day calendar is one card surface. Events are flat soft fills of their tone,
  with no side bar, border, or gradient, `8px` radius, `8px` between lanes, `2px`
  between stacked events; finished events halve their tint. The now marker is a
  2px red line with a dot at the time gutter.
- A contained mockup may sit on a quiet dot grid; full-page mockups, charts, and
  explainers stay on the plain surface.
- A variant carousel's controls sit centred below the designs in reserved space:
  borderless 28px icon buttons, a frameless picker of design names, and a muted
  tabular "n / total" count.
- A runtime error is reported below the view, never over it: one meta-size line in
  `--text-secondary` with a 14px `--error-text` alert icon and a ghost fix action.
  One notice per view.

Design controls follow [Menus, popovers, and pickers](#menus-popovers-and-pickers):
an opaque borderless elevated panel with a 10px radius. Inline, it docks below the
view at the view's width and never covers content; in the fullscreen viewer it
floats top-right, 288px wide, scrolling past 440px. Rows are borderless 32px rows
with the label in secondary text and the control trailing. The footer carries
Show original (a pressed toggle), Reset, and a primary action that adds the
changes to the message; all stay disabled until something changes. Added changes
ride the composer as one attachment pill per visualization. The panel never opens
on its own.

The fullscreen viewer follows [Image preview](#image-preview) for scrim, paging,
and closing. The view sits centred on a `--bg-primary` panel (12px radius, 16px
padding, up to 1024px of content) and scrolls inside past the stage height. The
top-right toolbar pill holds the title, Design controls when registered, Copy as
image, and Exit fullscreen. The viewer sits below dialogs and toasts.

### Generative UI

Generative UI answers render natively in the reading column with Desktop's own
components, never in a visible frame, with no header, attribution, or host chrome.
The answer sits 8px from the surrounding Markdown and spans the column width.

- **Surfaces.** A card is one `--bg-secondary` surface with an 8px radius, no
  border or shadow, 16px padding (12px compact, 20px roomy, or none), and 12px
  between children. A box may take `--bg-secondary` or `--bg-tertiary` and a 6px
  or 8px radius; nothing else adds fills.
- **Layout.** Rows and columns default to an 8px gap, up to 64px. Column children
  stretch, except buttons, badges, icons, and images. Rows of buttons or badges
  wrap rather than leave the card; a button never outgrows its container and
  truncates instead. Text wraps at word boundaries.
- **Type.** Body uses the conversation scale; captions and table text use the meta
  and secondary sizes on `--text-secondary`. Large figures use the detail-title and
  title sizes with tabular numerals. Tones map only to `--text-*` and the semantic
  `-text` inks.
- **Controls.** Every control is the shared production control (button, field,
  select, checkbox and radio, segmented control, slider, status badge). Field
  labels sit 6px above the control in secondary medium type; hints follow in
  `--text-dimmed`, errors in `--error-text`.
- **Data.** Tables are frameless: secondary-size rows divided by `--border-subtle`,
  a medium `--text-secondary` header, right-aligned tabular numbers, and
  horizontal scrolling instead of squeezing.
- **Charts.** Series follow the chart palette in order. Grid lines are horizontal
  `--border-subtle`; axes have no lines and use 11px `--text-dimmed` ticks thinned
  to avoid collisions. Strokes are 2px, bars have a 4px top radius, donut slices
  have a 1° gap, the tooltip is the inverse surface, and a legend appears below
  when there is more than one series.
- **Icons.** 16px with a 1.75 stroke in the current text color or a `-text` tone;
  the box is reserved while an icon loads.
- **Images.** Only embedded images render, 8px radius by default; a failed image
  becomes a `--bg-tertiary` placeholder with its alt text.
- **Loading.** Until the first snapshot (or while the block waits to scroll near,
  or its closing fence has not arrived), one card-shaped skeleton holds the place.
  No spinner.
- **Failed.** One quiet notice in the Turn errors form, with the stage, position,
  and message as a dimmed meta line clamped to two lines and a secondary Retry.
- **Locked.** While the message streams or the turn runs, controls show their
  disabled state at full opacity; the block is never dimmed.
- **Inert.** A block that fails after rendering keeps its last frame at 60%
  opacity with every control disabled, the failure notice 12px below.
- **Invalid nodes.** A component that fails validation is replaced in place by one
  meta-size line (a `--error-text` alert icon, the component name and reason in
  `--text-secondary`, truncated, full reason in the tooltip). The rest renders
  normally.

### Image preview

Generated, attached, and tool output images open in one fullscreen preview over a
dark scrim. It pages through every image in the conversation with previous/next
buttons in side gutters and arrow keys. It opens at Zoom to fit. Download and Close
are separate round buttons at the top right, below the Windows caption buttons; a
bottom pill holds zoom out, the current percent as plain text, and zoom in.
Ctrl/⌘ + wheel and pinch zoom between 10% and 400% around the pointer, dragging
pans a large image, and `+`, `-`, `0` zoom in, out, and to fit. `Esc` or a scrim
click closes it; a press that moves more than 5px is a drag, not a close.
Switching images returns to Zoom to fit.

An image's context menu offers open, add to chat, copy, reveal on disk (only when
saved on this machine), and download a copy.

### Turn errors

A turn error is one quiet notice, not a red card: `--bg-secondary`, 8px radius, a
14px alert icon in `--error-text`, and the message in `--text-secondary` as
selectable text. Its top trailing edge holds one action: a secondary Retry whose
countdown fills the button when the turn can be retried in place, otherwise the
frameless copy button. A capacity or throttling failure shows a short localized
sentence instead of the raw message. Reaching a usage limit is a neutral timeline
divider instead.

### Run outcomes

A background run's detail view states how the run ended with at most one quiet
notice under its header, in the Turn errors form: a stopped run is neutral (stopped
glyph in `--text-tertiary`, elapsed time in `--text-secondary`); a failed run uses
the `--error-text` alert icon and the reason; a completed run shows nothing. A stop
is never presented as an error.

### Task cards

An offer to start a task in its own chat, and tasks the agent suggests, are one
card in the transcript at the position of the calls that raised them, in place of
their tool rows.

- The card is the created-chat surface (`--bg-primary`, `--border-default`, 8px
  radius) with a quiet header band: the new-chat glyph, the question or the
  suggested-task count, and at most one ghost action on its trailing edge (Don't
  ask again, Start all). Each task is a row below a hairline.
- A row is the task title in medium weight, then its actions: a ghost Options on
  the leading edge, a ghost Do it here and a primary Start or New chat on the
  trailing edge. An offer shows Esc and the platform Enter chord on those two
  buttons. Only settings that differ from this chat appear beside Options.
- Options opens a glass popover holding the Welcome composer's own permission,
  model, and location controls, so a new chat is configured exactly as from the
  Welcome screen. Clicking a suggested task's title opens its brief in the same
  popover surface.
- An answered task collapses into one line, its title and a secondary status
  word. A started task becomes a row with an Open chat button. Status is words,
  never sidebar marks.
- The created-chat card keeps its form. While the chat it opened runs, its
  subtitle carries the running shimmer; nothing else changes.

### Interactive tool UI

MCP tools may render an interactive UI in a sandboxed frame, following MCP Apps.
The app owns the inner UI; Desktop owns only the host frame.

- The host frame is one neutral surface (`--bg-secondary`, `--border-default`,
  8–10px radius) with a quiet header (tool title / app attribution) above the
  frame. When an app sets `prefersBorder: false`, keep the header and controls but
  drop the border and background. No decorative chrome.
- Desktop does not restyle the app's content; it passes theme and accent through
  host context so apps can match.
- Keep the frame compact by default; honor display-mode requests to expand.
- Non-Desktop clients show the tool result's text instead, so flows must not
  require the interactive UI.

### Turn navigation rail

The turn navigation rail is a map of the thread, not a control strip, so at rest
it is barely there.

- Each entry is a `36×10px` hit row holding a `2px`-tall marker on a `26px` track.
  The marker rests at `6px` in `--text-tertiary` at 40% opacity, with no fill,
  frame, or label.
- Active entries lift to `--text-primary` at 60%. The target (hovered, focused, or
  scrubbed) is full-strength `--text-primary` at full length; that marker is the
  keyboard focus indicator.
- A bookmarked marker keeps full opacity and carries a `2px` dot just past its end.
- The rail sits `16px` from the conversation's leading edge, vertically centred;
  when it scrolls, its ends fade over `2.5rem` instead of showing a scrollbar.
- The preview card is an ordinary elevated overlay, `320px` wide, `12px` radius,
  `8px` padding, in `--type-ui`: a medium truncated label, the reply in
  `--text-secondary` clamped to three lines, and outputs as a `--text-tertiary`
  icon-plus-label row. Bookmarking is an inline ghost icon action. Loading is a
  shape-matched skeleton.
- The jump flash pulses the user bubble from `--text-primary` at 14% back to its
  own fill over `1400ms`, ease-out.

## Lists and pages

### Selection rows

Compact selectors, menu items, picker options, sidebar thread rows, plugin/skill
rows, popover command rows, and compact breadcrumb controls share one interaction
language:

- rest state is borderless unless the control belongs to a framed toolbar family;
- hover/open/highlighted/selected states use neutral background elevation and text
  colour, never visible borders, inset rings, or outlines, and never a change of
  font weight, which reflows the label and shifts the layout;
- stronger outlines are reserved for inputs, drag/drop targets, validation,
  destructive confirmation, and focus-visible accessibility.

A row or group header may keep secondary metadata and management actions hidden
at rest and reveal them on hover, so a long list stays quiet. Such a control must
also reveal on keyboard focus, and reveals by opacity rather than mounting, so the
row never shifts. Metadata that only identifies the row (a path, a source URL, an
origin channel) may live in a hover annotation instead of layout: a tooltip for
one line, a details card for several. Metadata only some rows carry belongs there
by default, so the list stays one shape.

A sidebar thread title that does not fit fades out over its last 16px instead of
ending in an ellipsis; a title that fits is never masked. Resting on the row
scrolls the title once, after 350ms at 2em per second, until its end is fully
visible, then holds; leaving the row returns it. Reduced motion keeps the fade
and drops the scroll. Marks about the thread sit at the trailing end of the title
area, 6px before the status slot, so they line up down the list: a thread running
in a worktree carries the 14px worktree glyph in `--text-dimmed`. The
status slot holds one item, in priority order: pending request, running spinner,
unread dot, then the branch's pull request glyph.

### Settings groups

A settings group is a heading over a card, not a card with a heading in it. The
title, optional description, and group actions sit above the card as a frameless
header: the title in `--type-heading`, the description one `--text-secondary` line
4px beneath, actions right-aligned on the title line. A header with an action is
never shorter than the settings control band (`28px`); a title-only header keeps
its text height. The card follows 8px below and holds rows only: `--bg-secondary`,
`1px` `--border-default`, radius 12, hairline dividers between rows and none above
the first. Groups stand 20px apart.

Explanatory text belongs to the row it explains: label, hint (in `--type-hint`
under the label), control on the right. A group description is reserved for list
groups whose rows are items (paired PCs, servers, people) and for a group whose
rows together configure one feature; it defines the feature in one sentence. A row
never repeats its group's title.

A setting that works only while another is on keeps its row when the prerequisite
is off: the control is disabled with its stored value unchanged, and a tooltip
names the prerequisite. The hint keeps defining the setting, and turning the
prerequisite back on restores it as stored.

A select over items that do not all qualify lists every item and disables the
ones that cannot serve, with the reason as the option's description; an option
that follows another setting names what it currently resolves to. When the
effective choice cannot serve, a one-line warning notice under the rows says so
and links to the fix.

Each page or segment has one principal action, in primary neutral inversion;
refresh and other quiet actions beside it stay frameless icon buttons.

A choice among options is a row whose control is a select. Each option may carry a
one-line description in the menu, and the chosen one shows a check. Two or three
short label-only options may use a segmented control instead. Settings never use
bordered radio cards; framed tiles are reserved for visual previews such as theme
thumbnails. Workspace onboarding keeps its own selection cards. An inline
notice uses the shared banner geometry (14px padding, 12px gap, a 20px glyph, a
13px/600 title with an optional secondary line) in the level's colour, with no
dismiss control; the header's refresh is the retry.

### Detail sections

A detail page stacks several frameless groups: what an item contributes, its
metadata, its settings. A section is marked by a `--border-subtle` rule under its
heading, not a border around its rows, and its rows carry no dividers. Boxing each
group turns one column into competing cards, and a bordered table inside a
bordered section doubles the frame.

A detail page presents its subject; standing state controls stay in the manage
surface that owns them (see Dialog headers). A detail page uses one task-oriented
primary action slot rather than a standing management switch: install or enable
show in-control progress, then the same slot offers the next step.

A long list of contributed items previews the first few and hides the rest behind
one disclosure row of the same height that names the next hidden items and counts
the remainder over a stack of their marks, so it says what expanding reveals; the
heading keeps the full count. The disclosure row carries no hover fill, lightening
its text instead.

### Plugin surfaces

A plugin settings page uses the built-in settings grammar: a group heading over a
card of rows, with wide visual choices in a block row rather than a card inside a
card. Plugin artwork may carry its own colours; the controls around it stay on the
shared neutral borders, radii, spacing, focus, and selection states.

#### Conversation side surfaces

Content beside the conversation stays quieter than the conversation itself.

- **Header toggle.** A side panel's header toggle is a standard toolbar icon
  button with the header's size, gap, and pressed treatment. Its glyph never
  echoes the panel toggles or the overflow menu.
- **Summary card.** One self-contained card on `--bg-elevated` with a `1px`
  default border, a 16px radius, and 20px horizontal padding. Pinned, it aligns to
  the stream's trailing edge, 16px in, with no shadow; the popover form adds
  `--shadow-lg`. A section title is secondary-size muted text with no count;
  sections separate with a hairline. Rows are single-line at UI size with a 16px
  leading glyph and no trailing meta (except line totals, `+N` in `--success` and
  `-M` in `--error`, tabular, zero side omitted); a trailing row action stays hidden
  until hover or focus and is a sibling of the row's own button, never inside it.
  The card is its own scroll container, so its border and radius stay whole while
  it scrolls; it scrolls by wheel or trackpad with no visible scrollbar.
- **Summary context.** The Context section is one row with no leading glyph (the
  composer ring already shows usage): "n% used", and used / window tokens as meta
  with a reveal chevron. Open, it shows an 8px segmented bar and one flat legend at
  secondary size, largest first, with swatches on the section's left edge: the four
  largest categories take
  `--chart-series-1` to `-4` and the rest share `--chart-series-other`; tokens added
  since the breakdown was measured are a hatched neutral; the auto-compact reserve
  and free space are neutral tints. Tools loaded only when needed close the list as
  ordinary rows marked "(on demand)" with "—" for share. Only a row with parts (MCP
  servers, instruction and memory files) opens, from a chevron after its name; a
  file path keeps its head and file name around a middle ellipsis. A footer gives the room left before auto-compaction and a
  secondary Compact button.
- **Summary motion.** When a shell panel leaves too little room for the pinned
  card, it fades and slides out as that panel opens, non-interactive during the
  exit, and reverses if room returns. Reduced motion completes the exit at once.
- **Revealed item.** A transcript item revealed from a side surface is centred and
  flashed once with a `--text-primary` 10% fill that fades over 1.4s on
  `--ease-expand`.
- **Avatar stacks.** Overlapping agent avatars carry a 1.5px ring of the card
  surface. A muted row also mutes its avatars.

## Viewers and editors

- File viewers and docked file lists inherit the main surface rather than a
  secondary panel fill; secondary and tertiary surfaces are for controls, hover,
  and selected rows within them.
- Editable and read-only file views share one neutral chrome and one code
  presentation: grammars, paired theme colours, code font, size, line height,
  gutter, indentation, and wrapping. Editing adds no second syntax palette; focus
  adds only caret, selection, and current-line feedback. Highlighting updates never
  replace newer content or reset selection and scroll.
- Markdown opens as an editable semantic document in document typography, with
  shared code typography inside code. Syntax markers stay hidden except where the
  focused selection touches them; lists keep their markers; tables keep their cell
  layout; closed code fences keep their rendered boundary and language label. The
  header switches between source and preview with one icon button that shows the
  mode it switches to (code for source, eye for preview) rather than a pressed
  state, and each mode keeps its own selection and scroll.
- File-link navigation marks the target with a restrained full-line background and
  gutter marker from `--bg-navigation` and `--border-navigation`, distinct from
  selection, the active line, and search matches, preserving syntax colours. It
  persists through reading and focus and is dismissed by editing or deliberate
  selection movement; it is not a timed flash.
- The floating editor toolbar sits 16px from the bottom and right with compact
  undo/redo controls, a neutral border, 8px corners, and a 90% elevated surface
  with backdrop blur, a local exception to the opaque-overlay rule. Save state
  appears only when useful; hide the toolbar when it has none.
- Focused file editors own their Find shortcut: a top floating find bar with query,
  result count, previous/next, and close, without replacement.
- Large files stay read-only with a neutral outlined notice in the body, not a
  yellow warning strip.
- External-change review is a single-column diff with a fixed footer ordered wrap
  toggle (neutral pressed tone), Edit and Reject (`outline`), and Accept
  (`primary`). An over-limit diff shows an unavailable-preview message without
  removing the decisions.

### Line comments

A comment on code lines is one shared card, placed under the line it ends on in a
diff or file preview. Drafts, saved comments, and model review comments use the
same card.

- The card starts at the code column with a `6px` inset, spans the visible column
  up to `48rem`, and stays pinned while the code scrolls sideways. It is
  `--bg-secondary` with a `10px` radius, a `1px` `--border-default` ring, and the
  level-1 shadow.
- The header names the author left and the line right, both `--type-secondary` in
  `--text-secondary`; lines read as `L` (old side) or `R` (new side) plus the
  number. A `24px` circle leads the author: the user's avatar (falling back to
  initials, then a person glyph), or the DotCraft mark for a model review comment,
  which adds a bold title and no footer.
- The body is a bare field at `--type-ui`. Footer actions sit on the `sm` band: a
  draft shows Cancel (`ghost`), dictation, and Comment (`primary`, disabled while
  empty); a saved comment shows only a `ghost` Delete at rest, and while editing
  moves Delete left as `danger` beside Cancel, dictation, and Save.
- The diff's start control is an `18px` inverted square (`--text-primary` fill,
  `--bg-primary` plus glyph, `4px` radius) over the line-number edge, answering row
  hover and keyboard focus. Selected lines take a `16%` accent tint.

## Feature surfaces

### Pet

The Pet settings tab dresses the default companion, keeping the neutral posture
while letting the collection's own colour show.

- The dial is a 232px open ring around the 120px companion: thirteen colour
  segments over 300° with the gap at the bottom, Original first at the lower left
  and the twelve role palettes clockwise, each in its mid body tone at 72%
  opacity. Pointer, focus, and the selected segment go to full opacity; the
  selected segment pops 5px outward with a 3px white dot. The current colour's name
  sits in the gap in `--type-secondary`. With customization off the group is
  hidden.
- Rarity is chrome, never paint on the companion. On a bag tile it is a radial glow
  behind the art in the rarity colour, growing with the tier (uncommon 22%, rare
  30%, epic 38%, legendary 46%; common none), plus from rare up a 1px inset frame at
  55%. No rarity badges, dots, or pills; a named rarity is one word in its colour at
  weight 500.
- Bag tiles are `--bg-tertiary`, radius 8, at least 92px wide, art centred with
  the name in `--type-hint` beneath and a `×n` count top right. A worn tile carries
  a 1.5px inset `--text-primary` ring and a check. The bag is a fixed five-column,
  two-row grid whose empty slots stay visible as dashed `--border-default`
  outlines, so an empty or filtered bag never collapses or moves the dial; longer
  collections scroll with a reserved gutter.
- The exchange lists the rarity's spares as a wrapping row of 44px tiles over
  their rarity glow, each with its remaining `×n` count; a tile with none left
  dims and stops taking selections. Below it, the tray is ten 44px slots with the
  same dashed empty outlines; a filled slot shows the item over its rarity glow.
  The trade's result is the find toast, so the exchange never changes shape.
- An item's details card opens on hover or focus after the details-card delay:
  an ordinary elevated surface 240px wide above the item (mirroring below), with
  the full name at weight 600, the rarity word, then the slot and feature in
  `--type-secondary`. Items carry no native title.
- The find toast is the neutral toast card with 36px art over a stronger glow
  (uncommon 40% to legendary 66%) and no frame, the rarity word as its
  description, so a legendary never reads as a warning.
- The tab icon is the companion's head at the icon set's weight, matching the logo
  mark.

### Automations

Automations uses one list and one directly editable detail surface: editable
title, prompt, labelled detail rows, frequency rows, and collapsed advanced
settings. The agent profile choice shows the profile avatar and description. The
save row relies on spacing, with no rule above it. List and editor share the
resizable divider.

Previous runs are compact selection rows, not result cards: the whole row opens
the run, status is a small leading marker, identity in the middle, relative time at
the trailing edge. An "open run" action is not repeated per row. Archived rows keep
their place and fade their title.

Task rows reserve one trailing action slot: an unread-run marker at rest, exchanged
for More actions on hover, focus, or an open menu without moving the title or
timing; coarse-pointer surfaces keep the trigger visible. A task status control
reveals Pause over an idle circle on hover or focus and shows Play when paused;
running shows activity before every other marker.

### Subagents

The Subagents tab lists a thread's subagents under counted Running, Finished, and
Closed headings, each count set apart by a 6px gap; Finished and Closed start
folded. A row puts a 32px avatar beside two lines: the name in `--text-primary` at
the UI emphasis weight with its role in `--text-dimmed`, then the model and the
token count in `--text-secondary` with the word tokens in `--text-dimmed`,
separated by an 8px gap rather than a divider character. The name line's trailing
edge holds the running or relative time in tabular figures, or Failed or Cancelled
in `--text-secondary`; a running row ends with a ghost
Stop icon button that shows a spinner while stopping. Subagent names carry no hue;
the avatar is the identity.

An opened subagent shows inside the tab under a header row of Back, a 24px avatar,
name and role, and model with reasoning effort trailing. Its transcript reuses the
conversation's blocks, read-only and without a composer. The first load is a
transcript-shaped skeleton, and a read failure is stated under the header so Back
stays in reach.

### Discussion board

Board calls read as what happened on the board, never as the tool name or JSON. A
post leads with its sentence, quotes its first line under a 2px `--border-default`
rule in `--text-secondary`, and names the agents it notified with their avatars; a
longer post expands. The author is not named, since it is always the transcript's
own agent. Consecutive reads fold into one group row like explored files, and each
expands into the posts (author avatar, name, time, text), replies indented under a
rule, search matches on `--find-match-bg`. Channel names are plain inert text
introduced by the word "channel", not a `#` sigil. Agent names open their subagent.
A board error is an error row carrying the board's message.

### Documentation site

The documentation site is a Desktop window, not a marketing skin. It reuses this
file's tokens in both themes, the neutral-first posture, and the primary action
rule, with no palette of its own; a token change here is mirrored there. Two
exceptions serve reading in a browser: the dark theme re-seeds the surface deeper,
and the home page's hero and closing call to action sit on a fully defocused
colour-field image (light and dark variants) that drifts slowly without changing
colour. Reduced motion or missing WebGL leaves it still. The hero field fills edge
to edge from the top of the window; the closing field fades in on an eased ramp and
runs to the bottom of the page under the footer, with no rule or surface change.
Copy over it uses frosted or near-primary text.

The [Welcome backdrop](#welcome-backdrop) character field lies over both colour
fields with its own rules, except that characters take the other theme's field tone
(deep blue in light, pale blue in dark), reach about 2.2:1 (dark) and 1.8:1 (light)
against the field's mean tone, and a click glows in the logo gold. It clears under
the top band and above the footer.

- A chrome-tone band runs across every page. On the home page it carries only the
  lockup, language, appearance, and GitHub, transparent over the hero until hover,
  focus, or scroll. Content pages add the Desktop sidebar grammar and set the page
  on the main surface, with a sticky top band following the catalog top-bar rules.
- The home page runs hero, tour, Harness, product cards, then the closing call to
  action; each section says something the others do not.
- Every product visual on the home page sits on the same colour field, each showing
  a different slice. Product cards crop real product UI at the card edge and show
  the core clients only. The Harness code window is a static figure.
- Page text uses the conversation type rules retuned for reading; tables, inline
  code, links, and code blocks follow their Desktop counterparts. The page foot is a
  small last-updated line and plain Previous/Next text links, with no boxed pager
  cards.
- Sora is the brand display face, used only for home-page display type; everything
  else stays on the system stack, and Chinese falls back to the system CJK face.
- The icon and wordmark align by visual weight (the antenna carries weight), not by
  box centres.
- A perched mascot stands on its surface over a soft contact shadow, inset from the
  corner radius, never overlapping a title bar; it changes pose in place.
- The live Desktop demo renders at its real resolution, never transform-scaled.

## Do's and don'ts

Do:

- Use tokens and shared components before adding new local styles.
- Decide the action hierarchy before choosing a button treatment.
- Route text and icon actions through the shared button, icon button, and shared
  icon-action components.
- Keep action and icon buttons frameless by default; reserve a visible border for
  the `outline` / `outlineGhost` variants and framed icon buttons in special or
  important cases.
- Keep every view's main visual language neutral unless this file assigns a
  stronger role.
- Add or remove a reusable token in the token stylesheet and this file together.
  A token with no readers is removed from both theme blocks and the front-matter
  `colors:` map in one change.

Don't:

- Add raw colors in component styles unless they are media, charts, generated
  previews, provider logos, or imported assets.
- Use brand blue, accent borders, decorative gradients, or glow rings for
  ordinary actions.
- Add page-specific palettes to feature views.
- Put cards inside decorative cards.
- Add visible borders to ordinary menu rows, picker options, or sidebar rows.
- Use semantic colors as decoration.
- Use oversized type in compact panels, cards, sidebars, dashboards, menus, or
  dialogs.
- Wrap an inline reference in a pill, border, or fill, at rest or on hover; hover
  lifts the text instead.
- Chain interface metadata with glyph separators such as `·`, `•`, or `|`; give
  each fact its own element separated by spacing. Reference labels inside
  conversation content keep their own format.
