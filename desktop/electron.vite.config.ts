import { resolve } from 'path'
import { defineConfig, externalizeDepsPlugin } from 'electron-vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import pkg from './package.json'

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin({ exclude: ['@dotcraft/sdk'] })],
    build: {
      rollupOptions: {
        input: {
          index: resolve('src/main/index.ts'),
          voiceWorker: resolve('src/main/voice/voiceWorkerProcess.ts')
        }
      }
    }
  },
  preload: {
    plugins: [externalizeDepsPlugin({ exclude: ['@dotcraft/sdk'] })]
  },
  renderer: {
    define: {
      __APP_VERSION__: JSON.stringify(pkg.version)
    },
    resolve: {
      alias: {
        '@renderer': resolve('src/renderer'),
        '@': resolve('src/renderer')
      }
    },
    build: {
      rollupOptions: {
        input: {
          index: resolve('src/renderer/index.html')
        }
      }
    },
    // The highlight worker lazily imports one grammar module per language,
    // so its bundle is code-split. IIFE workers cannot be, and are the default.
    worker: {
      format: 'es'
    },
    plugins: [react(), tailwindcss()]
  }
})
