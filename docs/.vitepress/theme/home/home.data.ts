import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'
import { createMarkdownRenderer, defineLoader, type SiteConfig } from 'vitepress'
import { lucideParts, simpleIconParts, type IconParts } from '../icons'
import type * as Avatars from './avatars'

export type Pose = 'idle' | 'greeting' | 'done'

export interface HomeData {
  mascots: Record<'hero' | 'close', Record<Pose, string>>
  agents: string[]
  icons: Record<string, IconParts>
  program: string
}

declare const data: HomeData
export { data }

const here = dirname(fileURLToPath(import.meta.url))
const repo = resolve(here, '../../../..')
const docs = resolve(repo, 'docs')
const harnessPage = resolve(docs, 'developing/harness/index.md')

const lucide = [
  'app-window', 'arrow-right', 'blocks', 'bot', 'check', 'chevron-down', 'chevron-right', 'circuit-board', 'copy', 'cpu',
  'download', 'external-link', 'file-code', 'layers', 'layout-dashboard', 'monitor', 'mouse-pointer-2', 'plug-zap',
  'puzzle', 'server', 'sparkles', 'terminal'
]
const simple = ['android', 'apple', 'dotnet', 'github', 'linux', 'npm', 'nuget', 'windows']

async function loadAvatars(): Promise<typeof Avatars> {
  const result = await build({
    entryPoints: [resolve(here, 'avatars.tsx')],
    bundle: true,
    write: false,
    platform: 'node',
    format: 'cjs',
    jsx: 'automatic',
    external: ['react', 'react-dom', 'react/*', 'react-dom/*'],
    logLevel: 'warning'
  })
  const module = { exports: {} as typeof Avatars }
  new Function('module', 'exports', 'require', result.outputFiles[0].text)(
    module,
    module.exports,
    createRequire(resolve(docs, 'package.json'))
  )
  return module.exports
}

async function highlightProgram(): Promise<string> {
  const source = readFileSync(harnessPage, 'utf8').replace(/\r\n/g, '\n')
  const fence = /```csharp\n([\s\S]*?)```/.exec(source)
  if (!fence) throw new Error('The Harness overview has no C# sample')
  const code = fence[1]
    .split('\n')
    .filter((line) => !/^\s*\/\//.test(line))
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .trimEnd()
  const config = (globalThis as { VITEPRESS_CONFIG?: SiteConfig }).VITEPRESS_CONFIG
  const md = await createMarkdownRenderer(config?.srcDir ?? docs, config?.markdown, config?.site.base, config?.logger)
  const pre = /<pre[\s\S]*<\/pre>/.exec(md.render(`\`\`\`csharp\n${code}\n\`\`\`\n`))
  if (!pre) throw new Error('The C# sample did not highlight')
  return pre[0].replace(' v-pre=""', '')
}

export default defineLoader({
  watch: ['./avatars.tsx', '../../../developing/harness/index.md', '../../../../sdk/typescript/packages/avatar/src/*.{ts,tsx}'],
  async load(): Promise<HomeData> {
    const avatars = await loadAvatars()
    const poses = (sequence: Pose[], size: number) =>
      Object.fromEntries(sequence.map((pose) => [pose, avatars.renderMascot(pose, size)])) as Record<Pose, string>
    return {
      mascots: { hero: poses(['idle', 'greeting', 'done'], 84), close: poses(['idle', 'done', 'greeting'], 72) },
      agents: ['Leader', 'Explorer', 'Builder', 'Reviewer', 'Operator'].map((name) => avatars.renderAgent(name, 26)),
      icons: Object.fromEntries([
        ...lucide.map((name) => [name, lucideParts(name)]),
        ...simple.map((name) => [name, simpleIconParts(name)])
      ]),
      program: await highlightProgram()
    }
  }
})
