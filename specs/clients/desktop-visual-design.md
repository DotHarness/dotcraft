# DotCraft Desktop Visual Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Living |
| **Date** | 2026-05-21 |
| **Parent Spec** | [Desktop UX Specification](desktop-client.md) |
| **Source Tokens** | [`desktop/src/renderer/styles/tokens.css`](../../desktop/src/renderer/styles/tokens.css) |

Purpose: Define the stable visual decision rules for DotCraft Desktop. This spec gives agents and contributors a shared basis for color, emphasis, control styling, and view-level composition so Desktop remains visually coordinated as new surfaces are added.

This document does not freeze exact layout geometry or require every component to be refactored into shared primitives immediately. It defines the design system contract new and changed Desktop UI should follow.

---

## 1. Design Posture

DotCraft Desktop is a quiet operational tool for repeated agent work. The interface should feel calm, dense enough for productivity, and visually consistent across conversation, settings, skills, automations, plugins, channels, and modal flows.

Design decisions must be neutral-first:

- Use neutral surfaces, text, borders, and spacing as the default visual language.
- Use emphasis sparingly and only when it clarifies the next action, current state, risk, or selected context.
- Avoid page-specific palettes, decorative gradients, and large color washes unless a spec explicitly calls for them.
- Prefer existing Desktop styles, shared components, and CSS variables over inventing local colors or local button/input treatments.

The Desktop brand accent is intentionally conservative. It is not the default call-to-action color.

## 2. Token Authority

The canonical token implementation lives in `desktop/src/renderer/styles/tokens.css`.

New or changed UI must use tokens for color, typography, radius, elevation, focus, and spacing whenever a token exists. Do not introduce raw hex/RGB/HSL colors in component styles unless one of these is true:

- The value is a local rendering artifact for media, charts, generated previews, provider logos, or imported assets.
- The value is part of a documented semantic token addition.
- The value is a temporary migration step and is isolated with a clear follow-up.

Token-derived `color-mix()` is allowed for subtle interaction states, but the base colors should still be tokens. If a color becomes reusable, promote it into `tokens.css` and update this spec.

## 3. Color Roles

### 3.1 Neutral System

Neutral tokens carry most UI structure:

- `--bg-primary`: main content surface and inverted primary-action text.
- `--bg-secondary`: cards, panels, menus, secondary controls, and repeated list items.
- `--bg-tertiary` / `--bg-active`: hover, pressed, selected, or nested neutral states.
- `--text-primary`: primary copy, headings, and inverted primary-action background.
- `--text-secondary`: supporting copy, metadata, labels, inactive icons.
- `--text-dimmed` / `--text-tertiary`: low-priority helper text and empty-state detail.
- `--border-default`: ordinary control and card boundaries.
- `--border-active`: hover, active, or focused neutral boundaries.

Default screens should read as neutral surfaces with clear hierarchy, not as a themed color page.

### 3.2 Brand Accent

`--accent` and `--accent-hover` are reserved for restrained brand and navigational emphasis:

- logo-related glow or brand presentation;
- focus rings and accessibility affordances;
- selected navigation, selected segmented controls, or active state accents when neutral inversion is not appropriate;
- links or small inline affordances where a product accent helps recognition;
- exceptional onboarding or setup moments that intentionally present DotCraft as the product.

Do not use `--accent` as the default primary button background for ordinary actions such as create, start, close, save, submit, or continue.

### 3.3 Semantic Colors

Semantic tokens communicate state, not decoration:

- `--success`: completed, healthy, connected, applied.
- `--warning`: caution, pending review, potentially risky but recoverable.
- `--error`: destructive action, failed state, blocked state.
- `--info`: informational status when neutral text is insufficient.

Semantic colors should normally appear in icons, status badges, borders, small text, or alert surfaces. Avoid turning an entire page section into a semantic color field.

### 3.4 Feature, Channel, and Provider Colors

Feature-specific colors are allowed only as small identity accents:

- channel/provider icons;
- small avatars or badges;
- compact preview placeholders;
- charts or visualizations that require multiple data colors.

Theme-sensitive monochrome provider or agent marks should render as inline `currentColor` SVG so they inherit the surrounding text token in both light and dark themes. Fixed-color brand assets may remain image assets when their color is part of the identity.

They must not become the main theme of a view. A feature page's main color is always the neutral system unless a separate spec explicitly says otherwise.

## 4. Action Hierarchy

### 4.1 Primary Action

Each surface may have at most one primary action in the immediate decision area. Examples: `New Task`, a modal confirmation, a setup-step continue button, or a dialog's main submit action.

The default Desktop primary action style is the neutral inverted button:

```ts
{
  border: '1px solid var(--text-primary)',
  backgroundColor: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  fontWeight: 600
}
```

When matching catalog/header controls such as `New Task`, use the existing `catalogStyles.manageButton` shape as the base: 32px height, 8px radius, 13px type, inline-flex alignment, and 12px horizontal padding.

Primary actions should be concise text labels. Icons are appropriate when the action represents a familiar tool operation (`Plus` for create, save/download/search icons), but avoid adding decorative arrows or extra icons just to make the button louder.

### 4.2 Secondary Action

Secondary actions use neutral filled or bordered controls:

- border: `var(--border-default)`;
- background: `var(--bg-secondary)` or transparent for low-density surfaces;
- text: `var(--text-primary)` or `var(--text-secondary)` depending on priority.

Use secondary buttons for cancel, alternate paths, settings actions, non-destructive management actions, and repeated row controls.

### 4.3 Tertiary and Ghost Actions

Tertiary actions are text/icon controls with transparent background and hover feedback. They are appropriate for low-frequency actions, inline links, and compact toolbars.

Hover/active states should use neutral mixes such as `color-mix(in srgb, var(--text-primary) 8%, transparent)` or existing local variables.

### 4.4 Icon Buttons

Toolbar icon buttons generally use the existing 32px square treatment:

- 32px width and height;
- 8px radius for toolbar/catalog controls;
- `1px solid var(--border-default)`;
- `var(--bg-secondary)` background;
- `var(--text-secondary)` icon color.

Modal close buttons are the main exception: use a borderless transparent icon button with neutral hover feedback so the close control does not compete with the dialog content.

### 4.5 Destructive Actions

Destructive actions must use explicit copy and normally require confirmation. Use `--error` for the danger affordance, but keep surrounding chrome neutral. Do not rely on color alone; use labels such as Delete, Remove, Discard, or Stop.

## 5. Inputs and Pickers

Text inputs, textareas, selects, search boxes, and picker fields must remain neutral:

- use `--bg-primary`, `--bg-secondary`, or dedicated input tokens such as `--composer-input-background`;
- use `--border-default` for rest state;
- use a subtle focus treatment, usually `--accent` or a tokenized focus ring;
- use `--text-primary` for entered values and `--text-secondary` or composer placeholder tokens for placeholders;
- do not use brand or semantic fills for ordinary input backgrounds.

Input validation should combine copy, border/icon treatment, and semantic tokens. Error/warning colors should identify the issue without taking over the whole form.

## 6. View-Level Color Assignment

Desktop views should share one visual grammar rather than invent per-view themes.

| Surface | Main visual color | Emphasis color | Notes |
|---------|-------------------|----------------|-------|
| Conversation | neutral surfaces | neutral inversion for send/primary actions; small semantic/tool status colors | Agent output, tool cards, and approvals may use semantic accents, but the conversation column stays neutral. |
| Automations | neutral catalog/list surfaces | neutral inverted `New Task` action; semantic status badges | Automation state may use status colors, not a page theme. |
| Skills / Plugins / Catalogs | neutral cards and rows | neutral primary management actions; small icon/provider colors | Marketplace/provider identity colors stay inside icons or badges. |
| Settings | neutral grouped rows | subtle selected navigation and focus states | Settings should feel utilitarian and calm. |
| Channels | neutral cards/forms | small channel identity icons; semantic connection state | Platform brand colors are identity accents only. |
| Detail viewers | content-native when needed | viewer controls remain neutral | Rendered files may contain their own colors; viewer chrome should not. |
| Modals and dialogs | neutral elevated surface | one neutral inverted primary action | Close buttons are borderless; secondary actions stay neutral. |
| Setup / onboarding | neutral product surface | restrained brand accent is allowed | This is the main place where brand presentation can be stronger. |
| What's New / release highlights | neutral modal surface | neutral inverted close/continue action; media previews can contain their own colors | The release dialog should not create a temporary page palette. |

## 7. Surface and Card Hierarchy

Use surface depth instead of color variety:

- main content: `--bg-primary`;
- panels, cards, menus, and repeated items: `--bg-secondary`;
- nested, hovered, or active surfaces: `--bg-tertiary` / `--bg-active`;
- borders: `--border-default`, escalating to `--border-active`;
- elevated overlays: existing glass/elevation tokens.

Do not put cards inside decorative cards. Use cards for repeated items, modals, and framed tools. Page sections should remain unframed or use full-width bands with constrained content.

## 8. Typography, Radius, and Density

Desktop should favor compact, readable UI typography:

- normal UI text: 13px tokenized type where possible;
- supporting text: 12px tokenized secondary text;
- card and panel headings: modest weight increase rather than large display type;
- hero-scale type only for true entry surfaces, not compact panels or dialogs.
- UI fonts are system-first and must not require bundled web fonts; `--font-ui`, `--font-body`, and `--font-sans` may switch by document `lang` for CJK locales while preserving the same sizing, weight, and spacing tokens.

Use 8px radius or less for ordinary controls and cards unless an existing component family uses another token. Keep fixed-format controls stable with explicit height, padding, and alignment so text, icons, and loading states do not shift layout.

## 9. Interaction States

Every interactive control needs visible states:

- rest;
- hover when pointer interaction is supported;
- active/pressed when meaningful;
- disabled;
- focus-visible keyboard state.

Focus-visible may use `--accent` because it is an accessibility affordance. Hover and active states should usually remain neutral.

### 9.1 Borderless Selection Rows

Compact selectors, menu items, picker options, sidebar thread rows, and other navigation or selection rows should share the same interaction language:

- rest state is borderless unless the control belongs to a framed toolbar family;
- hover, open, highlighted, and selected states use neutral background elevation (`--sidebar-control-hover`, `--sidebar-control-active`, `--bg-tertiary`, or `--bg-active`) and text emphasis;
- do not add a visible border, inset ring, or outline for ordinary pointer hover, open, or selected states;
- keep `focus-visible` rings for keyboard accessibility;
- reserve borders, rings, or stronger outlines for inputs, drag/drop targets, validation, destructive confirmation, or other states where the boundary itself communicates meaning.

## 10. Agent Workflow for Desktop UI Changes

Before changing Desktop UI styling, agents must:

1. Read this spec and inspect nearby existing components.
2. Search for an existing component or style constant that matches the needed role.
3. Use existing tokens and shared styles before adding local styles.
4. Decide the action hierarchy: primary, secondary, tertiary, icon-only, destructive, or semantic status.
5. Keep the view's main color neutral unless this spec assigns a stronger role.
6. Test or inspect both light and dark themes when changing color, contrast, or control styling.

Agents must not add a new color or visual treatment merely because a control "needs to stand out." First use hierarchy, placement, size, text weight, and the neutral inverted primary style.

## 11. Adding or Changing Tokens

Adding a visual token is appropriate when:

- the role is reusable across multiple Desktop surfaces;
- the role cannot be expressed with existing tokens without ambiguity;
- both light and dark theme values are defined;
- this spec is updated to describe the role.

Do not add one-off tokens for a single button, modal, or feature unless that feature owns a documented visual domain such as charting or media rendering.

## 12. Review Checklist

For every Desktop UI review, verify:

- no new raw colors were introduced without a token or documented exception;
- ordinary primary actions use the neutral inverted style, not brand blue;
- each surface has at most one immediate primary action;
- semantic colors are used only for status/risk;
- feature/provider colors remain small identity accents;
- inputs and pickers remain neutral;
- modal close buttons are borderless transparent icon buttons;
- focus-visible state is present and accessible;
- light and dark themes preserve contrast and visual hierarchy.
