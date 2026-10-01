import nodeProcess from 'node:process'
import { afterAll, afterEach, describe, expect, it, vi } from 'vitest'
import { createFakeBrowserManager } from './helpers/replBrowserFixture'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'
import { NodeReplManager } from '../nodeReplManager'

const { mockedAppPath } = vi.hoisted(() => ({
  mockedAppPath: process.cwd()
}))

vi.mock('electron', () => ({
  app: { getAppPath: () => mockedAppPath },
  BrowserWindow: vi.fn()
}))

const workerFixture = createReplWorkerFixture()
afterAll(() => workerFixture.dispose())

type FakeBrowser = ReturnType<typeof createFakeBrowserManager>

const prelude = `
  const { setupBrowserRuntime } = await import(dotcraft.browserClientPath)
  const agent = await setupBrowserRuntime({ backend: "iab" })
  const browser = await agent.browsers.get("iab")
  const tab = await browser.tabs.new()
  const messageOf = async (action) => {
    try {
      await action()
      return "resolved"
    } catch (error) {
      return error instanceof Error ? error.message : String(error)
    }
  }
`

const mouse = (type: string, x: number, y: number, extra: Record<string, unknown> = {}) => ({
  method: 'Input.dispatchMouseEvent', type, x, y, ...extra
})
const cursor = (x: number, y: number, extra: Record<string, unknown> = {}) => ({ method: 'cursor', x, y, ...extra })
const moved = (x: number, y: number, modifiers = 0) => mouse('mouseMoved', x, y, { button: 'none', buttons: 0, modifiers })
const pressed = (x: number, y: number, clickCount: number, extra: Record<string, unknown> = {}) =>
  mouse('mousePressed', x, y, { button: 'left', buttons: 1, clickCount, modifiers: 0, ...extra })
const released = (x: number, y: number, clickCount: number, extra: Record<string, unknown> = {}) =>
  mouse('mouseReleased', x, y, { button: 'left', buttons: 0, clickCount, modifiers: 0, ...extra })

const primary = nodeProcess.platform === 'darwin'
  ? { key: 'Meta', code: 'MetaLeft', keyCode: 91, mask: 4 }
  : { key: 'Control', code: 'ControlLeft', keyCode: 17, mask: 2 }

describe('Node REPL browser client input', () => {
  const initialCwd = nodeProcess.cwd()
  const managers: NodeReplManager[] = []
  const browserManagers: FakeBrowser[] = []
  let runCount = 0

  async function run(browserManager: FakeBrowser, body: string): Promise<any> {
    const manager = new NodeReplManager(browserManager as never, workerFixture.fork)
    managers.push(manager)
    browserManagers.push(browserManager)
    const result = await manager.evaluate({} as Electron.BrowserWindow, { threadId: `input-${runCount += 1}`, code: `${prelude}
${body}` })
    expect(result.error).toBeUndefined()
    return result.resultText ? JSON.parse(result.resultText) : undefined
  }

  afterEach(async () => {
    if (nodeProcess.cwd() !== initialCwd) nodeProcess.chdir(initialCwd)
    await Promise.all(managers.map((manager) => manager.disposeAll()))
    await Promise.all(browserManagers.map((manager) => manager.closeBackendForTests()))
    managers.length = 0
    browserManagers.length = 0
  })

  const inputMethods = (browserManager: FakeBrowser) => browserManager.inputSequence.map((entry) => entry.type ?? entry.method)
  const pasteExpressions = (browserManager: FakeBrowser) => browserManager.cdpCommands
    .filter((command) => command.method === 'Runtime.evaluate')
    .map((command) => String((command.commandParams as { expression?: string }).expression))
    .filter((expression) => expression.includes('ClipboardEvent'))

  it('moves the cursor before CDP pointer input for move, click, and double click', async () => {
    const browserManager = createFakeBrowserManager()
    await run(browserManager, `
      await tab.cua.move({ x: 12, y: 18 })
      await tab.cua.click({ x: 30, y: 40 })
      await tab.cua.double_click({ x: 50, y: 60, keypress: ["Shift"] })
      await tab.cua.click({ x: 70, y: 80, button: "right" })
      true
    `)

    expect(browserManager.inputSequence).toEqual([
      cursor(12, 18), moved(12, 18),
      cursor(30, 40), moved(30, 40), pressed(30, 40, 1), released(30, 40, 1),
      cursor(50, 60), moved(50, 60, 8),
      pressed(50, 60, 1, { modifiers: 8 }), released(50, 60, 1, { modifiers: 8 }),
      pressed(50, 60, 2, { modifiers: 8 }), released(50, 60, 2, { modifiers: 8 }),
      cursor(70, 80), moved(70, 80),
      pressed(70, 80, 1, { button: 'right', buttons: 2 }), released(70, 80, 1, { button: 'right' })
    ])
  })

  it('rejects positional and non-finite coordinates with the object-shaped form', async () => {
    const browserManager = createFakeBrowserManager()
    const payload = await run(browserManager, `
      JSON.stringify({
        positional: await messageOf(() => tab.cua.click(940, 444)),
        missing: await messageOf(() => tab.cua.move({ x: 1 })),
        drag: await messageOf(() => tab.cua.drag({ path: [{ x: "left", y: 2 }] }))
      })
    `)

    for (const message of Object.values(payload) as string[]) {
      expect(message).toContain('InvalidArgument')
      expect(message).toContain('{ x: 940, y: 444 }')
    }
    expect(browserManager.inputSequence).toEqual([])
  })

  it('keeps acting when the cursor fails and does not wait for unawaited drag moves', async () => {
    const failing = createFakeBrowserManager({ moveMouseFails: true })
    await run(failing, 'await tab.cua.click({ x: 5, y: 6 }); true')
    expect(inputMethods(failing)).toEqual(['cursor', 'mouseMoved', 'mousePressed', 'mouseReleased'])

    const held = createFakeBrowserManager({ holdUnawaitedCursorMoves: true })
    await run(held, 'await tab.cua.drag({ path: [{ x: 1, y: 2 }, { x: 3, y: 4 }, { x: 5, y: 6 }] }); true')
    const drag = (x: number, y: number) => mouse('mouseMoved', x, y, { button: 'left', buttons: 1, modifiers: 0 })
    expect(held.inputSequence).toEqual([
      cursor(1, 2), moved(1, 2), pressed(1, 2, 1),
      cursor(3, 4, { waitForArrival: false }), drag(3, 4),
      cursor(5, 6, { waitForArrival: false }), drag(5, 6),
      released(5, 6, 1)
    ])
  })

  it('releases the button when a drag move fails', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }) => {
        if (commandParams.type === 'mouseMoved' && commandParams.buttons === 1 && commandParams.x === 3) throw new Error('page closed')
      }
    })
    const message = await run(browserManager, `
      JSON.stringify(await messageOf(() => tab.cua.drag({ path: [{ x: 1, y: 2 }, { x: 3, y: 4 }, { x: 5, y: 6 }] })))
    `)

    expect(message).toContain('page closed')
    expect(browserManager.inputSequence.at(-1)).toEqual(released(3, 4, 1))
    expect(inputMethods(browserManager).filter((type) => type === 'mouseReleased')).toHaveLength(1)
  })

  it('scrolls with one mouse wheel event at the origin', async () => {
    const browserManager = createFakeBrowserManager()
    const message = await run(browserManager, `
      await tab.cua.scroll({ x: 12, y: 18, scrollX: 5, scrollY: -80, keypress: ["Control"] })
      await tab.dom_cua.scroll({ y: 120 })
      await tab.dom_cua.scroll({ node_id: "42", x: 7, y: 9 })
      JSON.stringify(await messageOf(() => tab.cua.scroll({ x: 1, y: 2 })))
    `)

    const wheel = (x: number, y: number, deltaX: number, deltaY: number, modifiers = 0) =>
      mouse('mouseWheel', x, y, { deltaX, deltaY, modifiers })
    expect(message).toContain('non-zero distance')
    expect(browserManager.inputSequence).toEqual([
      cursor(12, 18), moved(12, 18, 2), wheel(12, 18, 5, -80, 2),
      cursor(640, 360), moved(640, 360), wheel(640, 360, 0, 120),
      cursor(60, 40), moved(60, 40), wheel(60, 40, 7, 9)
    ])
    expect(browserManager.cdpCommands.map((command) => command.method)).not.toContain('Input.synthesizeScrollGesture')
  })

  it('presses chords with US layout fields, modifiers, and reverse key up', async () => {
    const browserManager = createFakeBrowserManager()
    await run(browserManager, `
      await tab.cua.keypress({ keys: ["Shift", "a"] })
      await tab.cua.keypress({ keys: ["Enter"] })
      await tab.cua.keypress({ keys: ["ControlOrMeta+a"] })
      true
    `)

    const down = (type: string, key: string, code: string, keyCode: number, extra: Record<string, unknown>) => ({
      method: 'Input.dispatchKeyEvent', type, key, code, windowsVirtualKeyCode: keyCode, location: 0, isKeypad: false, ...extra
    })
    const up = (key: string, code: string, keyCode: number, modifiers: number, location = 0) => ({
      method: 'Input.dispatchKeyEvent', type: 'keyUp', key, code, windowsVirtualKeyCode: keyCode, location, isKeypad: false, modifiers
    })
    expect(browserManager.inputSequence).toEqual([
      down('rawKeyDown', 'Shift', 'ShiftLeft', 16, { modifiers: 8, text: '', unmodifiedText: '', location: 1 }),
      down('keyDown', 'A', 'KeyA', 65, { modifiers: 8, text: 'A', unmodifiedText: 'A' }),
      up('A', 'KeyA', 65, 8),
      up('Shift', 'ShiftLeft', 16, 0, 1),
      down('keyDown', 'Enter', 'Enter', 13, { modifiers: 0, text: '\r', unmodifiedText: '\r' }),
      up('Enter', 'Enter', 13, 0),
      down('rawKeyDown', primary.key, primary.code, primary.keyCode, { modifiers: primary.mask, text: '', unmodifiedText: '', location: 1 }),
      down('rawKeyDown', 'a', 'KeyA', 65, { modifiers: primary.mask, text: '', unmodifiedText: '', commands: ['selectAll'] }),
      up('a', 'KeyA', 65, primary.mask),
      up(primary.key, primary.code, primary.keyCode, 0, 1)
    ])
  })

  it('releases pressed keys when a later key fails and rejects unknown keys', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }) => {
        if (commandParams.key === 'x') throw new Error('page closed')
      }
    })
    const payload = await run(browserManager, `
      JSON.stringify({
        failed: await messageOf(() => tab.cua.keypress({ keys: ["Alt", "x"] })),
        unknown: await messageOf(() => tab.cua.keypress({ keys: ["NoSuchKey"] }))
      })
    `)

    expect(payload.failed).toContain('page closed')
    expect(payload.unknown).toContain('Unknown key: "NoSuchKey"')
    expect(browserManager.inputSequence.at(-1)).toMatchObject({ type: 'keyUp', key: 'Alt', modifiers: 0 })
  })

  it('routes copy and paste chords through the virtual clipboard', async () => {
    const browserManager = createFakeBrowserManager()
    const payload = await run(browserManager, `
      await tab.clipboard.writeText("from clipboard")
      await tab.cua.keypress({ keys: ["Control", "v"] })
      await tab.cua.keypress({ keys: ["Control", "c"] })
      JSON.stringify({
        clipboard: await tab.clipboard.readText(),
        blocked: await messageOf(() => tab.cua.keypress({ keys: ["Control", "Alt", "v"] }))
      })
    `)

    expect(inputMethods(browserManager)).toEqual([])
    const [paste, copy] = pasteExpressions(browserManager)
    expect(paste).toContain('"action":"paste"')
    expect(paste).toContain('from clipboard')
    expect(copy).toContain('"action":"copy"')
    expect(payload.clipboard).toBe('copied text')
    expect(payload.blocked).toContain('Native clipboard shortcuts')
  })

  it('types text as a synthetic paste for CUA and DOM-CUA', async () => {
    const browserManager = createFakeBrowserManager()
    await run(browserManager, `
      await tab.cua.type({ text: "héllo" })
      await tab.dom_cua.type({ node_id: "42", text: "node text" })
      true
    `)

    const [first, second] = pasteExpressions(browserManager)
    expect(first).toContain('"action":"paste"')
    expect(first).toContain('"mimeType":"text/plain","text":"héllo"')
    expect(first).toContain('"replace":false')
    expect(second).toContain('node text')
    expect(inputMethods(browserManager)).toEqual(['cursor', 'mouseMoved', 'mousePressed', 'mouseReleased'])
  })

  it('fills, types, and presses locators without moving the cursor or clicking', async () => {
    const browserManager = createFakeBrowserManager({ fillNeedsInput: true })
    await run(browserManager, `
      const name = tab.playwright.getByLabel("Name")
      await name.fill("Ada")
      await name.type(" Lovelace")
      await name.press("Enter")
      await tab.clipboard.writeText("pasted")
      await name.press("Control+v")
      true
    `)

    const operations = browserManager.unhandledCommands
      .filter((command) => command.type === 'playwright_locator_operation')
      .map((command) => [command.operation, command.payload])
    expect(operations).toEqual([
      ['fill', { value: 'Ada' }],
      ['focus', { requireEditable: true }],
      ['focus', { requireEditable: false }],
      ['focus', { requireEditable: false }]
    ])
    const pastes = pasteExpressions(browserManager)
    expect(pastes[0]).toContain('"replace":true')
    expect(pastes[0]).toContain('Ada')
    expect(pastes[1]).toContain('"replace":false')
    expect(pastes[1]).toContain(' Lovelace')
    expect(pastes[2]).toContain('pasted')
    expect(browserManager.moveMouseCalls).toEqual([])
    expect(inputMethods(browserManager)).toEqual(['keyDown', 'keyUp'])
  })

  it('fills simple inputs through the page without pasting', async () => {
    const browserManager = createFakeBrowserManager()
    await run(browserManager, 'await tab.playwright.getByLabel("Name").fill("Ada"); true')

    expect(pasteExpressions(browserManager)).toEqual([])
    expect(browserManager.inputSequence).toEqual([])
  })

  it('prepares locator clicks for actionable targets and uses the click sequence', async () => {
    const browserManager = createFakeBrowserManager()
    await run(browserManager, `
      const save = tab.playwright.getByRole("button", { name: "Save" })
      await save.click()
      await save.click({ force: true })
      await save.dblclick()
      const accept = tab.playwright.getByLabel("Accept")
      await accept.check()
      await accept.check()
      await accept.uncheck()
      true
    `)

    const operations = browserManager.unhandledCommands
      .filter((command) => command.type === 'playwright_locator_operation')
      .map((command) => [command.operation, command.payload])
    expect(operations).toEqual([
      ['prepareClick', { force: false }],
      ['prepareClick', { force: true }],
      ['prepareClick', { force: false }],
      ['checked', {}],
      ['prepareClick', { force: false }],
      ['checked', {}],
      ['checked', {}],
      ['checked', {}],
      ['prepareClick', { force: false }],
      ['checked', {}]
    ])
    const click = [cursor(60, 40), moved(60, 40), pressed(60, 40, 1), released(60, 40, 1)]
    expect(browserManager.inputSequence).toEqual([
      ...click,
      ...click,
      ...click, pressed(60, 40, 2), released(60, 40, 2),
      ...click,
      ...click
    ])
  })

  it('resolves a click after 250 ms when the page does not start loading', async () => {
    const browserManager = createFakeBrowserManager()
    const elapsed = await run(browserManager, `
      const started = Date.now()
      await tab.cua.click({ x: 1, y: 2 })
      Date.now() - started
    `)

    expect(elapsed).toBeGreaterThanOrEqual(240)
    expect(elapsed).toBeLessThan(2000)
  })

  it('waits for the load event when a click starts a navigation', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }, emit) => {
        if (commandParams.type !== 'mouseReleased') return
        emit('Page.frameStartedLoading', { frameId: 'main-frame' })
        setTimeout(() => emit('Page.loadEventFired', { timestamp: 1 }), 700)
      }
    })
    const elapsed = await run(browserManager, `
      const started = Date.now()
      await tab.cua.click({ x: 1, y: 2 })
      Date.now() - started
    `)

    expect(elapsed).toBeGreaterThanOrEqual(650)
    expect(elapsed).toBeLessThan(3000)
  })

  it('resolves a click immediately for same-document navigation', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }, emit) => {
        if (commandParams.type === 'mouseReleased') emit('Page.navigatedWithinDocument', { frameId: 'main-frame', url: 'http://localhost:3000/#a' })
      }
    })
    const elapsed = await run(browserManager, `
      const started = Date.now()
      await tab.cua.click({ x: 1, y: 2 })
      Date.now() - started
    `)

    expect(elapsed).toBeLessThan(240)
  })

  it('fails a click when the navigation is blocked', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }, emit) => {
        if (commandParams.type === 'mouseReleased') emit('Page.navigationBlocked', { errorDescription: 'blocked by policy' })
      }
    })
    const message = await run(browserManager, 'JSON.stringify(await messageOf(() => tab.cua.click({ x: 1, y: 2 })))')

    expect(message).toContain('NavigationFailed')
    expect(message).toContain('blocked by policy')
  })

  it('stops waiting for a load that never completes after three seconds', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }, emit) => {
        if (commandParams.type === 'mouseReleased') emit('Page.frameStartedLoading', { frameId: 'main-frame' })
      }
    })
    const elapsed = await run(browserManager, `
      const started = Date.now()
      await tab.cua.click({ x: 1, y: 2 })
      Date.now() - started
    `)

    expect(elapsed).toBeGreaterThanOrEqual(2900)
    expect(elapsed).toBeLessThan(8000)
  }, 15_000)

  it('waits for the load window before rethrowing a failed click', async () => {
    const browserManager = createFakeBrowserManager({
      onInput: ({ commandParams }, emit) => {
        if (commandParams.type !== 'mousePressed') return
        emit('Page.frameStartedLoading', { frameId: 'main-frame' })
        setTimeout(() => emit('Page.loadEventFired', { timestamp: 1 }), 600)
        throw new Error('page closed')
      }
    })
    const payload = await run(browserManager, `
      const started = Date.now()
      const message = await messageOf(() => tab.cua.click({ x: 1, y: 2 }))
      JSON.stringify({ message, elapsed: Date.now() - started })
    `)

    expect(payload.message).toContain('page closed')
    expect(payload.elapsed).toBeGreaterThanOrEqual(550)
  })
})
