import { spawnSync } from 'node:child_process'
import { rmSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const output = resolve(root, '..', 'docs', 'public', 'phone-demo')
const site = `/${(process.env.VITEPRESS_BASE ?? '/').replace(/^\/+|\/+$/g, '')}/`.replace(/^\/\/$/, '/')
const baseUrl = `${site}phone-demo`

rmSync(output, { recursive: true, force: true })
const result = spawnSync('npx', ['expo', 'export', '--platform', 'web', '--output-dir', output], {
  cwd: root,
  stdio: 'inherit',
  shell: process.platform === 'win32',
  env: { ...process.env, DOTCRAFT_WEB_BASE_URL: baseUrl },
})
process.exit(result.status ?? 1)
