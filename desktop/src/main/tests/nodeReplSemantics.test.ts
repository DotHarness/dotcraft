import { afterAll, afterEach, expect, it, vi } from 'vitest'
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { pathToFileURL } from 'node:url'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { createFakeBrowserManager } from './helpers/replBrowserFixture'

vi.mock('electron', () => ({ app: { getAppPath: () => process.cwd() }, BrowserWindow: vi.fn() }))
import { NodeReplManager } from '../nodeReplManager'

const fixture = createReplWorkerFixture()
const directory = mkdtempSync(join(tmpdir(), 'dotcraft-repl-modules-'))
const browserManager = createFakeBrowserManager()
const manager = new NodeReplManager(browserManager as never, fixture.fork)
const owner = {} as Electron.BrowserWindow
const evaluate = (code: string, threadId = 'one', extra = {}) => manager.evaluate(owner, { code, threadId, ...extra })
afterEach(async () => { await manager.disposeAll(); vi.clearAllMocks() })
afterAll(async () => {
  await browserManager.closeBackendForTests()
  fixture.dispose()
  rmSync(directory, { recursive: true, force: true })
})

it('retains lexical declarations, destructuring, closures, classes and await across complete cells', async () => {
  expect((await evaluate(`
    const { value } = await Promise.resolve({ value: 3 });
    let count = value;
    function next() { return ++count; }
    class Counter { read() { return count; } }
    const counter = new Counter();
    nodeRepl.write(next());
    counter.read()
  `)).logs).toEqual(['4'])
  expect((await evaluate('next(); counter.read()')).resultText).toBe('5')
  expect((await evaluate('const value = 9; value')).resultText).toBe('9')
  expect((await evaluate('const same = 1; const same = 2')).error).toContain('already been declared')
  expect((await evaluate('return 9')).error).toContain('Illegal return')
  expect((await evaluate('count += 1; throw new Error("after mutation")')).error).toContain('after mutation')
  expect((await evaluate('count')).resultText).toBe('6')
  expect((await evaluate('let broken = ;')).error).toBeTruthy()
  expect((await evaluate('next()')).resultText).toBe('7')
})

it('loads native modules without rewriting text and refreshes module state only on reset', async () => {
  const modulePath = join(directory, 'counter.mjs')
  writeFileSync(modulePath, 'let count = 0; export const next = () => ++count; export const metadata = () => dotcraft.browserSession;')
  const url = JSON.stringify(pathToFileURL(modulePath).href)
  expect((await evaluate(`const mod = await import(${url}); mod.next()`)).resultText).toBe('1')
  expect((await evaluate(`(await import('./counter.mjs')).next()`, 'one', { workspacePath: directory })).resultText).toBe('2')
  expect((await evaluate(`const mod = await import(${url}); mod.next()`, 'two')).resultText).toBe('1')
  expect((await evaluate('mod.metadata().threadId', 'one')).resultText).toBe('one')
  expect((await evaluate('mod.metadata().threadId', 'two')).resultText).toBe('two')
  const literal = 'import( "import(" `import(` /* import( */'
  expect((await evaluate(`// import('missing')\n${JSON.stringify(literal)}`)).resultText).toBe(literal)
  expect((await evaluate('`template import(${2})`')).resultText).toBe('template import(2)')
  manager.reset('one')
  expect((await evaluate(`const mod = await import(${url}); mod.next()`)).resultText).toBe('1')
  expect((await evaluate('mod.next()', 'two')).resultText).toBe('2')
})

it('isolates working directories and host metadata without mutating Electron main globals', async () => {
  const initialDotcraft = (globalThis as any).dotcraft
  const initialRepl = (globalThis as any).nodeRepl
  const initialCwd = process.cwd()
  const [one, two] = await Promise.all([
    evaluate('await new Promise(r => setTimeout(r, 30)); nodeRepl.write({ ...dotcraft.browserSession, cwd: nodeRepl.cwd }); process.cwd()', 'one', { workspacePath: directory, evaluationId: 'a' }),
    evaluate('nodeRepl.write({ ...dotcraft.browserSession, cwd: nodeRepl.cwd }); process.cwd()', 'two', { workspacePath: initialCwd, evaluationId: 'b' })
  ])
  expect(one.resultText).toBe(directory)
  expect(two.resultText).toBe(initialCwd)
  expect(JSON.parse(one.logs[0])).toMatchObject({ threadId: 'one', evaluationId: 'a', cwd: directory })
  expect(JSON.parse(two.logs[0])).toMatchObject({ threadId: 'two', evaluationId: 'b', cwd: initialCwd })
  expect(process.cwd()).toBe(initialCwd)
  expect((globalThis as any).dotcraft).toBe(initialDotcraft)
  expect((globalThis as any).nodeRepl).toBe(initialRepl)
})

it('kills synchronous loops while other tasks continue and starts a clean environment', async () => {
  await evaluate('const before = 1')
  const stuck = evaluate('while (true) {}', 'one', { timeoutMs: 1000, evaluationId: 'loop' })
  expect((await evaluate('const other = 7; other', 'two')).resultText).toBe('7')
  expect((await stuck).error).toContain('timed out')
  expect(browserManager.abortEvaluation).toHaveBeenCalledWith('one', 'loop')
  expect((await evaluate('typeof before')).resultText).toBe('undefined')
  expect((await evaluate('other', 'two')).resultText).toBe('7')
})

it('notifies the worker cancellation hook before killing pending promises', async () => {
  const evidence = join(directory, 'cancel.json')
  await evaluate(`const cancellationFs = await import('node:fs');
    __dotcraftSetChromeCancelHook((id, reason) => cancellationFs.writeFileSync(${JSON.stringify(evidence)}, JSON.stringify({ id, reason })));`)
  const pending = evaluate('await new Promise(() => {})', 'one', { evaluationId: 'cancel-me' })
  await vi.waitFor(() => expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({ evaluationId: 'cancel-me' })))
  expect(manager.cancel('one', 'cancel-me')).toEqual({ ok: true })
  expect((await pending).error).toContain('cancelled')
  expect(JSON.parse(readFileSync(evidence, 'utf8'))).toEqual({ id: 'cancel-me', reason: 'cancelled' })
  expect((await evaluate('typeof cancellationFs')).resultText).toBe('undefined')
})

it('ignores old asynchronous output and host calls after their evaluation ends', async () => {
  await evaluate(`setTimeout(() => {
    nodeRepl.write('stale');
    try { nodeRepl.createElicitation({ stale: true }); } catch {}
    throw new Error('expired callback')
  }, 40); 'scheduled'`)
  const result = await evaluate('await new Promise(r => setTimeout(r, 100)); nodeRepl.write("current"); 42')
  expect(result.logs).toEqual(['current'])
  expect(result.resultText).toBe('42')
  expect(browserManager.handleBrowserUseElicitation).not.toHaveBeenCalled()
})

it('invalidates pending and queued calls on reset and destroys deleted task environments', async () => {
  await evaluate('const original = true')
  const pending = evaluate('await new Promise(() => {})', 'one', { evaluationId: 'pending' })
  const queued = evaluate('nodeRepl.write("must not run")')
  await vi.waitFor(() => expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({ evaluationId: 'pending' })))
  manager.reset('one')
  expect((await pending).error).toContain('reset')
  expect((await queued).error).toContain('cancelled before')
  expect((await evaluate('typeof original')).resultText).toBe('undefined')
  await evaluate('let present = true')
  manager.handleNotification('thread/deleted', { threadId: 'one' })
  expect((await evaluate('typeof present')).resultText).toBe('undefined')
})

it('invalidates queued evaluations during Desktop teardown', async () => {
  await evaluate('const initialized = true')
  const pending = evaluate('while (true) {}', 'one', { evaluationId: 'shutdown' })
  const queued = evaluate('nodeRepl.write("must not run")')
  await vi.waitFor(() => expect(browserManager.prepareNodeRepl).toHaveBeenCalledWith(owner, expect.objectContaining({ evaluationId: 'shutdown' })))
  await manager.disposeAll()
  expect((await pending).error).toContain('disposed')
  expect((await queued).error).toContain('cancelled before')
  expect((await evaluate('typeof initialized')).resultText).toBe('undefined')
})

it('supports cross-cell redeclaration and recovery after a failed initializer without rewriting source', async () => {
  expect((await evaluate('const x = 1; x')).resultText).toBe('1')
  expect((await evaluate('const x = 2; x')).resultText).toBe('2')
  expect((await evaluate('let changing = 3; changing')).resultText).toBe('3')
  expect((await evaluate('let changing = 4; changing')).resultText).toBe('4')
  expect((await evaluate('const failed = await Promise.reject(new Error("failed"))')).error).toContain('failed')
  expect((await evaluate('const failed = 10; failed')).resultText).toBe('10')
  expect((await evaluate('const { field } = { field: 1 }; field')).resultText).toBe('1')
  expect((await evaluate('const { field } = { field: 2 }; field')).resultText).toBe('2')
  expect((await evaluate('class Item { value() { return 1 } }; new Item().value()')).resultText).toBe('1')
  expect((await evaluate('class Item { value() { return 2 } }; new Item().value()')).resultText).toBe('2')
})

it('retains const assignment and closure behavior across cells', async () => {
  await evaluate('const fixed = 1; let observed = 1; const read = () => observed;')
  expect((await evaluate('fixed = 2')).error).toContain('constant variable')
  expect((await evaluate('let observed = 2; read()')).resultText).toBe('2')
})
