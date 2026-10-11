import {
  GENERATIVE_UI_COMPONENT_NAMES,
  GENERATIVE_UI_LIMITS,
  GENERATIVE_UI_PROTOCOL_VERSION,
  generativeUiActionLimit,
  readGenerativeUiSavedState,
  type GenerativeUiActionKind,
  type GenerativeUiBreakpoint,
  type GenerativeUiElementNode,
  type GenerativeUiFailureStage,
  type GenerativeUiHostMessage,
  type GenerativeUiJson,
  type GenerativeUiNode,
  type GenerativeUiRunnerMessage,
  type GenerativeUiSavedState,
  type GenerativeUiTheme
} from '../generativeUiProtocol'
import { compileGenerativeUiProgram, GENERATIVE_UI_ELEMENT_FACTORY, GENERATIVE_UI_FRAGMENT } from './generativeUiCompiler'
import { createGenerativeUiHooks, type GenerativeUiInstance } from './generativeUiHooks'

const ELEMENT = Symbol('dotcraft.element')
const FRAGMENT = Symbol('dotcraft.fragment')

type Component = (props: Record<string, unknown>) => unknown

interface RuntimeElement {
  [ELEMENT]: true
  type: string | Component | typeof FRAGMENT
  key: string | null
  props: Record<string, unknown>
  children: unknown[]
}

class GenerativeUiRuntimeError extends Error {
  constructor(readonly stage: GenerativeUiFailureStage, message: string) {
    super(message)
  }
}

export interface GenerativeUiRuntime {
  receive: (message: unknown) => void
  reportUncaught: (error: unknown) => void
}

type Post = (message: GenerativeUiRunnerMessage) => void

const isElement = (value: unknown): value is RuntimeElement =>
  typeof value === 'object' && value !== null && (value as Partial<RuntimeElement>)[ELEMENT] === true

export function createGenerativeUiRuntime(post: Post): GenerativeUiRuntime {
  let started = false
  let stopped = false
  let root: RuntimeElement | null = null
  let globals: { theme: GenerativeUiTheme; breakpoint: GenerativeUiBreakpoint } = { theme: 'dark', breakpoint: 'regular' }
  let callbacks = new Map<string, (...args: unknown[]) => unknown>()
  let lastSnapshotJson = ''
  let version = 0
  let renderScheduled = false
  let synchronousRenders = 0
  let probeLine = 1
  let lastCallbackAt = Number.NEGATIVE_INFINITY
  let savedStateJson: string | null = null
  const restoredState = new Map<string, Map<number, unknown>>()
  const instances = new Map<string, GenerativeUiInstance>()
  const anonymousIds = new WeakMap<Component, number>()
  let anonymousSequence = 0
  const hooks = createGenerativeUiHooks({
    scheduleRender,
    globals: () => globals,
    active: () => !stopped
  })

  const send = (message: Record<string, unknown>): void => {
    post({ protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION, ...message } as GenerativeUiRunnerMessage)
  }

  function fail(stage: GenerativeUiFailureStage, error: unknown, location?: { line?: number; column?: number }): void {
    if (stopped) return
    stopped = true
    const message = (error instanceof Error ? `${error.name === 'Error' ? '' : `${error.name}: `}${error.message}` : String(error))
      .slice(0, GENERATIVE_UI_LIMITS.maxErrorMessageLength)
    const line = location?.line ?? programLine(error)
    send({ type: 'error', stage, message, line, column: location?.column })
    hooks.unmountAll(instances.values())
    instances.clear()
  }

  function failFrom(defaultStage: GenerativeUiFailureStage, error: unknown): void {
    fail(error instanceof GenerativeUiRuntimeError ? error.stage : defaultStage, error)
  }

  function programLine(error: unknown): number | undefined {
    const stackLine = evalStackLine(error)
    if (stackLine === undefined) return undefined
    const line = stackLine - probeLine + 1
    return line >= 1 ? line : undefined
  }

  function scheduleRender(): void {
    if (stopped || renderScheduled || !root) return
    renderScheduled = true
    queueMicrotask(() => {
      renderScheduled = false
      renderPass()
    })
  }

  function renderPass(): void {
    if (stopped || !root) return
    if (synchronousRenders++ === 0) setTimeout(() => { synchronousRenders = 0 }, 0)
    if (synchronousRenders > GENERATIVE_UI_LIMITS.maxSynchronousRenders) {
      fail('runtime_error', new Error('Too many re-renders. A state update runs on every render.'))
      return
    }
    const pass = { nodes: 0, visited: new Set<string>(), callbacks: new Map<string, (...args: unknown[]) => unknown>() }
    let tree: GenerativeUiNode[]
    try {
      tree = []
      renderValue(root, 'r', 0, tree, pass)
    } catch (error) {
      failFrom('runtime_error', error)
      return
    }
    for (const [key, instance] of instances) {
      if (pass.visited.has(key)) continue
      hooks.unmountAll([instance])
      instances.delete(key)
    }
    callbacks = pass.callbacks
    const json = JSON.stringify(tree)
    if (json !== lastSnapshotJson) {
      if (new TextEncoder().encode(json).byteLength > GENERATIVE_UI_LIMITS.maxSnapshotBytes) {
        fail('payload_too_large', new Error(`The rendered tree is larger than ${GENERATIVE_UI_LIMITS.maxSnapshotBytes / 1024} KB.`))
        return
      }
      lastSnapshotJson = json
      send({ type: 'snapshot', version: ++version, tree })
    }
    postSavedState()
    queueMicrotask(() => {
      if (stopped) return
      try {
        hooks.flushEffects()
      } catch (error) {
        failFrom('runtime_error', error)
      }
    })
  }

  function postSavedState(): void {
    const saved: GenerativeUiSavedState = {}
    for (const [key, instance] of instances) {
      const slots = hooks.savedState(instance)
      if (slots) saved[key] = slots
    }
    const json = JSON.stringify(saved)
    const baseline = savedStateJson === null
    if (json === savedStateJson) return
    savedStateJson = json
    if (baseline || Date.now() - lastCallbackAt > GENERATIVE_UI_LIMITS.actionWindowMs) return
    if (new TextEncoder().encode(json).byteLength > GENERATIVE_UI_LIMITS.maxSavedStateBytes) return
    send({ type: 'state', state: saved })
  }

  interface Pass { nodes: number; visited: Set<string>; callbacks: Map<string, (...args: unknown[]) => unknown> }

  function renderValue(value: unknown, path: string, depth: number, out: GenerativeUiNode[], pass: Pass): void {
    if (value === null || value === undefined || typeof value === 'boolean') return
    if (typeof value === 'string') { out.push(value); return }
    if (typeof value === 'number' || typeof value === 'bigint') { out.push(String(value)); return }
    if (Array.isArray(value)) {
      value.forEach((item, index) => renderValue(item, childPath(path, item, index), depth, out, pass))
      return
    }
    if (typeof value === 'function') throw new TypeError('A function is not a valid child. Call it or render it as <Component />.')
    if (!isElement(value)) throw new TypeError('An object is not a valid child. Render its fields as text instead.')
    const { type } = value
    if (type === FRAGMENT) {
      renderValue(value.children, path, depth, out, pass)
      return
    }
    if (typeof type === 'function') {
      const key = `${path}#${componentName(type)}`
      pass.visited.add(key)
      let instance = instances.get(key)
      if (!instance) {
        instance = hooks.createInstance(restoredState.get(key))
        restoredState.delete(key)
        instances.set(key, instance)
      }
      const props = value.children.length > 0
        ? { ...value.props, children: value.children.length === 1 ? value.children[0] : value.children }
        : value.props
      renderValue(hooks.renderWith(instance, () => type(props)), key, depth, out, pass)
      return
    }
    if (typeof type !== 'string') throw new TypeError('Element type must be a built-in component or a function component.')
    if (depth + 1 > GENERATIVE_UI_LIMITS.maxDepth) {
      throw new GenerativeUiRuntimeError('payload_too_large', `The rendered tree is deeper than ${GENERATIVE_UI_LIMITS.maxDepth} levels.`)
    }
    if (++pass.nodes > GENERATIVE_UI_LIMITS.maxNodes) {
      throw new GenerativeUiRuntimeError('payload_too_large', `The rendered tree has more than ${GENERATIVE_UI_LIMITS.maxNodes} components.`)
    }
    const node: GenerativeUiElementNode = { type, props: {}, children: [] }
    if (value.key !== null) node.key = value.key
    for (const [name, prop] of Object.entries(value.props)) {
      if (name === 'children') continue
      const serialized = serialize(prop, `${path}:${name}`, pass)
      if (serialized !== undefined) node.props[name] = serialized
    }
    renderValue(value.children.length > 0 ? value.children : value.props.children, path, depth + 1, node.children, pass)
    out.push(node)
  }

  function serialize(value: unknown, path: string, pass: Pass): GenerativeUiJson | undefined {
    if (value === undefined) return undefined
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return value
    if (typeof value === 'number') return Number.isFinite(value) ? value : null
    if (typeof value === 'function') {
      pass.callbacks.set(path, value as (...args: unknown[]) => unknown)
      return { $callback: path }
    }
    if (isElement(value)) throw new TypeError('Components can only be passed as children, not as props.')
    if (Array.isArray(value)) return value.map((item, index) => serialize(item, `${path}.${index}`, pass) ?? null)
    if (typeof value === 'object' && Object.getPrototypeOf(value) === Object.prototype) {
      const result: Record<string, GenerativeUiJson> = {}
      for (const [key, item] of Object.entries(value)) {
        const serialized = serialize(item, `${path}.${key}`, pass)
        if (serialized !== undefined) result[key] = serialized
      }
      return result
    }
    throw new TypeError('Props must be plain JSON values or functions.')
  }

  function componentName(component: Component): string {
    if (component.name) return component.name
    let id = anonymousIds.get(component)
    if (id === undefined) {
      id = ++anonymousSequence
      anonymousIds.set(component, id)
    }
    return `~${id}`
  }

  function createElement(type: RuntimeElement['type'], props: Record<string, unknown> | null, ...children: unknown[]): RuntimeElement {
    const { key, ...rest } = props ?? {}
    return { [ELEMENT]: true, type, key: key === undefined || key === null ? null : String(key), props: rest, children }
  }

  function requestAction(kind: GenerativeUiActionKind, field: 'text' | 'url', value: unknown): void {
    if (typeof value !== 'string' || value.length === 0) throw new TypeError(`DotCraft.${kind} needs a non-empty string.`)
    if (field === 'url') {
      if (!/^https?:\/\//i.test(value)) throw new TypeError('DotCraft.openUrl only opens http and https links.')
      if (value.length > GENERATIVE_UI_LIMITS.maxUrlLength) throw new RangeError(`DotCraft.openUrl accepts up to ${GENERATIVE_UI_LIMITS.maxUrlLength} characters.`)
    } else if (new TextEncoder().encode(value).byteLength > generativeUiActionLimit(kind)) {
      throw new RangeError(`DotCraft.${kind} accepts up to ${generativeUiActionLimit(kind) / 1024} KB.`)
    }
    if (!stopped) send({ type: 'action', kind, payload: { [field]: value } })
  }

  function start(message: Extract<GenerativeUiHostMessage, { type: 'start' }>): void {
    globals = { theme: message.theme, breakpoint: message.breakpoint }
    for (const [key, slots] of Object.entries(message.initialState ?? {})) restoredState.set(key, slotValues(slots))
    const compiled = compileGenerativeUiProgram(String(message.code))
    if (!compiled.ok) {
      fail('compile_error', new Error(compiled.message), { line: compiled.line, column: compiled.column })
      return
    }
    const api = Object.freeze({
      ...hooks.api,
      render(element: unknown) {
        if (!isElement(element)) throw new TypeError('DotCraft.render expects an element, such as DotCraft.render(<App />).')
        root = element
        scheduleRender()
      },
      sendMessage: (text: unknown) => requestAction('sendMessage', 'text', text),
      prefillComposer: (text: unknown) => requestAction('prefillComposer', 'text', text),
      copy: (text: unknown) => requestAction('copy', 'text', text),
      openUrl: (url: unknown) => requestAction('openUrl', 'url', url)
    })
    const scope: Record<string, unknown> = {
      DotCraft: api,
      [GENERATIVE_UI_ELEMENT_FACTORY]: createElement,
      [GENERATIVE_UI_FRAGMENT]: FRAGMENT
    }
    for (const name of GENERATIVE_UI_COMPONENT_NAMES) scope[name] = name
    const names = Object.keys(scope)
    const prefix = '"use strict";\n'
    try {
      new Function(...names, `${prefix}throw new Error()`)()
    } catch (probe) {
      probeLine = evalStackLine(probe) ?? probeLine
    }
    try {
      new Function(...names, `${prefix}${compiled.code}`)(...names.map(name => scope[name]))
    } catch (error) {
      failFrom('runtime_error', error)
    }
  }

  function restore(state: GenerativeUiSavedState): void {
    for (const [key, slots] of Object.entries(state)) {
      const values = slotValues(slots)
      const instance = instances.get(key)
      if (!instance) {
        restoredState.set(key, values)
        continue
      }
      hooks.restore(instance, values)
    }
    lastCallbackAt = Number.NEGATIVE_INFINITY
    scheduleRender()
  }

  function invoke(id: string, args: unknown[]): void {
    const callback = callbacks.get(id)
    if (!callback) return
    lastCallbackAt = Date.now()
    try {
      const result = callback(...args)
      if (result instanceof Promise) result.catch(error => failFrom('callback_error', error))
    } catch (error) {
      failFrom('callback_error', error)
    }
  }

  return {
    reportUncaught: error => failFrom('runtime_error', error),
    receive(raw: unknown) {
      if (stopped) return
      const message = raw as Partial<GenerativeUiHostMessage> | null
      if (!message || typeof message !== 'object') return
      if (message.protocolVersion !== GENERATIVE_UI_PROTOCOL_VERSION) {
        fail('protocol_mismatch', new Error(`Expected protocol version ${GENERATIVE_UI_PROTOCOL_VERSION}.`))
        return
      }
      if (message.type === 'start') {
        if (started) return
        started = true
        start(message as Extract<GenerativeUiHostMessage, { type: 'start' }>)
      } else if (message.type === 'callback' && typeof message.id === 'string' && Array.isArray(message.args)) {
        invoke(message.id, message.args)
      } else if (message.type === 'restore' && started) {
        const state = readGenerativeUiSavedState(message.state)
        if (state) restore(state)
      } else if (message.type === 'globals') {
        globals = { theme: message.theme === 'light' ? 'light' : 'dark', breakpoint: message.breakpoint ?? globals.breakpoint }
        scheduleRender()
      } else if (message.type === 'dispose') {
        stopped = true
        hooks.unmountAll(instances.values())
        instances.clear()
      }
    }
  }
}

function evalStackLine(error: unknown): number | undefined {
  const stack = error instanceof Error ? error.stack ?? '' : ''
  const match = /<anonymous>:(\d+):\d+/.exec(stack) ?? /Function:(\d+):\d+/.exec(stack)
  return match ? Number(match[1]) : undefined
}

function slotValues(slots: Record<string, GenerativeUiJson>): Map<number, unknown> {
  return new Map(Object.entries(slots).map(([index, value]) => [Number(index), structuredClone(value)]))
}

function childPath(path: string, item: unknown, index: number): string {
  return isElement(item) && item.key !== null ? `${path}.k${item.key}` : `${path}.${index}`
}
