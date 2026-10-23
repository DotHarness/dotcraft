import { afterEach, describe, expect, it, vi } from 'vitest'
import { GenerativeUiConnection, type GenerativeUiPort } from '../components/conversation/generativeUi/generativeUiConnection'
import {
  GENERATIVE_UI_LIMITS,
  type GenerativeUiAction,
  type GenerativeUiElementNode,
  type GenerativeUiFailure,
  type GenerativeUiNode,
  type GenerativeUiSavedState
} from '../components/conversation/generativeUi/generativeUiProtocol'
import { createGenerativeUiRuntime } from '../components/conversation/generativeUi/runner/generativeUiRuntime'

interface Session {
  connection: GenerativeUiConnection
  snapshots: GenerativeUiNode[][]
  actions: GenerativeUiAction[]
  states: GenerativeUiSavedState[]
  failures: GenerativeUiFailure[]
}

const open: MessagePort[] = []
const waitFor = <T>(callback: () => T) => vi.waitFor(callback, { interval: 1 })

afterEach(() => {
  for (const port of open.splice(0)) port.close()
  vi.useRealTimers()
})

function run(code: string, initialState?: GenerativeUiSavedState): Session {
  const channel = new MessageChannel()
  open.push(channel.port1, channel.port2)
  const runtime = createGenerativeUiRuntime(message => channel.port1.postMessage(message))
  channel.port1.addEventListener('message', event => runtime.receive(event.data))
  channel.port1.start()
  const session: Session = { connection: null as unknown as GenerativeUiConnection, snapshots: [], actions: [], states: [], failures: [] }
  session.connection = new GenerativeUiConnection(channel.port2 as unknown as GenerativeUiPort, {
    onSnapshot: tree => session.snapshots.push(tree),
    onAction: action => session.actions.push(action),
    onState: state => session.states.push(state),
    onFailure: failure => session.failures.push(failure)
  })
  session.connection.start({ code, theme: 'dark', breakpoint: 'regular', initialState })
  return session
}

function find(nodes: GenerativeUiNode[], type: string): GenerativeUiElementNode {
  for (const node of nodes) {
    if (typeof node === 'string') continue
    if (node.type === type) return node
    try {
      return find(node.children, type)
    } catch {
      continue
    }
  }
  throw new Error(`No ${type} node`)
}

function callbackId(node: GenerativeUiElementNode, prop: string): string {
  return (node.props[prop] as { $callback: string }).$callback
}

describe('generative UI runtime and connection', () => {
  it('reports compile errors with their source position', async () => {
    const session = run('function App() {\n  const total = 1\n  const label = ;\n  return <Text>{label}</Text>\n}')

    await waitFor(() => expect(session.failures).toHaveLength(1))
    expect(session.failures[0]).toMatchObject({ stage: 'compile_error', line: 3, column: 17 })
    expect(session.snapshots).toHaveLength(0)
  })

  it('round-trips callbacks and re-renders on state updates', async () => {
    const session = run(`
      function Counter() {
        const [count, setCount] = DotCraft.useState(1)
        return <Row><Text>{'Count ' + count}</Text><Button label="Add" onClick={() => setCount(count + 1)} /></Row>
      }
      DotCraft.render(<Counter />)
    `)

    await waitFor(() => expect(session.snapshots).toHaveLength(1))
    expect(find(session.snapshots[0], 'Text').children).toEqual(['Count 1'])
    const id = callbackId(find(session.snapshots[0], 'Button'), 'onClick')

    session.connection.invoke(id, [])
    await waitFor(() => expect(session.snapshots).toHaveLength(2))
    expect(find(session.snapshots[1], 'Text').children).toEqual(['Count 2'])
    expect(callbackId(find(session.snapshots[1], 'Button'), 'onClick')).toBe(id)
  })

  it('passes callback arguments and runs effects after render', async () => {
    const session = run(`
      function Field() {
        const [value, setValue] = DotCraft.useState('')
        const [echo, setEcho] = DotCraft.useState('')
        DotCraft.useEffect(() => { setEcho(value.toUpperCase()) }, [value])
        return <Col><Input value={value} onChange={setValue} /><Text>{echo}</Text></Col>
      }
      DotCraft.render(<Field />)
    `)

    await waitFor(() => expect(session.snapshots.length).toBeGreaterThan(0))
    session.connection.invoke(callbackId(find(session.snapshots.at(-1)!, 'Input'), 'onChange'), ['hello'])
    await waitFor(() => expect(find(session.snapshots.at(-1)!, 'Text').children).toEqual(['HELLO']))
  })

  it('forwards actions requested by a user callback and drops unprompted ones', async () => {
    const session = run(`
      function Ask() {
        DotCraft.useEffect(() => { DotCraft.sendMessage('unprompted') }, [])
        return <Button label="Ask" onClick={() => DotCraft.sendMessage('Explain the score')} />
      }
      DotCraft.render(<Ask />)
    `)

    await waitFor(() => expect(session.snapshots).toHaveLength(1))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(session.actions).toEqual([])

    session.connection.invoke(callbackId(find(session.snapshots[0], 'Button'), 'onClick'), [])
    await waitFor(() => expect(session.actions).toEqual([{ kind: 'sendMessage', text: 'Explain the score' }]))
  })

  it('rejects a prompt over its limit while larger copies pass', async () => {
    const session = run(`
      function Tools() {
        return <Row>
          <Button label="Copy" onClick={() => DotCraft.copy('c'.repeat(${GENERATIVE_UI_LIMITS.maxPromptBytes + 1}))} />
          <Button label="Ask" onClick={() => DotCraft.sendMessage('s'.repeat(${GENERATIVE_UI_LIMITS.maxPromptBytes + 1}))} />
        </Row>
      }
      DotCraft.render(<Tools />)
    `)

    await waitFor(() => expect(session.snapshots).toHaveLength(1))
    const [copy, ask] = session.snapshots[0].flatMap(node => typeof node === 'string' ? [] : node.children) as GenerativeUiElementNode[]
    session.connection.invoke(callbackId(copy, 'onClick'), [])
    await waitFor(() => expect(session.actions).toEqual([expect.objectContaining({ kind: 'copy' })]))
    session.connection.invoke(callbackId(ask, 'onClick'), [])
    await waitFor(() => expect(session.failures).toEqual([expect.objectContaining({ stage: 'callback_error' })]))
  })

  it('saves useState after user interaction and restores it into a fresh runner', async () => {
    const program = `
      function Step({ label }) {
        const [done, setDone] = DotCraft.useState(false)
        return <Checkbox label={label} checked={done} onChange={setDone} />
      }
      function Quiz() {
        const [score, setScore] = DotCraft.useState(0)
        const ref = DotCraft.useRef(() => {})
        const [handler] = DotCraft.useState(() => () => {})
        return <Col>
          <Slider label="Score" min={0} max={10} value={score} onChange={setScore} />
          <Step label="Reviewed" />
        </Col>
      }
      DotCraft.render(<Quiz />)
    `
    const first = run(program)
    await waitFor(() => expect(first.snapshots).toHaveLength(1))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(first.states).toEqual([])

    first.connection.invoke(callbackId(find(first.snapshots[0], 'Slider'), 'onChange'), [7])
    await waitFor(() => expect(first.states).toHaveLength(1))
    first.connection.invoke(callbackId(find(first.snapshots[0], 'Checkbox'), 'onChange'), [true])
    await waitFor(() => expect(first.states).toHaveLength(2))
    const saved = first.states[1]
    expect(JSON.parse(JSON.stringify(saved))).toEqual(saved)

    const second = run(program, saved)
    await waitFor(() => expect(second.snapshots).toHaveLength(1))
    expect(find(second.snapshots[0], 'Slider').props.value).toBe(7)
    expect(find(second.snapshots[0], 'Checkbox').props.checked).toBe(true)
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(second.states).toEqual([])
  })

  it('restores a state saved elsewhere into the running program without saving it again', async () => {
    const session = run(`
      function Step({ label }) {
        const [done, setDone] = DotCraft.useState(false)
        return <Checkbox label={label} checked={done} onChange={setDone} />
      }
      function Quiz() {
        const [score, setScore] = DotCraft.useState(0)
        const [mounts, setMounts] = DotCraft.useState(0)
        DotCraft.useEffect(() => { setMounts(value => value + 1) }, [])
        return <Col>
          <Slider label="Score" min={0} max={10} value={score} onChange={setScore} />
          <Text>{'Mounted ' + mounts}</Text>
          {score > 5 && <Step label="Bonus" />}
        </Col>
      }
      DotCraft.render(<Quiz />)
    `)
    await waitFor(() => expect(find(session.snapshots.at(-1)!, 'Text').children).toEqual(['Mounted 1']))
    session.connection.invoke(callbackId(find(session.snapshots.at(-1)!, 'Slider'), 'onChange'), [3])
    await waitFor(() => expect(session.states).toHaveLength(1))

    session.connection.restore({ 'r#Quiz': { 0: 8 }, 'r#Quiz.2#Step': { 0: true } })

    await waitFor(() => expect(find(session.snapshots.at(-1)!, 'Slider').props.value).toBe(8))
    const tree = session.snapshots.at(-1)!
    expect(find(tree, 'Checkbox').props.checked).toBe(true)
    expect(find(tree, 'Text').children).toEqual(['Mounted 1'])
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(session.states).toHaveLength(1)
    expect(session.failures).toEqual([])
  })

  it('ignores saved state that does not follow a user interaction', () => {
    const listeners: Array<(event: MessageEvent) => void> = []
    const port = { postMessage: vi.fn(), addEventListener: (_: 'message', listener: (event: MessageEvent) => void) => listeners.push(listener), removeEventListener: vi.fn(), close: vi.fn() }
    const onState = vi.fn()
    const connection = new GenerativeUiConnection(port, { onSnapshot: vi.fn(), onAction: vi.fn(), onState, onFailure: vi.fn() })
    const deliver = (): void => listeners.forEach(listener => listener({ data: { protocolVersion: 1, type: 'state', state: { 'r#App': { 0: 1 } } } } as MessageEvent))

    deliver()
    expect(onState).not.toHaveBeenCalled()
    connection.invoke('r:onClick', [])
    deliver()
    expect(onState).toHaveBeenCalledTimes(1)
  })

  it('reports a throwing event handler as a callback error', async () => {
    const session = run(`DotCraft.render(<Button label="Boom" onClick={() => { throw new Error('nope') }} />)`)

    await waitFor(() => expect(session.snapshots).toHaveLength(1))
    session.connection.invoke(callbackId(find(session.snapshots[0], 'Button'), 'onClick'), [])
    await waitFor(() => expect(session.failures).toEqual([expect.objectContaining({ stage: 'callback_error', message: 'nope' })]))
  })

  it('passes an undeclared component through for the host to reject', async () => {
    const session = run('DotCraft.render(<Card><Sparkline values={[1, 2]} /></Card>)')

    await waitFor(() => expect(session.snapshots).toHaveLength(1))
    expect(find(session.snapshots[0], 'Sparkline').props).toEqual({ values: [1, 2] })
  })

  it('stops a tree that exceeds the component limit', async () => {
    const session = run(`DotCraft.render(<Col>{Array.from({ length: ${GENERATIVE_UI_LIMITS.maxNodes + 1} }, (_, i) => <Text key={i}>x</Text>)}</Col>)`)

    await waitFor(() => expect(session.failures).toEqual([expect.objectContaining({ stage: 'payload_too_large' })]))
    expect(session.snapshots).toHaveLength(0)
  })

  it('stops a render loop that never settles', async () => {
    const session = run(`
      function Loop() {
        const [n, setN] = DotCraft.useState(0)
        DotCraft.useEffect(() => { setN(n + 1) })
        return <Text>{n}</Text>
      }
      DotCraft.render(<Loop />)
    `)

    await waitFor(() => expect(session.failures).toEqual([expect.objectContaining({ stage: 'runtime_error' })]))
  })

  it('rejects an oversized program before starting it', () => {
    const port = { postMessage: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), close: vi.fn() }
    const onFailure = vi.fn()
    const connection = new GenerativeUiConnection(port, { onSnapshot: vi.fn(), onAction: vi.fn(), onFailure })

    connection.start({ code: 'x'.repeat(GENERATIVE_UI_LIMITS.maxSourceBytes + 1), theme: 'light', breakpoint: 'compact' })

    expect(onFailure).toHaveBeenCalledWith(expect.objectContaining({ stage: 'payload_too_large' }))
    expect(port.postMessage).not.toHaveBeenCalledWith(expect.objectContaining({ type: 'start' }))
  })

  it('times out a program that never renders', () => {
    vi.useFakeTimers()
    const port = { postMessage: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), close: vi.fn() }
    const onFailure = vi.fn()
    const connection = new GenerativeUiConnection(port, { onSnapshot: vi.fn(), onAction: vi.fn(), onFailure })

    connection.start({ code: 'const idle = true', theme: 'dark', breakpoint: 'regular' })
    vi.advanceTimersByTime(GENERATIVE_UI_LIMITS.firstSnapshotTimeoutMs)

    expect(onFailure).toHaveBeenCalledWith(expect.objectContaining({ stage: 'worker_timeout' }))
    expect(port.close).toHaveBeenCalled()
  })

  it('coalesces callbacks beyond the rate limit and delivers the latest arguments', () => {
    vi.useFakeTimers()
    const port = { postMessage: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), close: vi.fn() }
    const connection = new GenerativeUiConnection(port, { onSnapshot: vi.fn(), onAction: vi.fn(), onFailure: vi.fn() })
    const callbacks = (): Array<{ id: string; args: unknown[] }> => port.postMessage.mock.calls.map(([message]) => message).filter(message => message.type === 'callback')

    const limit = GENERATIVE_UI_LIMITS.maxCallbacksPerSecond
    for (let value = 0; value < limit + 10; value++) connection.invoke('slider', [value])
    expect(callbacks()).toHaveLength(limit)

    vi.advanceTimersByTime(1000)
    expect(callbacks()).toHaveLength(limit + 1)
    expect(callbacks().at(-1)).toMatchObject({ id: 'slider', args: [limit + 9] })
  })
})
