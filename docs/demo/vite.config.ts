import { resolve } from 'path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

const desktopRenderer = resolve(__dirname, '../../desktop/src/renderer')
const avatarSource = resolve(__dirname, '../../sdk/typescript/packages/avatar/src')
const repoRoot = resolve(__dirname, '../..')

export default defineConfig({
  base: './',
  define: {
    __APP_VERSION__: JSON.stringify('web-demo')
  },
  resolve: {
    alias: [
      // Keep desktop-only viewer dependencies out of the web demo.
      { find: /^.*\/detail\/ViewerTab$/, replacement: resolve(__dirname, 'src/stubs/ViewerTab.tsx') },
      { find: '@dotcraft/avatar', replacement: avatarSource },
      { find: '@renderer', replacement: desktopRenderer },
      { find: '@', replacement: desktopRenderer }
    ],
    // Prevent Desktop's node_modules from supplying shared runtime instances.
    dedupe: [
      'react',
      'react-dom',
      'zustand',
      'js-yaml',
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
      'shiki',
      '@shikijs/themes',
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
    outDir: resolve(__dirname, '../public/demo'),
    emptyOutDir: true
  }
})
