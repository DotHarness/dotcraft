---
version: "0.27.0"
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
  success-bg: "var(--success-bg)"
  warning-bg: "var(--warning-bg)"
  error-bg: "var(--error-bg)"
  info-bg: "var(--info-bg)"
  success-text: "var(--success-text)"
  warning-text: "var(--warning-text)"
  error-text: "var(--error-text)"
  info-text: "var(--info-text)"
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
    padding: "no vertical padding; label inset 6px to align with tool-row text"
    divider: "full-width 1px border-default; 8px below standalone text, 4px below disclosure text"
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

`--success-bg`, `--warning-bg`, and `--error-bg` are the tinted surfaces for
those hues. Each is mixed off its hue token, so both themes follow one value.
Use them behind status badges and notice strips, with the hue itself as the
foreground; reach for a local `color-mix` only where a surface needs a different
strength than the shared step.

One inverse surface exists. `--bg-inverse` is the opposite theme's tertiary tone
— light in dark mode, dark in light mode — with `--text-on-inverse`,
`--text-on-inverse-muted`, `--border-on-inverse`, and `--fill-on-inverse` on top
of it. It is reserved for transient, non-interactive labels: tooltips, and nothing
else. Menus, popovers, dialogs, and hover cards are places
the pointer goes, and they keep the ordinary elevated surface. Do not reach for
the inverse pair to make a control stand out; that is what the Elevation rule
below already forbids.

Feature, channel, and provider colors are allowed only as small identity accents
inside icons, avatars, badges, media previews, or charts. They must not become a
view theme.

`--chart-series-1` through `--chart-series-4` and `--chart-series-other` are the
data-visualization palette: four hues in a fixed order, each theme stepped for its
own surface and validated for color-vision deficiency, plus a neutral for the
folded remainder. Assign them in order, fold everything past the kept series into
the neutral, and keep chart text, axes, and legends on `--text-*` and `--border-*`
tokens.

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
| `--type-detail-title` | 20 / 27 | the named subject in an identity-led detail header |
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

### The conversation scale

The transcript has its own four tiers. Code follows the code font size setting;
the other three derive from `--conversation-font-size`, so one value sizes the
rest of the conversation.

| Token | Size / leading | Use |
| --- | --- | --- |
| `--conversation-font-size` | 14 / 1.5 | messages, markdown body, card titles, and every process line: the activity divider, tool rows, thinking and reasoning, system notices |
| `--conversation-secondary-size` | 13 / 1.5 | content nested under a process line: expanded tool output and commands, card subtitles and counts, reference chips, error blocks |
| `--text-code-size` | 12 / 1.5 | code blocks, diffs, inline code, file content; follows the code font size setting |
| `--conversation-meta-size` | 12 / 16 | timestamps, message origin, key hints, counters |

A process line ranks against the answer by colour, not size: it stays at
conversation size in `--text-secondary` or `--text-dimmed`. Markdown headings
scale from the body in `em` (1.5, 1.25, 1.125, then 1 for `h4`–`h6`) at weight
600. Composer controls outside the input are UI chrome and use `--type-ui`.

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
  Plugin detail is the narrow exception: item-scoped actions may sit beside the
  title block.
- Every full-page catalog surface, including Automations, shares one browse frame
  below that band: a pinned header padded `28px 64px 16px` carrying the hero title
  and search row, then a scrolling body padded `28px 64px 48px`, both centering
  their groups on the same 760px column. A surface that builds its own band, pads
  its hero differently, or scrolls that hero away reads as a different page.
- Inside that frame a group is a `16px`/`700` title over one grid of `58px` rows:
  two columns at `34px`/`18px` gaps for items the user can add, one column at `4px`
  for records the user already owns. Every row is a borderless `8px` box with a
  `40px` leading mark, a `--type-ui` title and one truncating `--type-secondary`
  line, and fills with `--bg-tertiary` on hover rather than gaining a border.
- The Plugins browse page names the active workspace in the title of its installed
  group (`Installed in <workspace>`), because that group is the state that changes
  with the workspace. The band's tabs and breadcrumb and the hero above the search
  row never carry workspace scope.
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
- A notice that interrupts a surface takes that surface's content column: the
  same width and centering as the rows, cards, or grid it sits above, never the
  full content box. A notice wider than the column it interrupts reads as a
  different page. Where several elements share a column, they read its width from
  one place, so a new element cannot silently opt out of it.

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
- Workspace resize dividers rest as a `1px` hairline and answer hover and drag
  with one shared highlight: a `1.5px` neutral vertical gradient
  (`--main-surface-edge-glow`), brightest at centre and fading out toward both
  ends. It is a functional affordance in a neutral tone, which is why the rule
  below against glow on ordinary controls does not reach it.
- File viewers and docked file lists inherit the surrounding main surface instead
  of introducing a secondary panel fill. Use secondary and tertiary surfaces for
  controls, hover states, and selected rows within them.
- Editable file viewers keep the same neutral viewer chrome as read-only files.
  Source, read-only code, and Markdown source share Shiki grammars, paired theme
  tokens, code font, size, line height, gutter, text origin, indentation, and wrapping.
  CodeMirror supplies editing mechanics, not a second syntax palette. Focus adds
  only caret, selection, and current-line feedback. Asynchronous highlighting maps
  existing spans through edits until the current document's result arrives; stale
  results never replace current content or reset selection and scroll position.
- Markdown opens as an editable semantic document using document typography and
  shared code typography/theme inside code. Syntax-tree decorations cover headings,
  lists, quotes, emphasis, links, inline/fenced code, GFM tables and strikethrough.
  Heading markers reveal when the focused selection touches the marker; emphasis,
  strikethrough, inline-code and link markers reveal within their focused structure.
  Lists retain source markers. Top-level quote markers stay hidden; nested quotes
  keep their markers. Tables retain their cell layout, hiding pipe/delimiter syntax;
  closed code fences retain their rendered boundary and language label during focus.
  Mermaid reuses the existing renderer. The header owns View source /
  View preview. Each mode retains its own selection and scroll position, with
  history restored only for the matching content version.
- The floating editor toolbar sits 16px from the bottom and right, with compact
  undo/redo controls, 1px gaps, 4px padding, a neutral border, 8px corners, a 90%
  elevated surface and backdrop blur. This is a local exception to the opaque
  ordinary-menu rule, not a new overlay treatment. Source shows undo/redo and
  reports failed saves through notifications. Semantic Markdown additionally shows
  icon-labelled Saving… / Save failed. Hide the toolbar when it has no useful state.
- Focused file editors own their Find shortcut. A top floating find bar contains
  query, result count, previous/next and close; it does not expose replacement or
  open the window-wide find overlay simultaneously.
- Large files keep their corresponding view read-only, with a neutral outlined
  notice inside the body rather than a yellow warning strip. External-change
  review is a single-column diff with insertion/deletion highlighting. Its fixed
  footer orders wrapping, Edit, Reject (danger), Accept (success). Keep the underlying
  editor session mounted. An over-limit diff shows an unavailable-preview message
  without removing applicable decisions. Behavior and size limits are specified in
  [Desktop UX §10.1](../clients/desktop-client.md#101-viewer-panel).

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

### Identity marks

Product, plugin, skill, channel, provider, and connected-app artwork uses one
identity-mark family. Avatars, status dots, thumbnails, favicons, action glyphs,
and illustrations keep their own shape rules. Choose a role instead of deriving
corner geometry from an arbitrary size:

Agent avatars derive their complete visual identity and interaction rendering from
the shared `@dotcraft/avatar` package. Hosts pass names, state, expression, gesture,
and hand/work-prop intent without copying or overriding internal SVG artwork. At
`20px` and below, keep the robot arms but hide face, hand, overlay-skin, and effect layers
while retaining the head and back silhouettes. Item effects animate only at `44px` and above with
motion enabled. A left-side hold owns the left hand. Native work props take priority
over decorative hand poses when both are requested. Rarity is catalog and settings chrome; it
never paints onto an avatar in product lists. See
[Avatar System](../features/avatar-system.md).

| Role | Standard size / radius | Use |
| --- | --- | --- |
| Compact | `24px / 6px` | Dense metadata, prompt prefixes, and connection rows. Inline marks may reduce to `18px` while retaining the compact radius. |
| List | `40px / 8px` | Catalog, management, dialog-header, and repeated identity rows. Local layouts may use `30–40px` without creating another radius tier. |
| Hero | `60px / 16px` | One prominent product or plugin identity at the start of a detail or setup surface. Hero use is intentionally rare. |

Use `object-fit: contain`; cropping belongs to thumbnails. Identity-mark shells
are transparent. Each icon or logo asset owns its complete visual treatment,
including any background required for reliable contrast. Artwork that remains
legible in both themes may stay transparent. An item that ships no artwork gets the
shared neutral fallback mark, never a generated initial: one shape per kind — a cube
for a Skill, a plug for a Plugin, a message bubble for a Channel, each echoing that
destination's own icon — shaded from `--text-primary` over the neutral fallback
ground, at 68% of the shell. Each mark is one connected silhouette; only the cube
carries more than one tone, because its three faces tile without overlapping, and a
second tone on an appendage reads as two shapes crossing instead of one object.
One kind, one shape, at every size and weight: a Skill is a cube wherever it appears
— the destination, the reference chip in a message or tool row, the included-content
row, the fallback mark — filled at identity sizes and drawn as line art inline.
`Puzzle` belongs to Plugins and Extensions and is not borrowed by another kind. A column of such items then
reads as one family instead of as many unrelated colours, and the shape survives the
`16px` sizes where an initial cannot. Overlapping marks separate with a ring in the
page colour; the `1px` hairline alone is too quiet at that size. Hero shells use
only the near-invisible `1px`
`--identity-mark-hero-border` hairline (about 8% ink); compact and list shells
remain unframed unless interaction requires a boundary. Reserve circles for
people, presence/status, toggles, and circular actions.

Identity marks use the renderer's supported squircle treatment. Their documented
`6px`, `8px`, and `16px` radii are the base geometry; on engines that support
`corner-shape: superellipse(1.5)`, the shared `1.25` radius compensation keeps
the perceived corner equivalent while producing the flatter, continuous curve.
Do not apply the compensated radius without the matching corner shape.

Hero-led detail headers place the identity row `16px` below the mark. The subject
uses `--type-detail-title` (`20px / 27px`, weight `500`); the subtitle uses
`--type-body` in `--text-secondary`, with `6px` between them. Actions align to
the row's top. Plugins and channels share this rhythm.

Use the shared `IdentityMark` primitive and semantic radius tokens. Apply optical
padding to the artwork, not the shell.

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

Inline references and subagent names are not quiet actions. They answer hover by
lifting their text to `--text-primary` (see Inline Reference Chips), which is the
one hover treatment a text-only target may carry — never a fill, border, or pill
drawn around the words.

Destructive actions must use explicit copy such as Delete, Remove, Discard, or
Stop. The danger affordance is a frameless `--error` fill (~10% tint, hover ~18%)
with `--error` text — not a bordered outline. Keep surrounding chrome neutral and
require confirmation where appropriate.

Buttons that share a row share one control band, so the row reads as one strip
rather than a set of controls that each chose a height. A surface picks its band
once; nothing opts in per control.

| Band | Height / radius / type | Where it applies | `size` |
| --- | --- | --- | --- |
| Standard | `32px` / `8px` / `13px` | every surface not named below, including dialogs | `default` |
| Compact | `28px` / `10px` / `12px` | a denser row inside a standard-band surface | `sm` |
| Catalog top bar | `28px` / `10px` / `13px` | every control in a catalog top bar — text, icon, compound trigger, search field | `toolbar` |
| Settings surface | `28px` / `10px` / `13px` | every button, select, and icon button inside `.dc-settings-surface` | token-scoped |
| Prominent pill | `38px` / `999px` / `13px` | one standalone high-emphasis call to action | `prominent` |
| Install pill | `28px` / `999px` / `12px` | the plugin-package Install action across browse, manage, and detail | `sm` |
| Icon, standard | `32px` / `8px` | ordinary icon buttons | `icon` |
| Icon, compact | `28px` / `10px` | icon buttons on either compact band above | `iconSm` |
| Icon, viewer chrome | `16px`, `24px`, or `28px` | a viewer tab slot or toolbar that already reserves that footprint; the in-app browser toolbar scopes the catalog band (`28px` / `10px`) over every control, including its address field | — |

Horizontal padding is around `12–14px` (`10px` on the settings band), icon+label
controls keep a `6px` gap, and every band sets `box-sizing: border-box`.

The two compact bands are shorter and rounder than the standard one because they
run a strip of many small controls rather than one decision. The settings band is
scoped by token rather than chosen per control, which is why a group's header
action fits inside a header instead of overhanging the gap to the card below, and
why `sm` reads as a `10px` radius there: the scope rewrites it, and there is no
`--button-radius-sm` token. Dialogs open outside that container and keep the
standard band.

The prominent pill is a deliberate exception, not a second default: the single
primary button in a focused setup or install dialog, or a lone full-width confirm.
When one shares a row with other buttons, raise the others to its height so the
row still aligns. Ordinary in-row and repeated actions stay on the standard band;
native-app installation and other row actions keep the ordinary radius.

Settings and catalog header actions carry their glyph when the verb has one — Plus
for create, a trash glyph for Delete, the refresh glyph for Refresh — at `15px`
with the shared `6px` gap, so the label never has to do the work of the icon.
Catalog top bars prefer icon-only actions with tooltips for repeated management
commands such as Refresh and Manage, keeping the labelled action for the one
principal command.

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

All ordinary action labels use `text-box: trim-both cap alphabetic`; compound
labels use the shared `ButtonLabel` slot and keep icons outside that slot.
Loading overlays the spinner without changing the control's footprint or accessible name.

Text buttons use a dedicated `--button-text-radius` pill radius: default actions
are 32px high, compact actions 28px, and prominent actions 38px. Catalog and Builder
toolbars use the same 28px height and 10px radius. Icon buttons, navigation, menu
rows and Composer controls retain their own geometry. Builder Create is primary;
Preview/Edit is secondary with a subtle fill.

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

A mode toggle may reveal its label when it turns on. The control keeps its band
height and its icon position and grows only on `max-width` and inline padding —
over `--duration-expand` on `--ease-expand`, the label fading in on opacity — so
the change reads as the control settling into a state, not as press feedback. The
growth is taken from the row's flexible neighbor; fixed peers do not move. The
shared reduced-motion rule collapses the transition. The in-app browser Annotate
control is the reference case; ordinary toggles keep the icon-only `active` tint.

### Icon Buttons

Icon buttons (the shared `IconButton`, styled by `.dc-icon-button`) are frameless by
default, matching the frameless action language:

- the footprint of the band they sit in (see the control band table above);
- transparent surface with a reserved `1px` transparent border;
- `var(--text-secondary)` icon color, with a neutral hover fill
  (`var(--bg-tertiary)` + `var(--text-primary)`);
- `active` marks a selected/toggled state with a subtle accent tint.
- `aria-pressed` marks a mode toggle; its on state uses the same accent tint and
  never an accent border.
- `aria-expanded="true"` marks an open menu or popover with a neutral fill; opening
  ordinary chrome is not a selected accent state.
- destructive icon-only actions use the shared danger tone rather than a locally
  painted red border.

The shared hover, focus, disabled, open, and danger treatments apply at every
footprint in that table.

Thread List icon actions answer on the foreground alone. The thread or project
row already owns the hover and current-state surface, so its compact actions and
its section-header options and create actions stay transparent through rest,
hover, focus, open, and pressed, moving their icon from the quiet foreground to
the primary one; a second rounded surface inside an already highlighted row reads
as a box drawn on a box. `focus-visible` keeps the shared outline and the hit
target stays fixed. Pin may use its filled icon instead of an active background.
Archive stays neutral here because archived threads are recoverable; danger colour
is for the irreversible. These rows carry a details card, so their tooltips take
the block axis (see Hover Annotations).

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

### Navigation Icon Motion

Sidebar destinations answer hover and keyboard focus with one short glyph motion.
This covers New chat, Search, Channels, Agents, Automations, Plugins, and Settings,
in the expanded rows and the collapsed rail alike, and a plugin-contributed
destination may join under the same contract. The row and its hit target never
move; one part of the glyph does, for 340–720ms:

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

- Every motion starts and ends on the static drawing, so leaving mid-motion returns
  the glyph to rest and the shared reduced-motion rule leaves it still.
- A glyph plays once per hover or focus. Sweeping the pointer across the sidebar
  moves only the glyph under it, never a row of them.
- Disabled rows stay still; the current destination still answers.
- Content rows (projects, threads, Chats) keep static icons.
- A plugin destination's icon may move. The Host marks every destination row and
  rail button with `data-nav-icon-host`; the plugin's own stylesheet keys its motion
  off that ancestor's hover and focus-visible states and follows the rules above.
  Oratorio's baton is the reference.
- Motion moves parts; it does not reshape them. A shape change of a pixel or two,
  such as a blink, reads as a flicker at 16px.
- Judge motion at 16px, not on the 64px drawing. A part that appears arrives on
  opacity, in sequence, rather than hopping across pixel rows, and is drawn at least
  2.5 units wide; two strokes a unit or two apart, such as a lid held over a rim,
  merge into one, so the glyph never draws them together.

### Status Indicators

A status indicator is one glyph that says what state something is in, read
together with the label beside it. Every surface uses the same one, so a row in
Settings, a detail header, and a card all say "healthy" the same way.

The shape never varies: an 8px circle with a `999px` radius that does not
shrink, drawn inside a fixed 14px inline-flex box. The box is what centres it,
so alignment is never corrected with `margin-top`, `margin-bottom`, or
`vertical-align`; two indicators in adjacent rows sit on the same line because
they share the same box, not because each one was nudged. The box also lets a
spinner or an icon take the indicator's place without moving the text.

The indicator precedes the label it describes and belongs to that label, not to
a title or name beside it. Colour lives in the indicator alone: the label stays
`--text-secondary`, and a failure reason follows it as ordinary neutral text.
Colouring the words as well doubles the signal and makes a healthy row shout.

Five fills carry every state:

- `--success` for the state worth noticing — running, in use, connected where
  being connected is the point;
- `--warning` for degraded but recoverable, and for work waiting on a decision
  the reader has to make;
- `--error` for failed or blocked;
- `--info` for a step the reader confirms rather than judges;
- `--text-dimmed` for every quiet state — ready, idle, offline, unknown — which
  the label distinguishes in words.

There is no hollow, dashed, or translucent variant: a ring reads as a different
component, and a dimmed circle already says "nothing is happening here". A
transitional state — connecting, testing, restarting, a turn still running — shows
the shared `Spinner` in the indicator's box rather than a colour, because a colour
would claim a result the system does not have yet. The box does not dim it: a dot is
a quiet fact and carries its own fill, while a spinner is the live thing in the row
and reads at the row's own text colour. When the label does not already name the
state, the indicator carries an accessible name of its own, since colour alone is not
readable.

When the state is the whole content rather than an attribute of a row, use a
badge instead: the tinted surface tokens (`--success-bg`, `--warning-bg`,
`--error-bg`, `--info-bg`) with the reading ink (`--success-text`,
`--warning-text`, `--error-text`, `--info-text`) as the foreground, and no
indicator inside it. A badge and an
indicator never appear together for the same fact.

The tint is the whole badge. A badge carries no border, because a frame turns a
state into a chip that looks pressable, and a column of framed states reads as a
column of controls. In a dense list the badge takes the compact size — the row's own
secondary type at ordinary weight inside an `18px` pill — so it sits inside the row's
height instead of setting it, and it truncates rather than pushing the title. While
the row is hovered or selected the badge drops its hue and goes neutral: the
highlight is already the stronger signal, and two of them competing is what makes a
list of waiting work look loud. A state keeps its own hue, and that hue comes from the
fixed status tokens rather than `--accent`, which the reader is free to change.

A settings surface says nothing when everything is fine. A green badge
confirming that a binary was found, a section headed Status that only ever
reports health, an all-clear a reader never acts on — none of them earn their
room, and a page that announces its own success has no weight left for the one
row that actually needs attention. Show the trouble instead: a notice at the
top of the page, in the reader's path before the settings it explains, naming
what is wrong and the way out of it. States that are merely quiet stay in the
row they describe, as an indicator or as plain secondary text.

Words that classify rather than report — default, custom, customized, the name
of a tier — are labels, not status, so they carry no pill, border, or fill on a
settings row any more than they do above a transcript block. Set them in the
hint size on `--text-dimmed` beside the title they qualify, and drop the ones a
section heading already says.

### Status Menu Buttons

A compact status menu button combines a current-state label with an overflow
menu when a repeated row would otherwise expose several competing actions. It
is a state affordance, not a second primary action:

- the trigger takes the control band of the surface it sits in and carries the
  shared status indicator, a concise label, a trailing chevron, and a persistent
  `1px solid var(--border-default)` outline;
- hover and open states may strengthen the neutral fill and border together,
  but the frame never becomes an accent border;
- the indicator follows Status Indicators above, and the label stays neutral;
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

### Toasts

Toasts are transient cards in the top-right stack. A toast is either present or
gone:
there is no remaining-time bar, hovering or focusing the stack holds every card,
and a repeated identical notice replaces its twin instead of stacking.

- Levels are `info`, `success`, `warning`, and `error`. Errors auto-dismiss like
  everything else; a toast that must persist passes duration `0`.
- `info` is the neutral card: elevated overlay tone, neutral border, neutral text.
  The other three tint the whole card — surface, border, title, icon, close, and
  action all take that level's colour. An outcome you are meant to read in passing
  should not depend on finding a small glyph to learn how it went.
- The tint is mixed into the elevated surface rather than laid over it as a
  transparent wash, so a toast stays opaque against whatever it covers.
- Surface and border take the level's own colour (`--success` and friends); anything
  readable on a tinted card takes that level's reading colour (`--success-text` and
  friends). The two roles are separate tokens because a status colour chosen to
  carry a 16px glyph does not clear AA as 13px running text on a tint of itself —
  the yellow reads at 2.3:1 on a light surface. Use the `-text` token wherever a
  status colour becomes prose, not only here.
- Give a toast a `key` when it reports the outcome of something that already showed
  an in-flight toast, so the result replaces the notice rather than joining it.
- An arrival is not an outcome. A toast that hands the user something newly possible
  — a plugin just installed, offering Try now — stays the neutral `info` card and puts
  that thing's `IdentityMark` in the leading slot: the mark says which thing, the
  action says what it makes possible, and there is no verdict to colour. The tinted
  levels report a finished outcome with nothing left to do — removed, failed, blocked
  — and keep the level glyph, since the subject may no longer be there to have a mark.
  Either way the title names the subject.
- A card is `max-content` wide against the stack's right edge and caps at the column
  width, so a short notice stays short instead of reserving the full 380px.
- The title is one medium-weight line in a 24px box, matching the icon, the action,
  and the close control, so one line of text is always a 42px card. `description`
  adds a second, quieter line in the level's reading colour; with it present the
  actions move to their own row below the text, since an inline action beside two
  lines has no line to sit on.
- The action is a filled `secondary` pill, tinted to the level on a tinted card. It
  is the one thing on a card the user is invited to press, so it is the one thing
  that may carry a fill. The close control stays a frameless `IconButton`.
- Offer inline Undo only when a compensating server call exists, such as archive
  and unarchive. Perform the change immediately and let Undo reverse it; never
  defer the change until the toast expires. Permanent deletes keep their
  confirmation dialog and offer no Undo.
- Offer a forward action such as Try now only when the notice itself is what made
  that destination reachable, and reaching it is this one press.
- The stack is one polite live region; individual cards carry no `role="alert"`.

### Tooltips

A tooltip is the one overlay that is pure annotation: `pointer-events: none`,
gone on the next pointer move, never a place the user can travel to. That is what
separates it from the menu family below, and it is why it does not share their
surface.

- The surface is the inverse pair (`--bg-inverse` / `--text-on-inverse`): a light
  tooltip over a dark app, a dark one over a light app. A label does not belong
  to the plane it labels, and stating that in the fill is what a same-tone
  overlay cannot do: in light theme it separates from the app at about 1.05:1,
  which is no boundary at all.
- No border. The inversion is the boundary; `--tooltip-border` is transparent and
  the shadow carries the lift.
- Type is `--type-secondary` (12/16). The label's 16px line box is what the
  keycap chip beside it is sized to match.
- Copy stays short. The single-line form clamps at 320px with an ellipsis; only
  a genuine explanation takes the multiline form, which wraps to 32rem.

Keyboard shortcuts inside a tooltip — and everywhere else a shortcut is shown —
use one continuous chip:

- one chip per shortcut, keys joined by `+` (`Ctrl+Shift+M`), matching how menu
  rows already write them;
- alternate shortcuts for the same action stay separate chips, divided by `/`,
  so "Enter or Shift+A" never reads as one five-key chord;
- the chip is a 16px box in the UI face at `--type-hint`, flat — no keycap
  emboss, and no per-key frames;
- the DOM keeps one `<kbd>` per key; the joins are drawn, not typed.

A tooltip inside a row that carries a details card takes the block axis. See
Hover Annotations below.

### Hover Annotations

Two things can be worth saying about the row under the pointer, and they are not
the same thing. A tooltip names the control the pointer is on. A details card —
the sidebar's `SidebarEntryDetailsCard` — describes the row's subject: its
project, its branch, when it last ran. The card is why a row can stay one shape
whether or not it came from a channel: metadata that only identifies the row
lives in the annotation instead of in a column the list has to reserve, which is
the trade the Selection Rows rule already asks for.

Both statements are true at the same moment, so they coexist rather than
compete. Closing the card when the pointer reaches the row's own actions would
blank out the context the reader is using, at exactly the moment they act on it.

Coexisting is only possible if the two stop asking for the same screen, so each
annotation owns one axis:

- a row's details card owns the **inline** axis — `right`, mirroring to `left`
  when the card would leave the viewport;
- every hover-revealed control inside that row owns the **block** axis — `top`,
  mirroring to `bottom` when the label would leave it.

Mirroring happens inside an axis, never across it. A control inside a
card-bearing row does not take `left` or `right`: that side belongs to the row,
and a label placed there lands on the card it was meant to sit beside, covering
the line the reader was reading. A row rail sets the axis once for every action
in it rather than per button, so the rail cannot drift control by control.

This governs annotations only. Menus and popovers opened by those same controls
are places the pointer travels to, and they keep their own positioning.

A tooltip is placed against the control's own box. A hover-revealed rail action
is normally taken out of flow so it can sit at the row's trailing edge without
taking layout, which collapses the wrapper around it to `0×0` and leaves the
label measured from a phantom point at the row's centre — low enough to clip the
card even with the axes already correct. Anchor to what is drawn, or reserve the
control's footprint in the row so it can stay in flow.

The card itself is an ordinary elevated surface, not the inverse pair: it is
sometimes a place the pointer can travel, so it follows the Colors rule above
rather than the tooltip's inversion. It tucks under the row's trailing edge and
carries the single `--glass-border` hairline on that overlapping edge, as the
Elevation rule requires. It opens on a delay and closes on a short grace period;
a tooltip on a control the pointer has already arrived at does not.

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

### Reply selection overlays

Reply text selection actions form a content-sized horizontal strip with a subtle
internal separator and no reserved action slots. Use the existing opaque elevated
surface, neutral hover treatment, and compact controls. Its comment editor is a
separate width mode. One active portal per window follows the selected range and
stays inside the viewport. Reply text context menus use native platform chrome
and native editing commands.

### Headings that name a selection

An entry heading may name the place the next action runs and let that name be
changed in place, rather than repeating the choice in a control beside it:

- the name stays part of the sentence — it keeps the heading's family, size,
  weight, colour and letter-spacing, and gains no pill, chevron, border or
  background;
- a `1px` dotted underline in `--text-tertiary` at a `4px` offset is the only
  resting affordance; hover and the open state move the text and the underline
  to `--text-secondary`;
- punctuation belongs to the sentence, not to the name, so it stays outside the
  underline in every locale;
- the heading and any control that offers the same choice open the same menu
  built from the same rows, so the two entry points cannot drift;
- the menu opens over the space above the heading, away from the primary input
  below it, and flips only when the window leaves no room;
- when there is nothing to name, the heading drops the clause and the control
  beside the input asks for the choice instead. The heading never names a place
  that does not exist.

An entry heading carries no second line of guidance that repeats what the input's
own placeholder and state already say.

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
  action row; project, the machine tools run on (Run on), work location,
  source-control branch or changelist, and provider subscription status form the
  context row below the card, with subscription status immediately following the
  branch or changelist control. Context-row chips share one footer pill (28px,
  pill radius, `--composer-footer-text`) and open their menus upward from the
  row; the work-location chip names the branch it works on in a hover tooltip;
- the send control is one 32px button whose glyph follows the turn: Send at rest
  or with a draft to steer or queue, Stop while a turn runs with an empty draft,
  and a spinner while stopping. The glyphs stack in one cell and crossfade on
  opacity alone so the control never moves or scales; reduced motion collapses
  the fade;
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

Search uses one composed control across catalog and feature pages: a pill-radius
shell containing the search glyph and a bare `Input`. When filters are available,
one compact filter icon sits beside the shell and opens filter dimensions as
submenus; pages do not place a row of select fields beside search. The shell keeps
the ordinary field focus border. Filter dimensions use semantic leading icons only
when every peer dimension has one; the selected option uses a trailing check so its
label stays aligned with the other options.

An always-editable page or detail title reads as text rather than a form box. It
stays frameless while focused; the caret and text selection communicate editing,
so the title does not add an underline or a surrounding focus frame.

Desktop-owned text fields use the shared `Input` and `Textarea` components rather
than a locally styled native element, so height, radius, placeholder, hover,
focus, invalid, and disabled treatments stay identical. The components own their
own height and never set `flex`; callers place them in a row or column layout and
pass only their genuine deltas. Two cases stay native: a visually hidden control
that supplies semantics, and an inline editor embedded in a canvas surface whose
own class already follows the focus rule above.

Validation combines copy, border/icon treatment, and semantic tokens. Error or
warning color identifies the issue without taking over the form.

Visible select controls, checkboxes, and editable suggestion fields in Desktop-owned
UI use the shared `Select`, `Checkbox`, and `Combobox` components so their menus,
focus treatment, disabled state, and keyboard behavior remain consistent. A native
form control may remain only when it is visually hidden and supplies semantics.
Third-party content rendered in sandboxed views is outside this rule.

Desktop-owned opaque RGB color choices use the shared `ColorPickerDialog`, not
`input[type="color"]`. The compact Host-owned dialog contains a Hex field, saturation/value
plane, Hue control, and explicit Done/Reset actions. Edits preview inside the dialog; Done
commits, Reset clears the semantic override immediately, and every close path cancels. A hidden
native file input may still open the system file chooser because file access remains a platform
capability rather than a Desktop-owned visual editor.

### Inline Reference Chips

File, command, skill, link, scheduled-task, and profile references — in the
composer, in sent bubbles, in markdown, and in tool rows — are quiet inline content
rather than standalone controls: a type icon and a tinted label, with no border,
fill, or pill at rest or on hover. Hover answers on the text alone, as the subagent
chips do: an interactive reference lifts to `--text-primary`, and one that navigates
(a link or a button that opens something) adds a 1px dashed underline offset 2px. A
composer pill instead reveals its neutral remove affordance, with default and remove
icons occupying the same fixed slot and swapping through opacity rather than entering
or leaving layout, so hover never changes the chip width, text baseline, caret
position, or the position of surrounding text. Inert references (a value merely
named by a tool row) do not react to hover. Use vector icons from the shared icon
language instead of font-dependent Unicode glyphs.

The tint is not a per-type code. Everything that goes somewhere — a file, a command,
an automation, a link — carries one reference colour (`--ref-base`) and lets the icon
name the type; skills are the one exception (`--ref-skill`), a capability rather than
a destination, and an inert value carries no tint at all. Both colours sit near the
text lightness rather than at full saturation, so a chip reads as text that happens to
be reachable rather than as a saturated link, and both clear 4.5:1 on their surface.

A click leaves nothing behind: the reference opens its target and keeps no frame
afterwards. Keyboard focus is the only state that draws a ring, through the shared
`focus-visible` outline, because it is the only one the reader cannot otherwise
locate. A reference never paints a ring from a focus event, which a mouse click
fires too.

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
row — a path, a source URL, an origin channel — may stay in a hover annotation
instead of taking layout at all: a tooltip when it is one line, a details card
when it is several. Metadata that only some rows carry belongs there by default:
a column reserved for it makes the rows that have it a different shape from the
rows that do not, and the list reads as ragged rather than as one column.

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

Features do not set `scrollbar-width`. Chromium treats it as overriding the
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

A detail page presents its subject; standing state controls stay in the manage
surface that owns them, for the same reason a dialog does not carry them (see
Dialog Headers). Plugin detail also manages its included Skills in a separate,
counted section. Each row uses a Skill identity mark, a document-preview button,
and an independent trailing switch backed by the same state as Skills management.
Uninstalled Skills remain read-only previews; installed Skills under a disabled
plugin show an off, disabled switch. Skill preview dialogs carry no switches.
The section previews five Skills and hides the rest behind one disclosure row of the
same height, which names the next two hidden Skills and counts the remainder ("See
Page diff, Form fill, and 3 more") over a stack of their marks, so the row says what
expanding reveals rather than only how much; the heading keeps the full count.
Expanding shows every Skill and turns the row into Show less. Collapsed, the label
starts at the Skill titles; expanded, it starts at the mark column. The row carries
no hover fill — it is a label, not a list row — and lightens its text instead.
The metadata section lists capabilities, developer, category, and version before the
website, privacy, and terms links. Version appears only when the plugin declares one,
and category names the category alone rather than repeating the developer.
Plugin detail uses one task-oriented primary CTA slot rather
than a standing management switch: Install and Enable show in-control progress,
then the same slot becomes Try in chat when the plugin is ready.

### Settings Groups

A settings group is a heading over a card, not a card with a heading in it. The
title, its optional description, and any group-level actions sit above the card as
a frameless header: the title in the `--type-heading` tier, the description one
line of `--text-secondary` 4px beneath it, actions right-aligned and centred on the
title line; when a header carries an action, its row is never shorter than the
settings control band (`28px`), so the action sits inside the header rather than
overhanging the gap below it, while a title-only header keeps the height of its
text. The card follows 8px below and holds rows only — `--bg-secondary`,
`1px solid var(--border-default)`, radius 12, hairline dividers between rows and
no rule above the first — and groups stand 20px apart. A header painted inside
the card's border, under its own rule, turns the frame into a decorative section
box and separates groups with lines instead of space (see Layout).

Explanatory text belongs to the row it explains: a control row is label, hint,
control, with the hint in the `--type-hint` tier under the label and the control
on the right. A group description is reserved for list groups whose rows are
items — paired PCs, servers, people — and so have nowhere else to say what the
group is; it defines the feature in one sentence rather than describing a
scenario or a benefit. A row never repeats its group's title.

Each page or segment has one principal action, and it carries the primary neutral
inversion wherever it sits in the header; refresh and other quiet actions beside
it stay frameless icon buttons. Option cards show their choice through the
selected state alone and carry no status pill. An inline notice uses the shared
banner geometry — 14px padding, 12px gap, a 20px glyph, a 13px/600 title with an
optional secondary line — in the level's own colour, and has no dismiss control:
its height follows its content, and the header's refresh is the retry.

### Pet

The Pet settings tab dresses the one default companion; its rules keep the
neutral posture above while letting the collection's own colour show.

- The dial is a 232px open ring around the companion at 120px: thirteen colour
  segments over 300° with the gap at the bottom, Original first at the lower left
  and the twelve role palettes clockwise over the top, each painted in its
  palette's mid body tone at 72% opacity. The pointer, keyboard focus, and the
  selected segment go to full opacity; the selected segment pops 5px outward and
  carries a 3px white dot. The current colour's name sits in the ring's gap in
  `--type-secondary`; while a colour is being previewed the name is
  `--text-secondary`. With customization off the whole group is hidden.
- Rarity is chrome, never paint on the companion. On a bag tile it is a radial
  glow behind the item art in the rarity colour that grows with the tier
  (uncommon 22%, rare 30%, epic 38%, legendary 46%; common has none) and, from
  rare up, a 1px inset frame in the rarity colour at 55%. There are no rarity
  badges, dots, or pills anywhere. Where a card names the rarity, it is one word
  in the rarity colour at weight 500.
- Bag tiles are `--bg-tertiary`, radius 8, at least 92px wide, art centred with
  the name in `--type-hint` beneath and a `×n` copy count at the top right in
  `--text-secondary`. A worn tile carries a 1.5px inset ring in `--text-primary`
  and a check bubble on the art. The bag is a fixed grid of five columns showing two
  rows: owned items fill it from the first slot and the remaining slots stay visible as
  dashed `--border-default` outlines like the tray's, so an empty or filtered bag never
  collapses and never moves the dial; longer collections scroll. The empty and filtered
  hints sit beside the `Bag` eyebrow in `--type-hint`.
- The exchange tray is ten 44px slots: empty slots are dashed
  `--border-default` outlines; a filled slot drops the outline and shows the item
  over its rarity glow. The result replaces the tray in place with 112px art over
  the glow, the item name in `--type-heading`, its slot and feature in
  `--type-secondary`, the rarity word, and the primary and ghost actions.
- The find toast is the shared toast card with art: it fills the stack width and
  keeps one row of art, text, action, and close. The art slot is 36px with the
  item over a stronger glow (uncommon 40%, rare 50%, epic 58%, legendary 66%)
  and no frame, the title is `New find: <item>`, the description is the rarity
  word, and the card stays neutral so a legendary never reads as a warning.
- The tab icon is the companion's head at Lucide weight: antenna light, rounded
  body, and the terminal prompt as its face, matching the logo mark.

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
block is the shared `Skeleton` family (`Skeleton`, `SkeletonRow`,
`SkeletonList`, `SkeletonCatalogGrid`) — a `--bg-tertiary` block on the
`skeleton-pulse` animation; the pulse itself is the running signal.

- Known-shape content → skeleton, not a centered spinner. When the layout of
  what is loading is known (a plan, a list, a card grid), render a shape-matched
  skeleton. Reserve the spinner for genuinely shapeless, indeterminate waits inside
  a control — a busy button, an inline refresh, a connection check, a running turn
  in a list row.
- There is one spinner: the shared `Spinner`. It is a track ring carrying a
  three-quarter arc, both drawn in `currentColor` at a twelfth of its own diameter,
  turning once per `--animate-spinner`. It has no colour of its own — it borrows the
  ink of whatever it sits in, so a button, a row, and a dialog all wait in their own
  voice and no wait ever claims the accent. Size it to the box it occupies; never
  give it a hue, a thicker ring, or a second animation.
- Partial content renders as it arrives. Once part of a streamed payload has
  parsed, render those parts as real content and keep pulsing skeleton rows only
  for what is still streaming. Do not hold arrived content behind a spinner.
- One running signal per surface. If a surface already shows it is working — a
  shimmering badge (`tool-running-gradient-text`), visibly growing diff text, a
  streaming caret — do not add a second spinner beside it. Remove the redundant
  indicator, along with any elapsed-time counter that rides with it.
- Mark loading regions `aria-busy`; give content-free skeletons `role="status"`
  with an `aria-label` so the loading state is announced. Skeleton blocks
  themselves stay `aria-hidden`.
- Skeleton animation honors `data-reduce-motion` via the global reduced-motion
  rule; never gate the *meaning* of a loading state on motion — under reduced
  motion the skeleton still reads as a placeholder.
- A wait with no shape to match — the in-app browser loading a page — runs a 2px
  accent bar along the toolbar's bottom edge, pulsing on opacity. It is
  `aria-hidden`; the Reload/Stop control is the accessible state. Under reduced
  motion the bar stays as a static rule.

The workspace launch transition is the one wait with no shape to match, because the
workspace it is opening does not exist on screen yet. While it connects or prepares, the
brand mark breathes on a slow four-second loop, peaking three percent above rest and
scaled about its own centre so it never drifts. This is not a second running signal
beside the shimmering caption: the caption reports progress, and the breath only keeps
the surface from reading as a hung frame during a wait that has no upper bound. It
carries no state, appears on no other surface, and rests at both ends of its loop so the
reduced-motion collapse leaves the mark still.

## Appearance Preferences

The palette is stated as a per-variant seed — `--seed-surface`, `--seed-ink`, `--seed-accent`,
and a 0-100 contrast — and `foundations/tokens.css` derives the surface, text, and border ramps
from it with `color-mix`. `surface` is the base plane: the page in dark, the card in light, so
both variants move away from it by mixing in ink. Desktop writes only what CSS
cannot compute (the seed colors, the normalized contrast multiplier `--contrast-k`, and
`--on-accent`), and writes nothing at all for a default seed. The layering rules and the
formulas live in `specs/architecture/desktop-styles.md`.

Desktop exposes an Appearance settings tab backed by `settings.json` and applied to the
renderer root element:

- Theme mode `system | light | dark` via `data-theme` (`system` resolves from the OS).
- A custom accent sets `--seed-accent`, from which `--accent`, `--accent-hover`, and the
  foreground `--on-accent` derive; unset writes nothing and the per-theme token defaults
  answer. A custom accent stays restrained per the Colors rules — it is not promoted to a
  primary-action fill.
- Code font size overrides `--text-code-size`.
- Diff markers (`color` vs `+/-`) change how `InlineDiffView` / `DiffViewer` present changes.
- `data-reduce-motion` (`system | on | off`) gates animations; `data-pointer-cursors`
  toggles pointer cursors on interactive elements.

When adding animated, accent-driven, or code-sized UI, rely on these tokens/attributes rather
than hardcoding colors, sizes, or unconditional animations, so user preferences are honored.

### Plugin surfaces

A plugin settings page uses the same settings grammar as built-in settings: a
group heading over a card of rows, with wide visual choices in a block row rather
than a card nested inside a card. Plugin artwork may carry its own colours; the
controls around it stay on the shared neutral borders, radii, spacing, focus, and
selection states. What a plugin may contribute, and how the Host composites it,
is defined in [Desktop Plugins](desktop-plugins.md).

## Do's and Don'ts

Do:

- Use tokens and existing style constants before adding new local styles.
- Decide the action hierarchy before choosing a button treatment.
- Route text and icon actions through the shared `Button` component and its
  variants rather than hand-rolling per-call inline button styles.
- Keep action and icon buttons frameless by default; reserve a visible border for
  the `outline` variant / `bordered` icon buttons in special or important cases.
- Keep every view's main visual language neutral unless this file assigns a
  stronger role.
- Update `desktop/src/renderer/styles/foundations/tokens.css` and this file together when adding a
  reusable token.
- Remove a token the same way: confirm zero `var(--x)` consumers across
  `desktop/src`, then delete the declaration from both theme blocks and this
  file's front-matter `colors:` map in one change. A token that survives with no
  readers is a phantom the next author will copy.

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
- Wrap an inline reference — a file, skill, link, agent, job, or profile value —
  in a pill, border, or fill, at rest or on hover; hover lifts the text instead.

## Automation editing and run identity

Automations uses one list and one directly editable detail surface. The hierarchy is
editable title, prompt, labelled detail rows, frequency rows, and collapsed advanced
settings. Agent Profile is an identity choice and includes the profile avatar and description.
Dirty drafts expose Cancel and Save; pending saves disable duplicate submission
and errors retain the draft. There is no second settings modal. Show fields only for the
selected execution mode. The primary creation action stages the built-in `$automations` skill
in the Welcome composer and starts a conversation; it does not add a handoff banner inside
the list. Manual creation uses the same editor. Suggestions are direct-add templates: hover or
focus exchanges their identity icon for Add, pending creation shows activity, and success inserts
the task without leaving the surface or opening its detail. The list and editor reuse the shared
resizable divider and edge glow. The
action row relies on spacing and does not add a horizontal rule above Cancel and Save.

Trusted automation tool cards show an operation snapshot and open the latest definition.
Run cards navigate by definition and run ids, and locate the exact turn for follow-ups.
Their summary uses the scheduled-work calendar reference, and their compact disclosure
shows timing, execution, and notification metadata rather than repeating the prompt.
Previous runs are compact conversation-selection rows rather than result cards: the whole
available row opens the exact run, status is a small leading marker, identity stays in the
middle, and relative time stays on the trailing edge. Do not repeat an "open run" action in
every row. An automation attached to an existing conversation exposes one page-level
"Open chat" action in the detail footer; that action opens the attached conversation rather
than a particular historical turn. DotCraft-only run actions such as worktree review remain
secondary to the row's navigation target.
History markers express running, unread, and archived state in that priority order;
execution errors remain in accessible status tooltips. Archived rows retain their
place, fade their title, and exchange the trailing time for Unarchive on hover or
keyboard focus without shifting layout. The context menu owns reading and archive
actions; the section menu owns bulk actions. Touch surfaces expose a menu button.
Task-list state controls and the detail pause/resume button share persisted state
and pending protection. Paused tasks show Play; active tasks reveal Pause over an idle
circle when the status control is hovered or focused. Completed tasks show completion,
and running tasks show activity before every other list marker. Pending schedule changes
retain the current icon instead of impersonating execution. The second line keeps the schedule
for active, paused, and running tasks; only active tasks add the next-run countdown, running
tasks add their in-progress label, and completed tasks replace timing with completion.
Preserve dirty drafts when controls update.

Task rows reserve one trailing action slot. An unread-run marker occupies it at rest; row hover,
keyboard focus, or an open menu exchanges the marker for More actions without moving title or
timing. The menu owns Run now, Pause/Resume, and Delete as allowed by task state. Completed
tasks expose only Delete. Coarse-pointer surfaces keep the menu trigger visible. The scheduled
task group label names the objects directly; it does not use a possessive "Your" heading.
The task subtitle places schedule and relative next-run time together, without a
repeated execution-mode label or a duplicate next-run paragraph in details. Pause
keeps the schedule but omits next-run timing; completion replaces the subtitle with
its lifecycle label. Countdown text
refreshes each minute and on visibility changes; overdue timestamps read as due now.
The design system mounts production components and deterministic stateful fixtures; it
covers editing, save errors/conflicts, pause/resume, history, long content and narrow widths.
