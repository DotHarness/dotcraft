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

Query parameters: `?theme=dark|light` and `?lang=en|zh`.

## Homepage embed

`docs/.vitepress/theme/home/DemoWindow.vue` mounts the built demo into the
homepage hero: poster first, the iframe fades in once the demo has rendered,
pointer input only after explicit activation.
The Pages workflow builds this project before the VitePress site; a demo build
failure fails the deploy.

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
with the `<LiveExample>` component in `docs/.vitepress/theme/components/`.
