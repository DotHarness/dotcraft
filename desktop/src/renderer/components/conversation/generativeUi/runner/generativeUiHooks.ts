import { isGenerativeUiJson, type GenerativeUiBreakpoint, type GenerativeUiJson, type GenerativeUiTheme } from '../generativeUiProtocol'

type Deps = readonly unknown[] | undefined

type HookSlot =
  | { kind: 'state'; value: unknown; set: (next: unknown) => void }
  | { kind: 'effect'; deps: Deps; mounted: boolean; cleanup?: () => void }
  | { kind: 'memo'; deps: Deps; value: unknown }
  | { kind: 'ref'; ref: { current: unknown } }
  | { kind: 'id'; value: string }

export interface GenerativeUiInstance {
  slots: HookSlot[]
  restored?: Map<number, unknown>
}

interface HookHost {
  scheduleRender: () => void
  globals: () => { theme: GenerativeUiTheme; breakpoint: GenerativeUiBreakpoint }
  active: () => boolean
}

export function createGenerativeUiHooks(host: HookHost) {
  let current: { instance: GenerativeUiInstance; index: number } | null = null
  let pendingEffects: Array<{ slot: Extract<HookSlot, { kind: 'effect' }>; effect: () => unknown }> = []
  let idSequence = 0

  function slot<K extends HookSlot['kind']>(kind: K, hook: string, create: (instance: GenerativeUiInstance, index: number) => Extract<HookSlot, { kind: K }>): Extract<HookSlot, { kind: K }> {
    if (!current) throw new Error(`DotCraft.${hook} can only be called while a component renders.`)
    const index = current.index++
    const existing = current.instance.slots[index]
    if (!existing) {
      const created = create(current.instance, index)
      current.instance.slots[index] = created
      return created
    }
    if (existing.kind !== kind) throw new Error('Hooks must be called in the same order on every render.')
    return existing as Extract<HookSlot, { kind: K }>
  }

  const changed = (previous: Deps, next: Deps): boolean =>
    previous === undefined || next === undefined || previous.length !== next.length || previous.some((value, index) => !Object.is(value, next[index]))

  const runCleanup = (target: Extract<HookSlot, { kind: 'effect' }>): void => {
    const cleanup = target.cleanup
    target.cleanup = undefined
    cleanup?.()
  }

  const api = {
    useState(initial?: unknown) {
      const state = slot('state', 'useState', (instance, index) => {
        const restored = instance.restored
        const created: Extract<HookSlot, { kind: 'state' }> = {
          kind: 'state',
          value: restored?.has(index) ? restored.get(index) : typeof initial === 'function' ? (initial as () => unknown)() : initial,
          set: next => {
            if (!host.active()) return
            const value = typeof next === 'function' ? (next as (previous: unknown) => unknown)(created.value) : next
            if (Object.is(value, created.value)) return
            created.value = value
            host.scheduleRender()
          }
        }
        return created
      })
      return [state.value, state.set] as const
    },
    useEffect(effect: () => unknown, deps?: readonly unknown[]) {
      if (typeof effect !== 'function') throw new TypeError('DotCraft.useEffect expects a function.')
      const state = slot('effect', 'useEffect', () => ({ kind: 'effect', deps: undefined, mounted: false }))
      if (state.mounted && !changed(state.deps, deps)) return
      state.mounted = true
      state.deps = deps
      pendingEffects.push({ slot: state, effect })
    },
    useMemo(compute: () => unknown, deps?: readonly unknown[]) {
      let fresh = false
      const state = slot('memo', 'useMemo', () => {
        fresh = true
        return { kind: 'memo', deps, value: compute() }
      })
      if (!fresh && changed(state.deps, deps)) {
        state.value = compute()
        state.deps = deps
      }
      return state.value
    },
    useRef(initial?: unknown) {
      return slot('ref', 'useRef', () => ({ kind: 'ref', ref: { current: initial } })).ref
    },
    useId() {
      return slot('id', 'useId', () => ({ kind: 'id', value: `dc-ui-${++idSequence}` })).value
    },
    useTheme() {
      return host.globals().theme
    },
    useBreakpoint() {
      return host.globals().breakpoint
    }
  }

  return {
    api,
    createInstance(restored?: Map<number, unknown>): GenerativeUiInstance {
      return { slots: [], restored }
    },
    restore(instance: GenerativeUiInstance, values: Map<number, unknown>): void {
      for (const [index, value] of values) {
        const target = instance.slots[index]
        if (target?.kind === 'state') target.value = value
      }
    },
    savedState(instance: GenerativeUiInstance): Record<string, GenerativeUiJson> | null {
      const saved: Record<string, GenerativeUiJson> = {}
      let empty = true
      instance.slots.forEach((target, index) => {
        if (target.kind !== 'state' || !isGenerativeUiJson(target.value)) return
        saved[index] = target.value
        empty = false
      })
      return empty ? null : saved
    },
    renderWith<T>(instance: GenerativeUiInstance, render: () => T): T {
      const previous = current
      current = { instance, index: 0 }
      try {
        return render()
      } finally {
        current = previous
      }
    },
    flushEffects(): void {
      const effects = pendingEffects
      pendingEffects = []
      for (const { slot: target, effect } of effects) {
        runCleanup(target)
        const cleanup = effect()
        if (typeof cleanup === 'function') target.cleanup = cleanup as () => void
      }
    },
    unmountAll(instances: Iterable<GenerativeUiInstance>): void {
      for (const instance of instances) {
        for (const target of instance.slots) {
          if (target.kind !== 'effect') continue
          try {
            runCleanup(target)
          } catch {
            continue
          }
        }
      }
    }
  }
}
