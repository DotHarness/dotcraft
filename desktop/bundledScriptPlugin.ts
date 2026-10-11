import { resolve } from 'node:path'
import { build } from 'esbuild'

const SUFFIX = '?bundled'

interface BundlePluginContext {
  resolve: (source: string, importer: string | undefined, options: { skipSelf: boolean }) => Promise<{ id: string } | null>
  addWatchFile: (id: string) => void
}

export function bundledScriptPlugin() {
  let minify = false
  return {
    name: 'dotcraft-bundled-script',
    enforce: 'pre' as const,
    configResolved(config: { command: string }) {
      minify = config.command === 'build'
    },
    async resolveId(this: BundlePluginContext, source: string, importer: string | undefined) {
      if (!source.endsWith(SUFFIX)) return null
      const resolved = await this.resolve(source.slice(0, -SUFFIX.length), importer, { skipSelf: true })
      return resolved ? `${resolved.id}${SUFFIX}` : null
    },
    async load(this: BundlePluginContext, id: string) {
      if (!id.endsWith(SUFFIX)) return null
      const result = await build({
        entryPoints: [id.slice(0, -SUFFIX.length)],
        bundle: true,
        write: false,
        format: 'iife',
        platform: 'browser',
        target: 'es2022',
        minify,
        metafile: true,
        legalComments: 'none',
        nodePaths: [resolve('node_modules')],
        logLevel: 'silent'
      })
      for (const input of Object.keys(result.metafile.inputs)) this.addWatchFile(resolve(input))
      return `export default ${JSON.stringify(result.outputFiles[0].text)}`
    }
  }
}
