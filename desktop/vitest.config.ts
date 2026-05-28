import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    projects: [
      {
        // Node environment for main-process and pure utility/store tests
        test: {
          name: 'node',
          environment: 'node',
          include: [
            'src/main/**/*.test.ts',
            'src/renderer/tests/*.test.ts',
            'src/shared/**/*.test.ts'
          ],
          globals: true
        }
      },
      {
        // jsdom environment for React component tests
        plugins: [react()],
        test: {
          name: 'browser',
          environment: 'jsdom',
          pool: 'threads',
          include: ['src/renderer/tests/*.test.tsx'],
          globals: true,
          setupFiles: ['src/renderer/tests/setup.ts']
        }
      }
    ]
  }
})
