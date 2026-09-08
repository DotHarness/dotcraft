import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname, resolve, relative, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'esbuild'

const sdk = dirname(dirname(fileURLToPath(import.meta.url)))
const work = mkdtempSync(join(tmpdir(), 'dotcraft-avatar-consumer-'))
const manifest = JSON.parse(readFileSync(join(sdk, 'packages/avatar/package.json'), 'utf8'))
const npmCli = process.env.npm_execpath ?? join(dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js')
function run(args, cwd) { execFileSync(process.execPath, [npmCli, ...args], {cwd, stdio:'pipe'}) }
mkdirSync(join(work, 'packs'))
run(['pack','--workspace','@dotcraft/avatar','--pack-destination',join(work,'packs')], sdk)
const tarball = join(work, 'packs', `dotcraft-avatar-${manifest.version}.tgz`)
writeFileSync(join(work,'package.json'), JSON.stringify({private:true,type:'module',dependencies:{'@dotcraft/avatar':tarball,react:'^19.0.0','react-dom':'^19.0.0','@types/react':'^19.0.0'}}))
run(['install','--ignore-scripts','--no-audit','--no-fund'], work)
const installed = join(work, 'node_modules/@dotcraft/avatar')
let mappedFiles = 0
function verifyMaps(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const file = join(directory, entry.name)
    if (entry.isDirectory()) { verifyMaps(file); continue }
    if (!/\.(js|ts)$/.test(file)) continue
    const reference = readFileSync(file, 'utf8').match(/\/\/# sourceMappingURL=(\S+)/)?.[1]
    if (!reference) continue
    mappedFiles++
    const mapPath = resolve(dirname(file), reference)
    assert.ok(!isAbsolute(reference) && !relative(installed, mapPath).startsWith('..'), 'Map stays inside package')
    assert.ok(existsSync(mapPath), `Missing map: ${reference}`)
    const map = JSON.parse(readFileSync(mapPath, 'utf8'))
    assert.ok(!map.sourceRoot, 'Maps use package-relative source paths')
    for (const source of map.sources) {
      const sourcePath = resolve(dirname(mapPath), source)
      assert.ok(!isAbsolute(source) && !/^[a-z]+:/i.test(source) && !relative(installed, sourcePath).startsWith('..'), 'Source stays inside package')
      assert.ok(existsSync(sourcePath), `Missing mapped source: ${source}`)
      readFileSync(sourcePath, 'utf8')
    }
  }
}
verifyMaps(join(installed, 'dist'))
assert.ok(mappedFiles > 0, 'The package retains source maps')
console.log(`Avatar source maps passed: ${mappedFiles} mapped files, all sources shipped.`)
writeFileSync(join(work,'core.mjs'), `export { deriveAppearance } from '@dotcraft/avatar'`)
const core = await build({entryPoints:[join(work,'core.mjs')],bundle:true,platform:'node',format:'esm',write:false,metafile:true})
assert.ok(!Object.keys(core.metafile.inputs).some(path=>/node_modules\/react\//.test(path)))
writeFileSync(join(work,'app.tsx'), `import { Avatar } from '@dotcraft/avatar/react';
import { deriveAppearance } from '@dotcraft/avatar';
import '@dotcraft/avatar/styles.css';
export const preview = <Avatar name="Reviewer" state="working" motion="system" label="Reviewer: working" />;
export const greeting = <Avatar name="Reviewer" expression="happy" gesture="blink" gestureSequence={2} onGestureComplete={(sequence: number) => void sequence} />;
export const appearance = deriveAppearance('Reviewer');`)
writeFileSync(join(work,'tsconfig.json'), JSON.stringify({compilerOptions:{target:'ES2022',module:'NodeNext',moduleResolution:'NodeNext',jsx:'react-jsx',strict:true,noEmit:true,skipLibCheck:true},include:['app.tsx']}))
execFileSync(process.execPath,[join(sdk,'node_modules/typescript/bin/tsc'),'-p',join(work,'tsconfig.json')],{stdio:'pipe'})
await build({entryPoints:[join(work,'app.tsx')],bundle:true,platform:'browser',format:'esm',outdir:join(work,'out'),logLevel:'silent'})
const css=readFileSync(join(work,'out/app.css'),'utf8')
assert.ok(css.includes('.dca-robot'))
const isolatedCss = !css.includes('--text-primary') && !css.includes('html[data-reduce-motion')
writeFileSync(join(work,'ssr.mjs'), `import assert from 'node:assert/strict';
import {createElement} from 'react'; import {renderToStaticMarkup} from 'react-dom/server';
import {Avatar} from '@dotcraft/avatar/react';
assert.ok(renderToStaticMarkup(createElement(Avatar,{name:'Reviewer',label:'Reviewer'})).includes('aria-label="Reviewer"'));`)
execFileSync(process.execPath,[join(work,'ssr.mjs')],{stdio:'pipe'})
console.log(`Avatar consumer functional checks passed: core without React, TypeScript, browser CSS bundle, SSR. Artifacts: ${work}`)
assert.ok(isolatedCss, 'Avatar CSS must not depend on host theme variables or selectors')
