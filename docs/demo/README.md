# DotCraft Desktop Web Demo

Browser-runnable simulator of DotCraft Desktop for the docs website.

It imports the real Desktop renderer components from `desktop/src/renderer`
via Vite path aliases, mocks the Electron preload bridge (`src/mockApi.ts`),
and seeds the production Zustand stores with canned wire-format threads
(`src/data/demoThreads.ts`). Visual fidelity is structural: when Desktop UI
changes, the demo follows on rebuild — and breaks loudly in CI if reuse drifts.

## Commands

```bash
npm install
npm run dev       # local dev server
npm run build     # emits static bundle into docs/public/demo (gitignored)
```

Query parameters: `?theme=dark|light`, `?lang=en|zh`, and
`?start=review-changes|several-projects|token-activity`, which opens the demo
in the state a documentation page illustrates (seed data in `src/data/`).
Without `start`, the demo opens the way the homepage shows it.

## Homepage embed

`docs/.vitepress/theme/home/DemoWindow.vue` mounts the built demo into the
homepage hero: poster first, the iframe fades in once the demo has rendered,
pointer input only after explicit activation.
The Pages workflow builds this project before the VitePress site; a demo build
failure fails the deploy.

Documentation pages embed the same page with `<LiveExample feature="desktop">`.
Embedded, it posts `dotcraft-showcase:ready` to its parent once the main view
has rendered.

## Live examples

`showcase.html` renders one compact example for documentation pages: a user
prompt and the assistant reply through the production message pipeline
(Generative UI blocks and visualization references), or the production Agent
Builder. `src/showcase/sandboxProxy.ts` serves the sandbox relay that Desktop
loads from its Electron-only scheme, and scripted AppServer handlers answer the
methods each example uses. Actions that would leave the example (sending a
message, filling the composer, saving an agent) show an inline note instead.

Query parameters: `feature=generative-ui|visualization|agent-builder`,
`example`, `mode=plain|interactive`, `theme`, and `lang`. The page posts
`dotcraft-showcase:height` to its parent, and `dotcraft-showcase:restart` when
the Agent Builder example starts over. It accepts `dotcraft-demo:set-theme` and
`dotcraft-showcase:show` (`example`, `mode`). Documentation pages embed it
with the `<LiveExample>` component in `docs/.vitepress/theme/components/`;
`liveExample.ts` there registers each feature's page, sizing, and frame.

## Scripted replays

An example that plays a scripted sequence uses `Timeline` from
`src/showcase/playback.ts`. The timeline posts `dotcraft-showcase:playback`
(`state: playing|paused|ended`, `progress` from 0 to 1), and the docs page
answers with `dotcraft-showcase:control` (`action: play|pause|restart`). The
page shows play, pause, and replay controls once the first report arrives,
starts each fresh timeline when the example is on screen, pauses it off screen,
and never autoplays for readers who prefer reduced motion.

`<LiveTour>` walks through one feature as the reader scrolls: the example stays
pinned while step cards switch, and each step opens the example at a named
point with the `chapter` query parameter or the `chapter` field of
`dotcraft-showcase:show`. The Computer Use example supports `approval`,
`operate`, `stopped`, and `confirm`.

Another app embedded the same way, such as the phone demo, implements these
messages itself. A fixed-size page posts `dotcraft-showcase:ready` once it has
rendered instead of reporting its height.

## Phone demo

The `phone` live example embeds the real phone app (`mobile/`) running its
built-in demo runtime in the browser. Build the TypeScript SDK packages first
(`npm ci && npm run build && npm run build --workspace @dotcraft/avatar` in
`sdk/typescript`), then export it:

```bash
cd mobile
npm ci
npm run export:docs   # emits docs/public/phone-demo (gitignored)
```

The export bakes in its base path, `<VITEPRESS_BASE>phone-demo`, so set
`VITEPRESS_BASE` to the same value as the site build when it isn't `/`.
The page reads `demo` (the scenario), `theme`, and `lang`, follows
`dotcraft-demo:set-theme`, and posts `dotcraft-showcase:ready` once the first
screen is up (`mobile/src/demo/docsEmbed.ts`). The Pages workflow exports it
before the VitePress build.
