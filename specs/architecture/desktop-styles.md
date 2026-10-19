# Desktop Style Architecture

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Desktop Design System](DESIGN.md) |

## Overview

DotCraft Desktop uses plain CSS as the production styling language. The style
system separates foundations, shared primitives, and feature-owned rules while
preserving one deterministic global cascade for existing global selectors.

## Core design and architecture

The renderer has one global style entry imported once at renderer startup. That
entry is an ordered import manifest with no declarations of its own; its import
order is a compatibility contract for existing global selectors.

Global styles are divided by ownership:

- Foundations define Tailwind theme values, product custom properties, themes,
  typography context, and document defaults.
- Primitives define shared controls and reusable interaction patterns.
- Shared concerns define cross-feature accessibility, motion, status, and
  appearance behavior.
- Feature styles live beside the feature that owns their selectors, animations,
  and responsive behavior. The global manifest may still include them when stable
  cascade order is required.

Existing class names stay global during organizational refactors. New isolated
leaf components may use CSS Modules when their selectors do not target portals,
shared component classes, third-party DOM, or ancestor-owned state. Global feature
selectors use an ownership prefix; shared production primitives use the `dc-`
prefix.

Static presentation belongs in CSS. Inline styles are reserved for values that
depend on runtime data: coordinates, measured sizes, progress, and custom-property
inputs.

Scrollbar geometry is global and token-driven (see the Scrollbars rules in
[DESIGN.md](DESIGN.md#scrollbars)). Foundations own the size, inset, radius, and
thumb colors; features own only whether a region scrolls and whether it reserves a
gutter. Persistent vertical scroll regions (page bodies and long-lived sidebars,
drawers, rails, and dynamic lists) reserve the gutter through the shared stable-
gutter primitive, which follows `--scrollbar-size`. Centered conversation surfaces
may reserve both edges when their layout needs symmetric space. Stable gutters are
never applied globally: menus, popovers, comboboxes, short-lived dialogs, code
blocks, horizontal-only scrollers, and rails that hide their scrollbar keep their
own overflow behavior, and the document root does not scroll.

## Design tokens

Color is derived rather than enumerated. Three layers own it, and each reads only
the layer above it.

| Layer | Where | Written by |
| --- | --- | --- |
| Seed | inline on the document element | runtime theme derivation |
| Semantic | the token and theme stylesheets | authored CSS, `color-mix` off the seed |
| Component | the same stylesheets | authored CSS, resolving from the semantic layer |

The seed is four values per variant (`--seed-surface`, `--seed-ink`,
`--seed-accent`, and a 0-100 `--seed-contrast`) plus `--contrast-k`, the
normalized multiplier the ramps read. `surface` is the base plane: the page in
dark, the card in light, so both variants move away from it by mixing in ink.
Every ramp percentage is `base% + var(--contrast-k) * slope%`, with each base
solved so the authored value reproduces at the variant's default contrast.

Runtime derivation writes only what CSS cannot compute: the three seed colors, the
contrast multiplier, and `--on-accent`, which needs relative luminance. A field at
its variant default is not written, so an uncustomized app resolves entirely from
the stylesheet. Ink follows the surface rather than being a separate control, so a
chosen background cannot leave text unreadable.

Three rules keep the layers honest:

- The color axes are closed: surfaces are `--bg-*`, text is `--text-*`, borders
  are `--border-*`. A new color belongs on an existing axis or is not a token.
- A component token resolves from the semantic layer, never from a literal.
- Component qualifiers name first-class surfaces only, such as `--composer-*`,
  `--sidebar-*`, `--shell-*`, `--main-surface-*`, and `--glass-*`.

Some families stay literal on purpose. The sixteen `--ansi-*` are a protocol
palette, `--code-block-bg` matches the bundled syntax theme's background, and the
status hues, `--brand-blue-*`, and `--find-match` are identities rather than
derivations. `--bg-inverse` and `--text-on-inverse` are the opposite variant's
tones, which one variant's seed cannot state.

The native caption bar and pre-paint window background use the same chrome mix as
the renderer.

Only explicitly published theme tokens are compatibility contracts; other custom
properties are internal.

## Workflow and lifecycle

Style-only source moves preserve selectors, declarations, at-rules, animation
names, and their effective order. Formatting, deduplication, selector cleanup, and
visual changes happen separately after equivalence is established.

The production application and the maintained design system consume the same
global entry and token source; the design system never copies production rules
into a parallel stylesheet.

## Constraints and compatibility

- The Tailwind import and top-level `@theme` definitions stay valid for the
  configured Tailwind build.
- A hand-written CSS file targets fewer than 500 formatted lines; one at or above
  800 lines is split when it receives a non-trivial change. Import-only manifests,
  generated output, and third-party styles are exempt.
- Organizational refactors do not introduce `@layer`; doing so changes cascade
  precedence and requires an explicit compatibility review.
- Global focus, reduced-motion, pointer, theme, and locale behavior remain
  authoritative across every feature.
- Source organization does not depend on renderer route load order.
