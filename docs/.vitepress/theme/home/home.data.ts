import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'
import { createMarkdownRenderer, defineLoader, type SiteConfig } from 'vitepress'
import { lucideParts, simpleIconParts, type IconParts } from '../icons'
import type * as Avatars from './avatars'

export type Pose = 'idle' | 'greeting' | 'done'
export type LookKey = Avatars.ProductLook | 'oratorio'

export interface HomeData {
  mascots: Record<'hero' | 'close', Record<Pose, string>>
  agents: string[]
  looks: Record<LookKey, string>
  icons: Record<string, IconParts>
  program: string
}

declare const data: HomeData
export { data }

const here = dirname(fileURLToPath(import.meta.url))
const repo = resolve(here, '../../../..')
const docs = resolve(repo, 'docs')
const harnessPage = resolve(docs, 'developing/harness/index.md')
const oratorioIcon = resolve(repo, 'desktop/src/bundled-plugins/oratorio/src/assets/oratorio-icon.svg')

const lucide = [
  'app-window', 'arrow-right', 'blocks', 'bot', 'check', 'chevron-down', 'chevron-right', 'circuit-board', 'copy', 'cpu',
  'download', 'external-link', 'file-code', 'layers', 'layout-dashboard', 'monitor', 'mouse-pointer-2', 'plug-zap',
  'puzzle', 'server', 'sparkles', 'terminal'
]
const simple = ['apple', 'dotnet', 'github', 'linux', 'npm', 'nuget', 'windows']

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

function conductor(size: number, serial: number): string {
  const width = (size * 1.3 * 640) / (1024 * 0.75)
  const left = (-153.6 * size) / 1024 + (97 * width) / 640
  const top = (-137.6 * size) / 1024 + (62 * width) / 640
  return readFileSync(oratorioIcon, 'utf8')
    .replace(/<title[^]*?<\/desc>\s*/, '')
    .replace(' role="img" aria-labelledby="title desc"', ' aria-hidden="true"')
    .replace(
      'width="1024" height="1024"',
      `width="${width.toFixed(2)}" height="${width.toFixed(2)}" class="dc-conductor" style="left:${left.toFixed(2)}px;top:${top.toFixed(2)}px"`
    )
    .replace(/id="([\w-]+)"/g, `id="dco${serial}-$1"`)
    .replace(/url\(#([\w-]+)\)/g, `url(#dco${serial}-$1)`)
    .replace(/\n\s*/g, '')
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
      looks: {
        desktop: avatars.renderLook('desktop', 40),
        harness: avatars.renderLook('harness', 40),
        oratorio: conductor(40, 1),
        satellite: avatars.renderLook('satellite', 40),
        avatar: avatars.renderLook('avatar', 40)
      },
      icons: Object.fromEntries([
        ...lucide.map((name) => [name, lucideParts(name)]),
        ...simple.map((name) => [name, simpleIconParts(name)])
      ]),
      program: await highlightProgram()
    }
  }
})
