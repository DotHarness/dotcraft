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
