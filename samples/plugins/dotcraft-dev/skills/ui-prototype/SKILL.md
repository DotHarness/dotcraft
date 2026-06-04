---
name: ui-prototype
description: Create standalone interactive prototypes for DotCraft Desktop UI changes before touching production code, using static HTML for loose visual exploration and TSX previews for Desktop-coupled surfaces. Use when exploring DotCraft desktop layout, visual states, badges, controls, responsive widths, interaction flows, or design references that should live outside the source repository and be reviewed in the in-app browser before implementation.
---

# DotCraft UI Prototype

Use this skill to separate visual/product design iteration from production Desktop changes.

## Repository Setup

Work with two repositories:

- DotCraft source repository: the current `dotcraft` checkout.
- DotCraft design repository: by default, a sibling directory named `dotcraft-design` next to the source checkout.

If the design repository does not exist, ask the user before creating it, or ask them to provide the prototype/design artifact directory. Do not hardcode absolute local paths in the skill, artifacts, or instructions.

## Workflow

1. Confirm the design question and scope.
   - Identify the UI surface, states, constraints, and what must stay unchanged.
   - Keep production wording, colors, or behavior stable unless the user explicitly wants to explore those dimensions.

2. Choose the preview fidelity.
   - Prefer a single `.html` file with embedded CSS and minimal JavaScript for early visual exploration, copy/layout alternatives, and designs that do not depend on Desktop runtime behavior.
   - Use a TSX preview in the design repository when the result is tightly coupled to Desktop implementation details, such as real React components, renderer stores, tokens, icons, locale providers, truncation behavior, hover/focus state, or measured grid/flex layout.
   - Keep existing HTML references as design notes and comparison material when useful, but do not treat them as the final pixel source for Desktop-coupled behavior once a TSX preview exists.

3. Create or update a standalone artifact in the design repository.
   - For HTML prototypes, prefer a single `.html` file with embedded CSS and minimal JavaScript.
   - Group artifacts by product area when useful, such as `desktop/thread-list/...`.
   - Do not depend on the DotCraft Desktop build, renderer stores, production components, or network assets for ordinary HTML prototypes.
   - For TSX previews, use the design repository as a thin host that imports the real Desktop source from the sibling DotCraft checkout, rather than copying production components into the design repository.

4. Model realistic states and edge cases.
   - Include narrow and normal widths, long labels, empty/loading/error/pending states, selected and inactive rows, hover/focus where relevant, and controls for the variables under discussion.
   - Use side-by-side comparison when evaluating current versus candidate layouts.
   - Make controls interactive enough for review in the in-app browser.

5. Review with the in-app browser.
   - Open static HTML artifacts from the design repository with a local `file://` URL.
   - Open TSX previews through the local Vite dev server for that preview entry.
   - Iterate on the prototype artifact until the user confirms the visual direction.
   - Treat screenshots, DOM measurements, and browser observations as design feedback. For TSX previews, also use them to reduce implementation drift before editing the DotCraft source repository.

6. Handoff before production implementation.
   - Summarize the approved layout decisions, interaction behavior, tokens, and unresolved risks.
   - Only edit the DotCraft source repository after the user explicitly approves implementing the chosen design.
   - Before production Desktop visual edits, read `specs/clients/DESIGN.md` from the DotCraft source repository and follow the repository's normal development/test workflow.

## TSX Desktop Preview

Use this path when a static HTML reference is likely to drift from Desktop behavior. Good triggers include:

- the UI depends on an existing Desktop React component or renderer store
- the issue involves exact text measurement, truncation, grid/flex tracks, hover/focus actions, badges, icons, or status slots
- the visual defect appears only after production state changes, such as loading, running, confirmation, unread, pinned, or active-thread states
- prior HTML prototypes and Desktop implementation have already diverged

Recommended setup:

- Add a design-only React/Vite entry in the design repository under the relevant product area, for example `desktop/thread-list/tsx-preview/`.
- Configure the preview to import Desktop source from the sibling DotCraft checkout by default, with an environment variable such as `DOTCRAFT_ROOT` for override.
- Alias renderer imports to the Desktop renderer source and dedupe shared runtime dependencies such as `react`, `react-dom`, `zustand`, and icon libraries.
- Import the real Desktop component, tokens, icons, providers, stores, and locale setup needed by the surface.
- Mock only the minimum `window.api` surface required for the component to render and respond to review interactions.
- Seed realistic store state directly in the preview and expose small controls for width, state, and interaction mode.
- Keep the preview self-contained in the design repository. The DotCraft source repository should be read-only until the user approves the production change.

Validation expectations:

- Run the preview build script, such as `npm run build:<surface>`, before using the TSX preview as implementation evidence.
- Use the in-app browser to verify the page is non-empty, has no framework overlay, and has no relevant console errors.
- Measure the actual DOM geometry when the issue is spacing, alignment, or layout shift. Prefer concrete checks such as grid tracks, bounding boxes, and gaps over visual impressions alone.
- Capture screenshots of the reviewed states, especially default versus hover/focus/confirm states or narrow versus normal widths.
- After production implementation, compare Desktop and TSX preview behavior and add targeted tests only where they protect a real regression.

## Artifact Standards

- Keep prototypes clearly non-production: mock data, local state, and explanatory labels are allowed.
- Preserve user-visible strings from production only when they are part of the design question.
- Keep dimensions and constraints explicit, especially status slots, truncation, alignment, and responsive width behavior.
- Prefer CSS variables for candidate tokens so color, spacing, and sizing can be tuned quickly.
- Avoid decorative UI that is not relevant to the design decision.
- Include enough inline notes in the page for reviewers to understand what is being compared without reading production code.

## Handoff Notes

When the prototype is approved, report:

- the design artifact path
- whether the reviewed source of truth was static HTML or TSX importing Desktop source
- the confirmed UI states and constraints
- the browser/build evidence used for review, including screenshots or DOM measurements when relevant
- the implementation surface in the DotCraft source repository
- any choices that still need product confirmation
