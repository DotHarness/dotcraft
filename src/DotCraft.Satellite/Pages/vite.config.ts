import { resolve } from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'

const packages = resolve(__dirname, '../../../sdk/typescript/packages')
const escape = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

/** WebView2 receives each page as one string, so the script and styles are folded into the HTML. */
function singleFile(): Plugin {
  return {
    name: 'satellite-single-file',
    enforce: 'post',
    generateBundle(_, bundle) {
      const page = Object.values(bundle).find((file) => file.type === 'asset' && file.fileName.endsWith('.html'))
      if (!page || page.type !== 'asset') return
      let html = String(page.source)
      for (const [name, file] of Object.entries(bundle)) {
        if (file.type === 'chunk') {
          html = html.replace(new RegExp(`<script[^>]*src="[^"]*${escape(file.fileName)}"[^>]*></script>`),
            () => `<script type="module">${file.code.replace(/<\/script/gi, '<\\/script')}</script>`)
          delete bundle[name]
        } else if (file.fileName.endsWith('.css')) {
          html = html.replace(new RegExp(`<link[^>]*href="[^"]*${escape(file.fileName)}"[^>]*>`), () => `<style>${String(file.source)}</style>`)
          delete bundle[name]
        }
      }
      page.source = html
    },
  }
}

export default defineConfig(({ mode }) => ({
  root: resolve(__dirname, 'src', mode),
  base: './',
  plugins: [react(), singleFile()],
  resolve: {
    alias: [
      { find: /^@dotcraft\/avatar\/react$/, replacement: resolve(packages, 'avatar/src/react.ts') },
      { find: /^@dotcraft\/avatar\/styles\.css$/, replacement: resolve(packages, 'avatar/src/styles.css') },
      { find: /^@dotcraft\/avatar$/, replacement: resolve(packages, 'avatar/src/index.ts') },
      { find: /^@dotcraft\/island\/react$/, replacement: resolve(packages, 'island/src/react.ts') },
      { find: /^@dotcraft\/island\/styles\.css$/, replacement: resolve(packages, 'island/src/styles.css') },
      { find: /^@dotcraft\/island$/, replacement: resolve(packages, 'island/src/index.ts') },
    ],
    dedupe: ['react', 'react-dom'],
  },
  build: {
    outDir: resolve(__dirname, 'dist'),
    emptyOutDir: false,
    assetsInlineLimit: Number.MAX_SAFE_INTEGER,
    cssCodeSplit: false,
    modulePreload: false,
    rollupOptions: { input: resolve(__dirname, 'src', mode, `${mode}.html`), output: { inlineDynamicImports: true } },
  },
}))
