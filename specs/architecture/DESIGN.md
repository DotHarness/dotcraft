---
version: "0.8.3"
name: "DotCraft Desktop"
description: "Quiet operational desktop UI for repeated agent work."
sourceTokens: "desktop/src/renderer/styles/foundations/tokens.css"
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
  ref-skill: "var(--ref-skill)"
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
  full: "999px"
components:
  primary-action:
    background: "{colors.text-primary}"
    color: "{colors.bg-primary}"
    border: "1px solid {colors.text-primary}"
  menu-overlay:
    border: "none"
    rowHover: "var(--sidebar-control-hover)"
  selection-row:
    border: "none"
    hoverBackground: "var(--bg-tertiary)"
---

# DotCraft Desktop Design

This document is the source of truth for DotCraft Desktop visual design. The
canonical token implementation is
`desktop/src/renderer/styles/foundations/tokens.css`, and the renderer loads the
ordered production style graph through `desktop/src/renderer/styles/index.css`.
The ownership and compatibility contract for that graph is defined in
`specs/architecture/desktop-styles.md`. This file defines the product-level
intent, component rules, and review checklist that new and changed Desktop UI
must follow.

## Overview

DotCraft Desktop is a quiet operational tool for repeated agent work. It should
feel calm, compact, legible, and consistent across conversation, settings,
skills, automations, plugins, channels, detail viewers, and modal flows.

The design posture is neutral-first:

- Use neutral surfaces, text, borders, spacing, and elevation as the default UI
  language.
- Use emphasis only when it clarifies current state, next action, risk, or
  selected context.
- Prefer existing Desktop tokens, shared components, and local style constants
  over one-off colors or bespoke control treatments.
- Avoid decorative gradients, page-specific palettes, and large color washes
  unless a feature owns a documented media or visualization surface.

The brand accent is intentionally conservative. It is not the default
call-to-action color.

## Colors

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
- `--border-default`: ordinary control, input, and card boundaries.
- `--border-active`: hover, focused, or active neutral boundaries.

`--accent` and `--accent-hover` are reserved for restrained brand or navigation
emphasis:

- field focus borders, focus-visible outlines, and accessibility affordances;
- selected navigation, segmented controls, or active state accents when neutral
  inversion is not appropriate;
- links or small inline affordances where product recognition helps;
- setup or onboarding moments where DotCraft is intentionally presented as the
  product.

Do not use `--accent` as the default primary button background for ordinary
actions such as create, start, close, save, submit, continue, manage, configure,
or refresh.

Semantic colors communicate state, not decoration:

- `--success`: completed, healthy, connected, applied.
- `--warning`: caution, pending review, risky but recoverable.
- `--error`: destructive action, failed state, blocked state.
- `--info`: informational status when neutral text is insufficient.
- `--permission-full-access`: the full-access / auto-approve permission state — a warmer orange than `--warning`, used only on the composer approval pill and small option icons.

Semantic colors should normally appear in icons, compact badges, borders, small
text, or alert surfaces. They should not take over an entire view.

Feature, channel, and provider colors are allowed only as small identity accents
inside icons, avatars, badges, media previews, or charts. They must not become a
view theme.

### Code token colors

Syntax highlighting is the one place where color does not come from product
tokens. A TextMate theme assigns color across hundreds of grammar scopes; the
product token vocabulary above cannot express that, and inventing a parallel
palette for it would drift from every editor the reader already knows.

Desktop therefore ships one vendored theme pair and lets the highlighter emit
the colors. Each highlighted run carries both resolutions as custom properties
(`--dc-token-light` / `--dc-token-dark`) and the stylesheet selects between them
with `light-dark()`. `color-scheme` is set on the code surface itself, never on
the document, so user-agent rendering elsewhere is unaffected. One tokenization
serves both themes, so switching appearance repaints without re-highlighting.

The boundary is the code text. Everything framing it stays on product tokens:
gutters and line numbers, block and pane backgrounds, diff add/remove fills and
accent bars, selection, search highlights, and every control around the view.
Features do not read or redefine `--dc-token-*`; they belong to the highlighter.

## Typography

Desktop typography is compact and readable:

- ordinary UI text uses 13px tokenized type where possible;
- supporting text uses 12px tokenized secondary text;
- card and panel headings use modest weight increases rather than display-scale
  type;
- hero-scale type is reserved for true entry surfaces, not compact panels,
  toolbars, menus, cards, or dialogs;
- letter spacing is `0` unless a specific technical label style documents a
  different value.

### The type scale

Sizes come from the `--type-*` tokens in `tokens.css`. Do not write a raw `px`
font size in a component: a literal cannot be retuned by context, and every
literal is a new tier nobody agreed to.

| Token | Size / leading | Use |
| --- | --- | --- |
| `--type-title` | 28 / 34 | entry surfaces only |
| `--type-page-title` | 18 / 23 | panel page heading |
| `--type-heading` | 15 / 20 | card and group heading |
| `--type-body` | 14 / 21 | conversation and document body |
| `--type-ui` | 13 / 18 | ordinary UI text, row labels, inputs |
| `--type-secondary` | 12 / 16 | supporting text; use `--type-secondary-prose-line-height` (18) when it wraps |
| `--type-hint` | 11 / 16 | supporting text nested under an already-labelled control |

Size expresses nesting depth, colour expresses role. Copy that describes a
control is `--text-secondary` at whichever size its depth calls for; only
incidental metadata (version strings, timestamps) drops to `--text-dimmed`.
Pairing the smallest size with the dimmest colour is what makes small text
unreadable, so do not do both at once.

The text colour ramp is `--text-primary` → `--text-secondary` → `--text-dimmed`
→ `--text-disabled`. `--text-tertiary` is an alias of `--text-dimmed`, not a
fifth step.

### Retuning the scale by context

A surface may override the `--type-*` tokens for the subtree it owns instead of
changing components. `.dc-settings-surface` uses this to lift the two smallest
tiers one step under `:lang(zh|ja|ko)`, because CJK glyphs carry far more strokes
per em than Latin. Lift tiers in pairs so the gap between them survives, and keep
the override scoped — a global lift reflows fixed-height rows in the composer and
sidebar.

UI fonts are system-first and must not require bundled web fonts. `--font-ui`,
`--font-body`, and `--font-sans` may switch by document language for CJK locales
while preserving the same weight and spacing scale.

## Layout

Desktop surfaces should favor dense but organized operational layouts.

- Keep common workflows ergonomic for repeated use.
- Catalog browse, manage, and detail surfaces use one 48px top control band: navigation
  (tabs or breadcrumb) stays left, page-level management actions stay right, and
  both sides share the same vertical center. Do not position catalog actions in
  the hero/header below this band or compensate with negative offsets.
- Catalog browse and manage pages separate their controls and groups with space
  and heading weight, not with rules: no rule under the hero/search header or
  manage toolbar, and none above a group. A rule above the first group is a frame
  edge rather than a separator, and one above the rest is redundant with the gap
  already between them.
- Use stable dimensions for fixed-format controls such as boards, rows,
  toolbars, icon buttons, counters, tabs, and menus.
- Constrain content with explicit grid, flex, min/max, or aspect-ratio rules so
  hover states, labels, icons, loading text, and dynamic content do not resize
  or shift the layout.
- Avoid nested cards and decorative section cards. Page sections should be
  unframed layouts or full-width bands with constrained inner content.
- Cards are for repeated items, modals, or genuinely framed tools.

View-level color assignment stays neutral:

| Surface | Main Visual Color | Emphasis |
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

## Elevation & Depth

Use depth instead of color variety.

- Main content: `--bg-primary`.
- Panels, cards, menus, secondary controls, and repeated items:
  `--bg-secondary`.
- Nested, hovered, or active surfaces: `--bg-tertiary` / `--bg-active`.
- Elevated overlays use a solid, opaque surface (`--glass-surface-strong`, which
  resolves to `--bg-elevated`) plus existing shadow tokens. The overlay surface is
  intentionally opaque, not translucent: a translucent overlay tints differently
  depending on what sits behind it, so the same menu looks inconsistent over
  content versus empty space. An opaque surface renders identically everywhere.
- Ordinary menu overlays are borderless and separated by surface plus shadow.
- When an overlay overlaps another elevated overlay (a submenu over its parent
  menu, or a popover stacked on another overlay), add a single
  `1px solid var(--glass-border)` hairline on the overlapping edge only — not a
  frame around the whole surface. The overlapping edge is the one boundary shadow
  cannot draw, because both layers share the same tone. This is the only border an
  ordinary overlay carries.
- Larger dialogs, inspectors, viewers, and non-menu popovers may use subtle
  neutral boundaries when contrast requires it.
- A fullscreen renderer overlay that crosses an Electron native view, such as an
  image lightbox above the embedded browser, must register as a native-view
  blocker for its complete mounted lifetime. Hide the native view while blocked
  and restore only the active view after the last blocker closes; ordinary menus
  and local popovers do not use this behavior.
- The three workspace resize dividers — sidebar/main, conversation/detail, and
  viewer/explorer — share the same hover and drag highlight: a `1.5px` neutral
  vertical gradient (`--main-surface-edge-glow`) that is brightest at center and
  fades to transparent toward the top and bottom. The rest-state `1px` hairline is
  unchanged. This is a functional resize affordance in a neutral tone (no accent,
  no bloom), not a decorative glow; the rule below still forbids glow/accent
  treatments on ordinary controls.
- File viewers and docked file lists inherit the surrounding main surface instead
  of introducing a secondary panel fill. Use secondary and tertiary surfaces for
  controls, hover states, and selected rows within them.

Do not use glow rings, highlighted borders, accent borders, or decorative
gradients to make ordinary controls "stand out." Use placement, hierarchy,
weight, spacing, and neutral inversion first.

## Shapes

Ordinary controls and cards use 8px radius or less unless an established
component family uses another token.

- Compact text buttons and catalog-toolbar controls: 10px.
- Compact icon buttons: 6px or 8px depending on the local family.
- Cards and repeated items: 8px.
- Dialogs and elevated popovers: 8px to 10px.
- Pills, badges, and toggles: `999px` when the shape is semantically pill-like.
  A standalone, high-emphasis primary action may also take the `999px` pill as a
  documented exception (see Actions); ordinary in-row or repeated buttons do not.

Keep shape language restrained. Large rounded rectangles should not be used as
decoration.

## Components

### Actions

Each immediate decision area may have at most one primary action.

Primary actions use neutral inversion:

```ts
{
  border: '1px solid var(--text-primary)',
  backgroundColor: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  fontWeight: 600
}
```

Action buttons are frameless by default. Secondary actions use a neutral frameless
fill:

- background: a subtle `--text-primary` tint (~6%, hover ~11%);
- border: transparent (reserved in the box model, not painted);
- text: `var(--text-primary)`.

A visible border is reserved for the `outline` variant and used only for special or
important framed actions — it is not the default for ordinary controls.

Ordinary management actions, including `Manage`, `Configure`, `Refresh`, and
repeated row controls, are secondary actions unless they are the one immediate
submit/continue action. They must not use decorative gradients, accent-tinted
borders, glow rings, or provider colors.

Tertiary actions are transparent text/icon controls with neutral hover feedback.
Use them for inline affordances, low-frequency commands, and compact toolbars.

Quiet actions are the one control that carries no hover feedback at all
(`.dc-quiet-action`). Reserve them for text that is primarily a label and only
secondarily a target — an agent name, a provenance line — where a hover block
would read as chrome wrapped around a name. A quiet action never rewrites the
type of what it wraps: it inherits the surrounding size, weight, and line height
rather than moving the text onto the button band. Two rules are not optional.
Focus must stay visible, since hover no longer signals that the text is
interactive. And the affordance has to live somewhere else — a chevron beside it,
or a tooltip naming the action. A row should keep at most one quiet action; if
everything in it goes silent, nothing in it reads as reachable.

Destructive actions must use explicit copy such as Delete, Remove, Discard, or
Stop. The danger affordance is a frameless `--error` fill (~10% tint, hover ~18%)
with `--error` text — not a bordered outline. Keep surrounding chrome neutral and
require confirmation where appropriate.

Text action buttons use a shared size so controls line up across panels,
toolbars, rows, and dialogs:

- default height `32px`, `8px` radius, `13px` type, `box-sizing: border-box`;
- compact `sm` height `28px`, `10px` radius, `12px` type;
- horizontal padding around `12–14px`; icon+label controls keep a `6px` gap;
- buttons that share a row (for example a primary next to a secondary or a
  refresh) must share the same height so the row reads as one control band.

A taller, more prominent action is allowed as a deliberate exception, not the
default. A standalone, high-emphasis call to action — the single primary button
in a focused setup or install dialog, or a lone full-width confirm — may use a
larger pill: a `~38px` height with `999px` radius to read as the one clear next
step. A compact plugin-package Install action is the other pill exception: it
uses the `28px` compact control height and a `999px` radius consistently across
plugin browse, manage, and detail surfaces. Native-app installation and other
row actions keep the ordinary radius. Outside these two cases, repeated or
in-row actions stay at the `32px` / `8px` standard.
When a prominent pill shares a row with other buttons, raise the others to the
same height so the row still aligns.

Catalog top bars run one control band of their own: `28px` height with a `10px`
radius, shorter and rounder than the standard band. Every control in that bar —
text actions, icon actions, compound triggers, and search fields — takes it, so the
row reads as one strip and the band does not change as the user moves between
catalog surfaces.
This is the only sanctioned alternative band; elsewhere the `32px` / `8px` standard
holds. Catalog top bars prefer icon-only actions with tooltips for repeated
management commands such as Refresh and Manage, keeping the labelled action for the
one principal command.

These action rules are implemented by the shared `Button` component and its
`.dc-button` styles. Route new text and icon actions through it instead of
re-deriving inline button styles. Choose the action hierarchy with the `variant`
prop and the footprint with the `size` prop:

- `variant`: `primary` (neutral inversion, the one immediate action), `secondary`
  (frameless neutral fill, the common action), `ghost` (transparent tertiary),
  `danger` (frameless semantic fill, paired with explicit Delete/Remove/Stop copy),
  `accent` (restrained brand, never the default create/save/manage), `outline` (the
  one bordered variant — only for special / important framed actions).
- `size`: `default` (the `32px` control band), `sm`, `icon`, `iconSm`,
  `prominent` (the standalone `38px` pill CTA), `toolbar` (the catalog top-bar band).

Buttons are frameless by default. Every variant keeps a `1px` border in the box
model but only `outline` paints it visibly, so switching a button between fills and
frames never shifts height or alignment — the "border-reserved" treatment. Heights
come from `--button-height` / `--button-height-sm` so buttons, selects, and icon
buttons share one control band.

Ordinary controls remain geometrically stable through hover, focus, open, and
pressed states. Use color, surface, border, or shadow changes for interaction
feedback; do not translate, scale, rotate, bounce, or spring the control on
press. Transform-based control motion is allowed only when a feature explicitly
requires and documents it (for example, a directional affordance or a functional
drag interaction), and it must honor the shared reduced-motion preference.

### Icon Buttons

Icon buttons (the shared `IconButton`, styled by `.dc-icon-button`) are frameless by
default, matching the frameless action language:

- 32px width and height; 8px radius for toolbar/catalog controls;
- transparent surface with a reserved `1px` transparent border;
- `var(--text-secondary)` icon color, with a neutral hover fill
  (`var(--bg-tertiary)` + `var(--text-primary)`);
- `active` marks a selected/toggled state with a subtle accent tint.
- `aria-expanded="true"` marks an open menu or popover with a neutral fill; opening
  ordinary chrome is not a selected accent state.
- destructive icon-only actions use the shared danger tone rather than a locally
  painted red border.

Viewer chrome may use compact `16px`, `24px`, or `28px` icon-button footprints
when required by an existing tab slot or toolbar, and catalog top bars use the
`28px` / `10px` toolbar band. The shared hover, focus, disabled, open, and danger
treatments still apply at those sizes.

Thread List icon actions use a denser, foreground-only exception to the default
surface hover. The parent thread or project row owns the hover and current-state
surface; compact actions inside that row, plus the options and create actions in
its section headers, stay transparent at rest, hover, focus, open, and pressed
states. Their icon moves from a quiet foreground to the primary foreground, while
`focus-visible` keeps the shared outline and the hit target remains stable. This
prevents a second rounded surface from appearing inside an already highlighted
row. Pin state may also use its filled icon to communicate selection without an
active background. Archive remains neutral in this context because archived
threads are recoverable; reserve danger color for irreversible or genuinely
hazardous actions.

Compound triggers combine a principal action with a menu of related commands. Both
segments share one intent and one size; the group clips the outer corners while each
segment drops the radius and border on the edge they meet, so the pair reads as a
single control.

Compound triggers use one joined geometry. Both segments meet flush and avoid a
doubled seam; when an outline variant is used, the pair reads as one neutral outer
frame. Hover changes only the hovered segment. The menu glyph sits at reduced opacity
so the chevron reads as an affordance rather than a second action.

Emphasis is carried by intent, not by a second treatment:

- the `primary` neutral inversion is for the principal action of a surface. The
  catalog create control is the reference case.
- the `secondary` same-color fill is for quiet compound triggers that sit among
  other chrome rather than leading it.
- the `outline` neutral frame is for matched open-target controls in the thread
  header and file viewer. These controls are the reference cases and should keep
  the same frame treatment even when one omits its text label for compactness.

Use the shared `SplitButton` rather than composing a button pair, chevron, and
positioned menu per feature, so segment geometry, keyboard navigation, outside-click
dismissal, and focus restoration stay identical everywhere.

A compound trigger takes the height of whichever control band it sits in, so the row
still reads as one band. Compact thread-header Apps triggers remain frameless and
omit connection counts.

A visible neutral frame (`bordered`: `var(--bg-secondary)` +
`1px solid var(--border-default)`) is opt-in and reserved for special or important
icon controls. Modal close buttons stay borderless with neutral hover feedback.

### Status Menu Buttons

A compact status menu button combines a current-state label with an overflow
menu when a repeated row would otherwise expose several competing actions. It
is a state affordance, not a second primary action:

- the trigger is a 32px-high neutral control with a small semantic status dot,
  concise label, trailing chevron, and a persistent `1px
  solid var(--border-default)` outline;
- hover and open states may strengthen the neutral fill and border together,
  but the frame never becomes an accent border;
- stable states such as connected or active may use a success dot, while
  unavailable or failed states use warning or error only in the dot and label;
- clicking the trigger opens the ordinary shared menu treatment; destructive
  commands remain explicit danger menu items and require confirmation when
  they revoke durable authority or delete data;
- a required next step such as Install, Connect, Add, or Review remains a
  direct shared `Button` instead of being hidden in the status menu;
- loading states disable the control and use one in-control progress signal;
- the trigger exposes `aria-haspopup`, `aria-expanded`, keyboard open/close,
  and restores focus after the menu closes.

The visible frame is a deliberate exception to the frameless ordinary-button
rule because the trigger combines status and menu responsibilities. Use the
shared `StatusMenuButton` rather than composing a badge, chevron, and positioned
menu per feature. Workspace-level app connection rows are the reference
treatment: `Connected` combines principal status with Reconnect and Disconnect.
Conversation app selection uses a `PillSwitch` instead because it is a
reversible on/off choice rather than a status menu.

### Dialog Headers

Dialogs that carry an identity icon share one header treatment so they read as
one family regardless of their differing bodies (forms, confirmations, pickers).
Use the shared header rather than re-implementing per dialog.

- The identity icon sits in a neutral rounded badge: a ~36px square with `8–9px`
  radius, a `--bg-tertiary` background, and the glyph at `18px` in
  `--text-secondary`. The badge gives every dialog the same quiet, recognizable
  anchor; a bare icon without the badge is not used. When the dialog's subject
  carries its own product artwork — a skill or plugin avatar — that artwork
  occupies the badge's footprint instead of being nested inside a neutral badge,
  which would read as two boxes.
- The title sits below the badge using the panel/dialog heading scale (`15px`,
  weight `600`, `--text-primary`). Do not use hero-scale type for dialog titles —
  even prominent dialogs stay at the dialog-heading scale.
- An optional one or two line description follows the title in
  `--text-secondary`.
- When the dialog has a close affordance, it is a borderless, transparent icon
  button in the top-right, aligned with the badge row (see Icon Buttons). A
  dialog-level overflow menu joins it there, to the left of close, rather than
  sitting beside the title.
- A dialog previews its subject; it does not double as a place to change the
  subject's state. Enabling, disabling, and similar switches stay in the manage
  surface that owns them, so one control governs the state rather than two that
  can disagree.

The badge stays neutral by default. A semantic tint (success/warning/error) is
allowed only when the dialog's whole purpose is that state, following the
semantic-color rules; ordinary dialogs keep the neutral badge.

Transient choice dialogs may omit a visible Cancel button when backdrop click
and Escape both dismiss safely and no operation is running. This applies to
short-lived destination and branch/changelist choices. Destructive
confirmations, long forms, edit modes, and running/error recovery flows retain
an explicit Cancel or Close action.

Workspace onboarding keeps its dedicated circular step navigation and selection
cards. Those controls express progress or choice, not ordinary button hierarchy;
only regular actions such as Start, Change folder, Login, and Retry use the shared
Button variants.

### Menus, Popovers, and Pickers

Floating menus, context menus, select dropdowns, compact popovers, and command
palettes share one overlay language:

- a single solid, opaque elevated surface shared by every floating menu so they
  look identical regardless of backdrop;
- no gradient or translucency on the menu surface;
- ordinary menu frames are borderless;
- shadow/elevation separates the overlay from the background;
- a submenu, flyout, or stacked overlay carries one `1px var(--glass-border)`
  hairline on the overlapping edge only — the overlap is the only case that earns
  a border;
- rows are borderless at rest;
- hover, open, highlighted, and selected rows use neutral background elevation;
- focus-visible rings remain available for keyboard accessibility.

Ordinary text-only field selects may expand toward the left when opened so the
longest option can be read without a tooltip. The trigger finishes its width
transition before the menu is revealed, preventing option text from reflowing
while the overlay is visible. The expanded width is capped to the viewport and
extreme labels wrap inside the menu. Rich options with icons or descriptions,
and frameless toolbar selects, keep their fixed-width treatment. Reduced-motion
preferences skip the staged animation.

The thread sidebar and thread-header overflow menus are the reference treatment
for ordinary Desktop menus: neutral overlay surface, quiet elevation, no outer
frame, and borderless rows.

### Inputs

Text inputs, textareas, selects, search boxes, and picker triggers stay neutral:

- use `--bg-primary`, `--bg-secondary`, or dedicated input tokens such as
  `--composer-input-background`;
- composer-adjacent activity docks use `--background-activity-dock-background`,
  which stays visually close to `--composer-input-background` while preserving
  soft glass translucency; when attached to the composer, they keep their top and
  side frame but omit the bottom border on the shared edge;
- when a composer-adjacent dock overlaps the composer, the composer draws a
  `--composer-top-accessory-separator` hairline on the shared edge so the two
  surfaces remain distinct in both light and dark themes;
- the composer card keeps model, context-window, and send controls in its primary
  action row; project, work location, source-control branch or changelist, and
  provider subscription status form the context row below the card, with
  subscription status immediately following the branch or changelist control;
- use `--border-default` for rest state; the primary message composer uses
  `--composer-input-rest-border` so the light theme has a subtle frame while the
  dark theme can remain effectively frameless, shows a soft brand-gradient glow
  that gently breathes on focus (`--composer-focus-glow`), and lifts slightly on
  hover;
- use `--accent` only for subtle focus affordances;
- use `--text-primary` for values and secondary/dimmed tokens for placeholders;
- do not use brand or semantic fills for ordinary input backgrounds.

A field's focus indicator is its own border, and nothing else. Focus moves the
border to `--accent`; it never adds an outline, a ring, or an outer glow around
the control. An outline drawn outside the border box reads as a second frame
stacked on the first, so ordinary `outline` focus styling is suppressed on every
field and the global `:focus-visible` outline remains only as the fallback for
elements that have no focus treatment of their own. This holds for every field
shape below, and for composed fields such as combo boxes and inputs with a
trailing action.

Fields carry no hover state. The pointer usually comes to rest inside the field
it just focused, so a hover border competes with the focus border for the same
1px and hides it exactly when it matters. Focus is also the only state that must
never be outranked: whatever a field's resting shape, the focus border wins while
it has focus.

Three shapes exist, the first two `--bg-primary` with an `8–10px` radius:

- bordered at rest with `--border-default`, moving to `--accent` on focus. This is
  the default. Dense multi-field dialog forms use it so the columns stay legible.
- frameless at rest, showing the accent border only on focus. A simple action
  dialog's single message/objective input (commit message, thread goal) is the
  reference case.
- bare: no frame, fill, or sizing of its own, for the inner field of a composed
  control — a search row, a combo box, an input with a trailing action. The shell
  paints the frame and owns the focus state; the field contributes only the shared
  reset. A bare field never appears on its own.

Desktop-owned text fields use the shared `Input` and `Textarea` components rather
than a locally styled native element, so height, radius, placeholder, hover,
focus, invalid, and disabled treatments stay identical. The components own their
own height and never set `flex`; callers place them in a row or column layout and
pass only their genuine deltas. Three cases stay native: a visually hidden control
that supplies semantics, one that opens a platform picker such as the system color
chooser, and an inline editor embedded in a canvas surface whose own class already
follows the focus rule above.

Validation combines copy, border/icon treatment, and semantic tokens. Error or
warning color identifies the issue without taking over the form.

Visible select controls, checkboxes, and editable suggestion fields in Desktop-owned
UI use the shared `Select`, `Checkbox`, and `Combobox` components so their menus,
focus treatment, disabled state, and keyboard behavior remain consistent. A native
form control may remain only when it is visually hidden and supplies semantics, or
when it deliberately opens a platform picker such as the system color chooser.
Third-party content rendered in sandboxed views is outside this rule.

### Inline Reference Chips

Composer file, command, and skill references are quiet inline content rather than
standalone controls. At rest they show only their type icon and label; hover may
reveal the type-tinted border and fill plus a neutral remove affordance. Rest and
hover states reserve identical border, padding, icon-slot, and label geometry, so
hover never changes the chip width, text baseline, caret position, or the position
of surrounding text. Default and remove icons occupy the same fixed slot and swap
through opacity rather than entering or leaving layout. Use vector icons from the
shared icon language instead of font-dependent Unicode glyphs.

### Message Markers

A user bubble holds only what the person wrote. Everything the client knows
*about* the message lives outside it.

Origin goes above the bubble (`.dc-message-origin`): a right-aligned line of
small icon plus label, on `--text-tertiary`, with no pill, border, or fill. It
names where the turn came from — steered conversation, another thread, an
automation — and nothing more. When the origin has somewhere to go the line is a
quiet action and lifts to `--text-secondary` on hover; an origin with no
destination stays inert, so a target that goes nowhere never looks reachable.

Special state goes into the message action row below the bubble
(`.dc-message-state`): the same small icon plus label, sitting after the actions.
State is information rather than an action, so it stays visible at rest while the
timestamp and copy controls beside it remain hover-revealed.

The same holds for the label above a block inside the transcript — the `Plan` on
a plan card, the `Created` on a scheduled task, the `Loaded` on a skill. A label
names what the block is; it is not a status chip, so it carries no pill, border,
or fill. Rank it by colour and placement instead: a label sitting above a title
stays below that title in weight, so the two do not compete for the same glance.
When the block is still running, the label shimmers on its own text
(`tool-running-gradient-text`) rather than gaining a badge — the running signal
belongs to the words that are already there.

A label that shares its row with the block's own controls forms one header row:
label left, actions right, both on the same vertical centre. This is preferred to
floating the controls over the card, which reserves no space for them and lets
long labels slide underneath.

Tooltips on these markers carry only what the visible line does not already say.
The tooltip is a single clamped line; spending it on a verbatim echo of the text
under the cursor pushes the part that matters — the originating thread name, the
job name — past the ellipsis. When a marker has no detail beyond its label, it
carries no tooltip. Accessible names are exempt: they keep the full sentence,
since assistive technology is not subject to the clamp.

### Selection Rows

Compact selectors, menu items, picker options, sidebar thread rows,
plugin/skill rows, popover command rows, and compact breadcrumb controls share
the same interaction language:

- rest state is borderless unless the control belongs to a framed toolbar
  family;
- hover/open/highlighted/selected states use neutral background elevation and
  text emphasis;
- ordinary pointer hover or open states do not add visible borders, inset rings,
  or outlines;
- stronger outlines are reserved for inputs, drag/drop targets, validation,
  destructive confirmation, or focus-visible accessibility.

A row or group header may keep its secondary metadata and its management actions
hidden at rest and reveal them on hover, so a list of many rows stays quiet. Such
a control must reveal on keyboard focus as well, or it is unreachable without a
pointer. Reveal by changing opacity rather than by mounting the control, so the
row does not shift as the pointer crosses it. Metadata that only identifies the
row — a path, a source URL — may stay a tooltip instead of taking layout at all.

### Scrollbars

A scrollbar is a control, so it is sized by what the pointer must catch rather
than by how much ink it should spend. Those are two different numbers, and the
shared treatment keeps them apart: `--scrollbar-size` is the grab target and
`--scrollbar-thumb-inset` insets the painted slider inside it, so the bar can
read as quiet while remaining easy to take hold of. Widening the visible slider
to make it catchable, or narrowing the target to make it discreet, gives up one
requirement to serve the other.

This matters most at a window edge. A frameless window reserves a resize border
just inside its own edge, and a scroll region flush against that edge puts its
scrollbar inside the reserved strip; a target no wider than the strip is caught
by the window, not by the thumb.

The thumb also carries a floor (`min-width` / `min-height`). A thumb sized in
proportion to a long document shrinks toward nothing, and a slider a few pixels
tall cannot be grabbed however wide its track is.

Three states, all neutral: `--scrollbar-thumb` at rest, `--scrollbar-thumb-hover`
under the pointer, `--scrollbar-thumb-active` while dragging. Tracks and corners
stay transparent so the bar never draws a channel through a surface.

Features do not set `scrollbar-width`. In current Chromium it overrides the
shared geometry entirely, so a region that sets it silently opts out of every
rule above; use it only to hide a scrollbar deliberately (`none`), and reach for
`dc-scrollbar-stable` when a region needs to reserve the gutter instead.

### Detail Sections

A detail page stacks several groups — what an item contributes, its metadata, its
settings. Those groups are frameless: a section is marked by a rule under its
heading in `--border-subtle`, not by a border around its rows, and rows inside a
section carry no dividers of their own. Boxing each group turns one readable
column into a stack of cards competing for the same attention, and nesting a
bordered table inside a bordered section doubles the frame.

`--border-subtle` is the quietest rule in the system, for separating stacked
groups. `--border-default` draws a control's own edge and is not used to divide a
page into regions.

A detail page presents its subject; it is not where the subject's state is
changed. Enable switches and similar controls stay in the manage surface that
owns them, for the same reason a dialog does not carry them (see Dialog Headers).

### Interactive Tool UI

MCP tools may render an interactive UI in a sandboxed iframe (see
[Desktop MCP Apps behavior](../clients/desktop-client.md#582-mcp-apps-interactive-tool-views), aligned with MCP Apps).
The app owns the inner UI; Desktop owns only the host frame around it.

- The host frame is a single neutral surface (`--bg-secondary`, `--border-default`,
  8–10px radius) with a quiet header (tool title / app attribution) and the iframe
  below. When an MCP App explicitly sets `prefersBorder: false`, keep the quiet
  header and controls but remove the host border and background. Do not add
  decorative chrome around the iframe.
- The iframe content is the app's own HTML/CSS; Desktop does not restyle it. Hand the
  theme (light/dark) and accent to the UI via host context (`ui/initialize` /
  host-config) so apps can match the desktop; apps choose whether to honor it.
- Keep the frame compact by default; honor `ui/request-display-mode` for expand.
- Non-Desktop clients do not render the iframe; they show the tool result's text. Do not
  design flows that require the interactive UI.

### Inline Visualization

Assistant inline visualizations are conversation-native media, not tool cards. Their host is
transparent and unframed, with no header or attribution row. Host actions sit just outside the
visualization content edge in a narrow host-owned action rail so they never cover the rendered
media. A single available command is exposed directly as a borderless tertiary icon button
rather than being hidden behind an overflow menu. It may appear on hover, keyboard focus, or
coarse-pointer devices and is not included when the visualization is copied as an image.

Historical visualization views are lazy-loaded near the message viewport. Before the preload
boundary is reached, reserve the expected content shape without a running animation. From the
first runtime request through iframe readiness, use one animated, shape-matched skeleton; do not
show a second spinner or eagerly open off-screen visualization views.

Desktop injects the active neutral surface, text, border, focus, accent, and font tokens into the
visualization document. Ordinary visualization buttons follow the shared 32px / 8px Desktop
action treatment; primary actions use neutral inversion rather than an accent fill. Feature
colors remain available for charts and diagrams, not ordinary controls.

## Loading & Progress

Loading is communicated by a placeholder shaped like the content that will
arrive, not by a generic spinner or a "Loading…" label. The shared building
block is `ui/Skeleton.tsx` (`Skeleton`, `SkeletonRow`, `SkeletonList`,
`SkeletonCatalogGrid`) — a `--bg-tertiary` block on the `skeleton-pulse`
animation; the pulse itself is the running signal.

- Known-shape content → skeleton, not a centered spinner. When the layout of
  what is loading is known (a plan, a list, a card grid), render a shape-matched
  skeleton. Reserve the spinner (`animate-spin-custom`) for genuinely shapeless,
  indeterminate waits inside a control — a busy button, an inline refresh, a
  connection check.
- Partial content renders as it arrives. Once part of a streamed payload has
  parsed, render those parts as real content and keep pulsing skeleton rows only
  for what is still streaming. Do not hold arrived content behind a spinner.
- One running signal per surface. If a surface already shows it is working — a
  shimmering badge (`tool-running-gradient-text`), visibly growing diff text, a
  streaming caret — do not add a second spinner beside it. Remove the redundant
  indicator, along with any elapsed-time counter that rides with it.
- Mark loading regions `aria-busy`; give content-free skeletons `role="status"`
  with an `aria-label` so the loading state a removed spinner used to convey is
  still announced. Skeleton blocks themselves stay `aria-hidden`.
- Skeleton animation honors `data-reduce-motion` via the global reduced-motion
  rule; never gate the *meaning* of a loading state on motion — under reduced
  motion the skeleton still reads as a placeholder.

## Appearance Preferences

Desktop exposes an Appearance settings tab backed by `settings.json` and applied to the
renderer root element:

- Theme mode `system | light | dark` via `data-theme` (`system` resolves from the OS).
- A custom accent overrides `--accent` / `--accent-hover`; unset falls back to the per-theme
  token defaults. A custom accent stays restrained per the Colors rules — it is not promoted
  to a primary-action fill.
- Code font size overrides `--text-code-size`.
- Diff markers (`color` vs `+/-`) change how `InlineDiffView` / `DiffViewer` present changes.
- `data-reduce-motion` (`system | on | off`) gates animations; `data-pointer-cursors`
  toggles pointer cursors on interactive elements.

When adding animated, accent-driven, or code-sized UI, rely on these tokens/attributes rather
than hardcoding colors, sizes, or unconditional animations, so user preferences are honored.

## Do's and Don'ts

Do:

- Read this file and inspect nearby Desktop components before changing styling.
- Use tokens and existing style constants before adding new local styles.
- Decide the action hierarchy before choosing a button treatment.
- Route text and icon actions through the shared `Button` component and its
  variants rather than hand-rolling per-call inline button styles.
- Keep action and icon buttons frameless by default; reserve a visible border for
  the `outline` variant / `bordered` icon buttons in special or important cases.
- Keep every view's main visual language neutral unless this file assigns a
  stronger role.
- Test or inspect light and dark themes when changing colors, contrast, or
  control styling.
- Update `desktop/src/renderer/styles/foundations/tokens.css` and this file together when adding a
  reusable token.

Don't:

- Add raw colors in component styles unless they are media, charts, generated
  previews, provider logos, imported assets, or a documented temporary
  migration step.
- Use brand blue, accent borders, decorative gradients, or glow rings for
  ordinary actions.
- Add a visible border to ordinary buttons; frameless is the default and borders
  are reserved for `outline` / `bordered` special cases.
- Add page-specific palettes to feature views.
- Put cards inside decorative cards.
- Add visible borders to ordinary menu rows, picker options, or sidebar rows.
- Use semantic colors as decoration.
- Use oversized type in compact panels, cards, sidebars, dashboards, menus, or
  dialogs.
