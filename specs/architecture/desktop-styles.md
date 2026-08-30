# Desktop Style Architecture

| Field | Value |
| --- | --- |
| Version | 1.1 |
| Status | Accepted |
| Date | 2026-08-09 |
| Parent Spec | `specs/architecture/DESIGN.md` |

## Overview

DotCraft Desktop uses plain CSS as the production styling language. The style
system separates foundations, shared primitives, and feature-owned rules while
preserving one deterministic global cascade for existing global selectors.

## Goal

Keep Desktop styling understandable, locally owned, and safe to evolve without
changing rendered output merely because source files are reorganized.

## Scope

- Renderer design tokens, themes, document defaults, accessibility, and motion.
- Shared UI primitive styles.
- Feature-owned global styles and locally scoped CSS Modules.
- Production and design-system style entry points.
- Source-size and visual-equivalence expectations for style refactors.

## Non-goals

- Replacing plain CSS with a preprocessor or CSS-in-JS runtime.
- Converting existing global selectors to CSS Modules as part of source moves.
- Changing product visuals, DOM structure, interaction behavior, or animation
  timing during an organizational refactor.
- Introducing cascade layers without a separately reviewed cascade migration.

## Core design and architecture

The renderer has one global style entry imported once by the renderer bootstrap.
That entry is an ordered import manifest; it contains no style declarations.
Its import order is a compatibility contract for existing global selectors.

Global styles are divided by ownership:

- Foundations define Tailwind theme values, product custom properties, themes,
  typography context, and document defaults.
- Primitives define shared controls and reusable interaction patterns.
- Shared concerns define cross-feature accessibility, motion, status, and
  appearance behavior.
- Feature styles live beside the feature that owns their selectors, animations,
  and responsive behavior. They may still be included by the ordered global
  manifest when stable cascade order is required.

Existing class names remain global during organizational refactors. New isolated
leaf components may use CSS Modules when their selectors do not target portals,
shared component classes, third-party DOM, or ancestor-owned state. Global
feature selectors use an ownership prefix. Shared production primitives use the
`dc-` prefix.

Static presentation belongs in CSS. Inline React styles are reserved for values
that genuinely depend on runtime data, including coordinates, measured sizes,
progress, and CSS custom-property inputs.

Persistent vertical scroll regions reserve space for classic scrollbars with
the shared `dc-scrollbar-stable` primitive. This applies to page bodies and
long-lived sidebars, drawers, rails, and dynamic lists whose content can cross
the overflow boundary while the surrounding layout remains mounted. Centered
conversation surfaces may use `scrollbar-gutter: stable both-edges` when their
own layout requires symmetric space.

Do not apply stable gutters globally. Menus, popovers, comboboxes, short-lived
dialogs, code blocks, horizontal-only scrollers, and navigation rails that hide
their scrollbar keep their feature-owned overflow behavior. The document root
also remains non-scrolling; nested scroll regions opt in to the primitive.

Scrollbar geometry is global and token-driven. Foundations own the size, inset,
radius, and the resting/hover/active thumb colors; features own only whether a
region scrolls and whether it reserves a gutter. The reserved gutter follows
`--scrollbar-size`, so changing the size reflows every `dc-scrollbar-stable`
consumer by design. That is the gutter staying honest about the bar, not a
regression to be compensated for locally.

Features do not set `scrollbar-width`. Current Chromium gives that property
precedence over the `::-webkit-scrollbar` pseudo-elements, so an element
declaring it drops the shared geometry and its own webkit thumb rules become
dead code. The one sanctioned use is `none`, to hide a scrollbar deliberately.

## Design tokens

Color is derived rather than enumerated. Three layers own it, and each reads only
the layer above it.

| Layer | Where | Written by |
| --- | --- | --- |
| Seed | inline on the document element | `shared/themeDerive.ts` |
| Semantic | `foundations/tokens.css`, `foundations/themes.css` | authored CSS, `color-mix` off the seed |
| Component | the same two files | authored CSS, resolving from the semantic layer |

The seed is four values per variant — `--seed-surface`, `--seed-ink`,
`--seed-accent`, and a 0-100 `--seed-contrast` — plus `--contrast-k`, the
normalized multiplier the ramps read. `surface` is the base plane: the page in
dark, the card in light, so both variants move away from it the same way, by
mixing in ink. Every ramp percentage is `base% + var(--contrast-k) * slope%`,
with each base solved so the authored value reproduces at that variant's default
contrast.

`themeDerive.ts` writes only what CSS cannot compute: the three seed colors, the
contrast multiplier, and `--on-accent`, whose choice needs relative luminance. A
field left at its variant default is removed rather than restated, so an
uncustomized app resolves entirely from the stylesheet. The ink is not a separate
control; it follows the surface, so a chosen background cannot leave text
unreadable.

Three rules keep the layers honest:

- The color axes are closed. Surfaces are `--bg-*`, text is `--text-*`, borders
  are `--border-*`. A new color belongs on an existing axis or is not a token.
- A component token resolves from the semantic layer, never from a literal. A
  literal there is a value no seed can reach, which is what held the palette
  fixed before.
- Component qualifiers name first-class surfaces only, such as `--composer-*`,
  `--sidebar-*`, `--shell-*`, `--main-surface-*`, and `--glass-*`. A one-off does
  not earn a prefix.

Some families stay literal on purpose. The sixteen `--ansi-*` are a protocol
palette, `--code-block-bg` matches the bundled syntax theme's own background, and
the status hues along with `--brand-blue-*` and `--find-match` are identities
rather than derivations. `--bg-inverse` and `--text-on-inverse` are the opposite
variant's tones, which one variant's seed cannot state.

`shared/titleBarOverlay.ts` applies the same chrome mix to the native caption bar
and the pre-paint window background, so the seed reaches outside the renderer.

The subset a plugin may read is published in
`docs/developing/integrations/desktop-plugin-api.md`. Every other custom property
moves with Desktop's own layout work.

## Workflow and lifecycle

Style-only source moves preserve selectors, declarations, at-rules, animation
names, and their effective order. Formatting, deduplication, selector cleanup,
and visual changes are performed separately after equivalence is established.

The production application and maintained design system consume the same global
entry and canonical token source. The design system must not copy production
rules into a parallel stylesheet.

## Constraints and compatibility

- The Tailwind import and top-level `@theme` definitions remain valid for the
  configured Tailwind Vite plugin.
- A normal hand-written CSS file targets fewer than 500 formatted lines. A file
  at or above 800 formatted lines must be split when it receives a non-trivial
  change. Import-only manifests, generated output, and third-party styles are
  exempt.
- Organizational refactors do not introduce `@layer`; doing so changes cascade
  precedence and requires an explicit compatibility review.
- Global focus, reduced-motion, pointer, theme, and locale behavior remain
  authoritative across every feature.
- Source organization must not depend on renderer route load order.

## Acceptance checklist

- The renderer imports one global style entry.
- Canonical tokens, themes, primitives, and feature rules have distinct owners.
- Scroll regions inherit the shared scrollbar geometry; no feature sets
  `scrollbar-width` except to hide a scrollbar deliberately.
- No ordinary hand-written CSS file remains at or above the refactoring trigger.
- Production and design-system builds succeed from the same production sources.
- Existing automated tests pass.
- Generated production CSS is semantically equivalent across source-only moves.
- Representative production-mounted surfaces show no visual regression at the
  agreed themes, locales, viewport widths, and motion settings.

## Open questions

None.
