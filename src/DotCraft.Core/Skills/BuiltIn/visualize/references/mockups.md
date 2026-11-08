# UI mockups

- When the user would benefit from comparing a few directions for the same component or screen, show them in a [variant carousel](#mockup-variants-and-design-controls) without waiting to be asked, and bind the settings worth exploring to [design controls](#design-controls). Keep ordinary mockup interactions local. Do not add design controls to charts, explainers, or simulations unless the user asks, and never build your own controls panel.
- "In the widget" means the in-conversation visualization, not a widget inside the depicted product.
- Use product and platform context already available in the conversation; don't search the project to render a mockup. Match the product's chrome, navigation, typography, colors, and content. If its design is unavailable, infer one from the platform and request.
- NEVER use visualization CSS variables or utility classes inside a mockup (for example, `--card`, `--font-size-base`, `.card`, or `.btn`). Define root-scoped, product-specific colors, typography, surfaces, and controls instead, and reset the host's default `button` styles within the mockup. This rule overrides all general visualization guidance.
- Keep only the surrounding conversation surface transparent. Give product windows, cards, menus, and popovers opaque backgrounds, and stack overlays above the product content.
- Follow the host's active appearance with product-specific `light-dark(<light>, <dark>)` colors unless a fixed theme is requested.
- **Contained mockup:** Frame a component, dialog, small feature, or mobile screen as a compact product surface. Add `.viz-dotted-background` to the surrounding preview area or the carousel root for a quiet, theme-aware dot grid. This preview-only class is an exception to the mockup utility rule; keep it outside the product UI and leave full-page mockups, charts, and explainers plain.
- **Full-page mockup:** Render a desktop window, application shell, or page at full width without an additional visualization card.
- Put app-wide navigation and pickers in the app chrome, and local controls in their component. Omit single-option pickers. Show realistic states, not invented dashboards, filler cards, or oversized icons.

### Mockup variants and design controls

#### Variant carousel

- Show one complete design at a time. Each design must differ in layout or interaction, not only in color. When the user asks to see designs side by side, lay them out side by side instead.
- Use one `.viz-carousel` root with an `aria-label` and one direct child per design. Give every child a unique, short `data-variant` name and an `aria-label`. Leave the first design visible and mark the others `hidden`.
- The host adds previous and next buttons, a picker of the names, and a count below the designs, and switches designs by toggling `hidden`, so each design keeps its DOM and local state. Never write carousel navigation markup, styles, or script.
- Keep the carousel root outside product-scoped styles; it and `.viz-dotted-background` are the only host classes allowed around a mockup. Scope product styles to the elements inside each design, because a rule on an ancestor, such as `#app button`, also restyles the carousel controls. The carousel reserves space below for its controls. Give every design the same responsive stage height, sized for the tallest one, so the controls stay put; at narrow widths raise that height or reflow the product content so nothing clips.
- Name designs by what sets them apart (`Compact`, `Editorial`), never with numbers or ordinals such as `01 · Compact`; the host already shows the count. Never start animation or audio, or send a follow-up, when a design becomes visible.
- For another language, set `data-previous-label` and `data-next-label` on the root; the count and picker follow the names. The visible design is local: it is not saved or sent to the model.

```html
<div class="viz-carousel viz-dotted-background" aria-label="Music player designs">
  <section data-variant="Minimal" aria-label="Minimal music player">
    <!-- A complete interactive player with product-specific styles -->
  </section>
  <section data-variant="Editorial" aria-label="Editorial music player" hidden>
    <!-- A distinct layout with its own controls and state -->
  </section>
  <section data-variant="Studio" aria-label="Studio music player" hidden>
    <!-- A third direction -->
  </section>
</div>
```

#### Design controls

The host provides `Tweak`, a helper that binds properties of your own state objects to controls in a host panel the user opens from the view. It does not draw a panel, so never import another controls library, add a controls launcher, or wire host messages yourself.

Render the initial state first, then register controls only when the helper exists, so the same HTML still works where it is unavailable:

```js
const player = document.getElementById("minimal-player");
const state = { radius: 18, accent: "#7c3aed", playing: true, density: "Comfortable" };

function render() {
  player.style.borderRadius = `${state.radius}px`;
  player.style.setProperty("--player-accent", state.accent);
  player.dataset.density = state.density.toLowerCase();
  player.querySelector(".play").textContent = state.playing ? "Pause" : "Play";
}
render();

if (globalThis.Tweak) {
  const tweak = new Tweak({ container: player, onChange: render });
  tweak.addSlider(state, "radius", { label: "Corner radius", min: 0, max: 40, unit: "px", reference: "player.radius" });
  tweak.addColorPicker(state, "accent", { label: "Accent", reference: "--player-accent" });
  tweak.addToggle(state, "playing", { label: "Playing" });
  tweak.addSelect(state, "density", { label: "Density", options: ["Compact", "Comfortable"] });
}
```

- `new Tweak({ container, onChange })` creates one group, headed by the container's `aria-label`. Use one instance per independently editable component and give each container a descriptive `aria-label`.
- `addSlider(object, property, { min, max, step = 1, unit?, label?, reference? })` binds a number; `unit` is only shown beside the value. `addColorPicker(object, property, { label?, reference? })` binds a hex color string. `addToggle(object, property, { label?, reference? })` binds a boolean. `addSelect(object, property, { options, label?, reference? })` binds a string; options are strings or `{ label, value }` objects. Each method returns the instance.
- The helper assigns a new value to the bound property and then calls `onChange`, including when the user resets or shows the original. Make `onChange` a deterministic local render that updates the registered element in place; never replace that element, write to the network, or take an irreversible action. The property's value at registration is its original.
- Use at most 12 controls per component, 12 options per select, and 16 components per view; extra controls are ignored. A `reference` hint names the token or state property a control changes, such as `--player-accent` or `player.radius`, using only letters, digits, and `_ - . / : @ $ #`.
- Inside a carousel, bind controls to components inside each design. The panel shows the controls of the visible design and keeps edits when the user switches. Controls bound outside the designs are always shown; use them for shared settings and apply them to every design they affect. Never add a control that switches designs.
- The user can add their edits to their next message. You then receive the visualization file, the visible design, and each changed control as `group · label (reference): from → to`; apply them to the file rather than asking the user to repeat them.
- `tweak.supported` is false when the host cannot show the controls; the helper is then inert. Cleanup is automatic; call `tweak.dispose()` only when you remove the component.
