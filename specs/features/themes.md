# Themes

| Field | Value |
|---|---|
| Version | 0.12.0 |
| Status | Draft |
| Date | 2026-11-15 |
| Parent | [Desktop Plugins](../architecture/desktop-plugins.md) |

A theme changes how the app looks around the conversation: its colors, what is drawn behind the shell, and how the shell's surfaces sit over it. Themes owns the theme library, where a theme applies, preview and application, and Agent-assisted creation. A theme is data that the Host validates and draws. The bundled Themes extension is the trusted host of that data, and generated theme content never runs with renderer privileges.

## Theme

A theme has a stable id, a revision, a name, and the appearance it is made for, light or dark. It carries at least one of colors and a backdrop, and may carry a tint for the surfaces over its backdrop.

| Part | Content |
|---|---|
| Colors | A surface, ink, and accent color and a 0–100 contrast, from which the Host derives the app palette. |
| Backdrop | One of: an animated scene, an image, a gradient, or a flat color. |
| Glass | How shell surfaces cover the backdrop: a blur radius from 0 to 64, an optional tint color, and an opacity from 0 to 1. |
| Pair | The id of the same theme made for the other appearance. |

A scene is self-contained SVG or HTML source with a static SVG poster. An image is a JPEG, PNG, or WebP stored with the theme, laid out as cover, contain, or tile. A gradient has two to sixteen color stops, an angle, and a linear or radial shape. Colors in a gradient or flat backdrop are raw colors.

A theme without a backdrop recolors the app and leaves the ordinary background in place. A theme without colors keeps the user's Appearance colors. A tint applies only when the theme has a backdrop. When it is absent the surfaces keep the app's base color.

A theme never changes the appearance mode. The user's Appearance setting decides light or dark, and a theme made for the other appearance is not drawn.

Saving rejects a theme whose ink does not reach 4.5:1 contrast against its surface, because the Host would otherwise paint unreadable text across the whole app. Backdrop and glass colors do not weaken this guarantee: the Host keeps text readable over any backdrop as described in [Desktop Plugins](../architecture/desktop-plugins.md).

## Library

The app includes example themes: color-only themes, still backdrops, and animated scenes, each with a pair where the idea works in both appearances. User themes are stored on this device outside conversation history and remain available after their authoring thread is deleted. An imported image becomes a user theme with an image backdrop.

Saving a revision replaces the content under the same id. A replacement requires the current revision so that concurrent edits are not overwritten. Bundled themes cannot be overwritten or deleted. The library holds at most 100 user themes. A theme holds at most 1 MiB of scene source, 256 KiB of poster, and 8 MiB of image, and the library at most 64 MiB of serialized user content. Library writes are atomic and serialized. Invalid content and failed writes leave the previous library intact.

The user can apply, edit, export, import, and delete their themes. Deleting a theme removes it from every choice that names it.

## Scopes

A choice names a theme for the light appearance and a theme for the dark appearance. Either slot can instead be **No theme**, or unset. Choices exist at two scopes:

| Scope | Applies to |
|---|---|
| Everywhere | Every chat on this device. |
| Chat | One thread. |

For the current appearance, the app draws the chat's slot when it is set and otherwise the everywhere slot. **No theme** in a chat draws the ordinary app there, which lets one chat opt out of the everywhere theme. Clearing a chat's choice unsets both slots.

Each window resolves its own foreground thread, so two windows can show different themes. A view with no thread, such as a new chat, shows the everywhere choice. Themes has no per-workspace scope: a look that belongs to a whole workspace is rare, and every extra scope adds a decision to each change.

Applying a theme fills the slot of the appearance it is made for. When the theme has a pair, the pair fills the other slot of the same scope, so switching appearance keeps the look. Blur and surface opacity are the user's own settings, kept across themes and scopes. Both start at their minimum, no blur and the Host's 30% floor, so a new backdrop shows as much as stays readable.

Choices are stored on this device, like the library they refer to, and are never written to a workspace's shared configuration. A choice whose theme is missing is treated as unset. The device keeps the chat choices of the 200 threads that chose most recently. Turning Themes off draws the ordinary app in every scope and keeps the choices.

## Rendering and motion

Backdrops do not receive focus or pointer input. Scene SVG and HTML render in sandboxed frames without same-origin access, preload access, navigation privileges, or a bridge to conversations and tools. The resource policy allows inline scene code and embedded resources but denies network requests, external resources, nested frames, forms, objects, workers, and base URL overrides. Generated content cannot modify the Host's theme directly. The Host validates and applies the theme's declarative colors and glass.

A theme's colors and glass apply only while that theme is drawn, and a scene counts as drawn once its poster or its running frame is visible. They are released on replacement, disablement, extension disposal, and when neither the scene nor its poster can be drawn.

The static poster is displayed while motion is disabled or the document is hidden. Scenes keep animating without GPU compositing, where only the glass falls back. Resuming motion mounts a fresh scene rather than accumulating animation loops. Runtime errors and startup timeouts return to the poster. The user can disable scene animation independently of the system's reduced-motion preference. System reduced motion always wins. Sandbox isolation limits authority, not a hostile script's CPU or GPU consumption.

## Preview and application

Selecting a theme applies it at once, with no separate confirmation. Themes can be chosen in two places. The Themes settings page manages the library, and selecting a theme there applies it everywhere. Each theme's own actions (edit, export, image layout, and delete) open from a menu on its tile, so they are available without applying the theme. The **Theme** command opens a picker beside the Composer that lists themes for the current appearance and closes once one is selected. It applies to the current chat, and the picker also offers **Default**, which clears the chat's choice. Before the Composer is attached to a thread, the command applies everywhere instead. Both places offer **No theme**.

An Agent preview is the only temporary look. It shows the saved theme in the foreground authoring chat and opens the picker with that theme marked. Selecting it applies it like any other theme. Closing the picker, switching thread, or turning Themes off ends the preview and restores the resolved choice.

## Agent creation

Create opens a new chat whose Composer holds an unsent request that mentions the theme authoring Skill. Edit continues the authoring conversation when available, otherwise opens the same kind of draft naming the theme. Nothing reaches the Agent until the user sends it. The Agent writes the theme, supplies a composed static poster for a scene, and saves it through the Desktop Theme tool. A save never changes a choice. The tool can list and read themes, save a theme, and request a preview for the calling thread in its owning Desktop window. Agent previews from a background thread do not replace the foreground look. Only the user's Apply action stores a choice.

The tool returns actionable validation errors and revision conflicts. The Skill explains the theme parts, the sandbox restrictions, the contrast rule, motion fallback, and the save and preview flow. It prefers a color-only theme or a still backdrop when the request needs no motion, animated SVG for scenes that do not need scripting, and self-contained HTML for Canvas and other scripted animation. Iteration reads the current theme, keeps its identity, and saves with the current revision. Library operations target this device even when the authoring conversation uses a remote workspace.
