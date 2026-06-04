---
version: "0.3.0"
name: "DotCraft Desktop"
description: "Quiet operational desktop UI for repeated agent work."
sourceTokens: "desktop/src/renderer/styles/tokens.css"
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
  glass-surface-strong: "var(--glass-surface-strong)"
typography:
  ui:
    fontFamily: "var(--font-ui)"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.35
    letterSpacing: "0"
  ui-small:
    fontFamily: "var(--font-ui)"
    fontSize: "12px"
    fontWeight: 400
    lineHeight: 1.35
    letterSpacing: "0"
  panel-heading:
    fontFamily: "var(--font-ui)"
    fontSize: "15px"
    fontWeight: 600
    lineHeight: 1.35
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
  secondary-action:
    background: "{colors.bg-secondary}"
    color: "{colors.text-primary}"
    border: "1px solid {colors.border-default}"
  menu-overlay:
    surface: "Solid, opaque (var(--glass-surface-strong) resolves to var(--bg-elevated))"
    border: "none"
    overlapBorder: "1px solid var(--glass-border) on the overlapping edge only"
    rowHover: "var(--sidebar-control-hover)"
  selection-row:
    border: "none"
    hoverBackground: "var(--bg-tertiary)"
---

# DotCraft Desktop Design

This document is the source of truth for DotCraft Desktop visual design. The
canonical token implementation is `desktop/src/renderer/styles/tokens.css`; this file
defines the product-level intent, component rules, and review checklist that
new and changed Desktop UI must follow.

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

- focus-visible outlines and accessibility affordances;
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

Semantic colors should normally appear in icons, compact badges, borders, small
text, or alert surfaces. They should not take over an entire view.

Feature, channel, and provider colors are allowed only as small identity accents
inside icons, avatars, badges, media previews, or charts. They must not become a
view theme.

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

UI fonts are system-first and must not require bundled web fonts. `--font-ui`,
`--font-body`, and `--font-sans` may switch by document language for CJK locales
while preserving the same sizing, weight, and spacing scale.

## Layout

Desktop surfaces should favor dense but organized operational layouts.

- Keep common workflows ergonomic for repeated use.
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

Do not use glow rings, highlighted borders, accent borders, or decorative
gradients to make ordinary controls "stand out." Use placement, hierarchy,
weight, spacing, and neutral inversion first.

## Shapes

Ordinary controls and cards use 8px radius or less unless an established
component family uses another token.

- Toolbar/catalog controls: 8px.
- Compact icon buttons: 6px or 8px depending on the local family.
- Cards and repeated items: 8px.
- Dialogs and elevated popovers: 8px to 10px.
- Pills, badges, and toggles: `999px` when the shape is semantically pill-like.

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

Secondary actions use neutral filled or bordered controls:

- border: `var(--border-default)`;
- background: `var(--bg-secondary)` or transparent for low-density surfaces;
- text: `var(--text-primary)` or `var(--text-secondary)` by priority.

Ordinary management actions, including `Manage`, `Configure`, `Refresh`, and
repeated row controls, are secondary actions unless they are the one immediate
submit/continue action. They must not use decorative gradients, accent-tinted
borders, glow rings, or provider colors.

Tertiary actions are transparent text/icon controls with neutral hover feedback.
Use them for inline affordances, low-frequency commands, and compact toolbars.

Destructive actions must use explicit copy such as Delete, Remove, Discard, or
Stop. Use `--error` for the danger affordance, but keep surrounding chrome
neutral and require confirmation where appropriate.

### Icon Buttons

Toolbar icon buttons generally use a 32px square treatment:

- 32px width and height;
- 8px radius for toolbar/catalog controls;
- `1px solid var(--border-default)`;
- `var(--bg-secondary)` background;
- `var(--text-secondary)` icon color.

Modal close buttons are the main exception. They should be borderless,
transparent icon buttons with neutral hover feedback.

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

The thread sidebar and thread-header overflow menus are the reference treatment
for ordinary Desktop menus: neutral overlay surface, quiet elevation, no outer
frame, and borderless rows.

### Inputs

Text inputs, textareas, selects, search boxes, and picker triggers stay neutral:

- use `--bg-primary`, `--bg-secondary`, or dedicated input tokens such as
  `--composer-input-background`;
- use `--border-default` for rest state; the primary message composer is an
  exception — it is frameless at rest (its raised fill plus a soft shadow separate
  it from the conversation), shows a soft brand-gradient glow that gently breathes
  on focus (`--composer-focus-glow`), and lifts slightly on hover;
- use `--accent` only for subtle focus-visible or focus ring affordances;
- use `--text-primary` for values and secondary/dimmed tokens for placeholders;
- do not use brand or semantic fills for ordinary input backgrounds.

Validation combines copy, border/icon treatment, and semantic tokens. Error or
warning color identifies the issue without taking over the form.

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

## Do's and Don'ts

Do:

- Read this file and inspect nearby Desktop components before changing styling.
- Use tokens and existing style constants before adding new local styles.
- Decide the action hierarchy before choosing a button treatment.
- Keep every view's main visual language neutral unless this file assigns a
  stronger role.
- Test or inspect light and dark themes when changing colors, contrast, or
  control styling.
- Update `src/renderer/styles/tokens.css` and this file together when adding a
  reusable token.

Don't:

- Add raw colors in component styles unless they are media, charts, generated
  previews, provider logos, imported assets, or a documented temporary
  migration step.
- Use brand blue, accent borders, decorative gradients, or glow rings for
  ordinary actions.
- Add page-specific palettes to feature views.
- Put cards inside decorative cards.
- Add visible borders to ordinary menu rows, picker options, or sidebar rows.
- Use semantic colors as decoration.
- Use oversized type in compact panels, cards, sidebars, dashboards, menus, or
  dialogs.

Review checklist:

- No new raw colors were introduced without a documented exception.
- Ordinary primary actions use neutral inversion, not accent blue.
- Each surface has at most one immediate primary action.
- Semantic colors are used only for status, risk, or validation.
- Provider/channel colors remain small identity accents.
- Inputs and pickers remain neutral.
- Ordinary menu overlays are borderless and share the solid opaque surface; the
  only border is a single hairline on a submenu/stacked overlay's overlapping edge.
- Modal close buttons are borderless transparent icon buttons.
- Focus-visible state is present and accessible.
- Light and dark themes preserve contrast and hierarchy.
