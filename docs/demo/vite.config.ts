import { resolve } from 'path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const desktopRenderer = resolve(__dirname, '../../desktop/src/renderer')
const repoRoot = resolve(__dirname, '../..')

export default defineConfig({
  // Relative base so the bundle works under any docs subpath (VITEPRESS_BASE).
  base: './',
  define: {
    __APP_VERSION__: JSON.stringify('web-demo')
  },
  resolve: {
    alias: [
      // The Monaco/xterm viewer subtree is lazy-loaded by DetailPanel and never
      // opened in the demo; stub it out so those heavy deps stay uninstalled.
      { find: /^.*\/detail\/ViewerTab$/, replacement: resolve(__dirname, 'src/stubs/ViewerTab.tsx') },
      { find: '@renderer', replacement: desktopRenderer },
      { find: '@', replacement: desktopRenderer }
    ],
    // Desktop has its own node_modules (possibly stale or partially installed);
    // force every shared runtime dependency to resolve from this project.
    dedupe: [
      'react',
      'react-dom',
      'zustand',
      'highlight.js',
      'lucide-react',
      '@iconify/react',
      '@iconify-json/vscode-icons',
      '@modelcontextprotocol/ext-apps',
      '@modelcontextprotocol/sdk',
      'zod',
      'react-markdown',
      'remark-gfm',
      'rehype-highlight',
      'diff',
      'dompurify',
      'mermaid',
      '@dnd-kit/core',
      '@dnd-kit/sortable',
      '@dnd-kit/utilities'
    ]
  },
  plugins: [react(), tailwindcss()],
  server: {
    fs: {
      allow: [repoRoot]
    }
  },
  build: {
    // Emitted into the docs site's public assets so `vitepress build` ships
    // the demo at <base>/demo/. Generated output is gitignored.
    outDir: resolve(__dirname, '../public/demo'),
    emptyOutDir: true
  }
})
