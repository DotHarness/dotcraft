// @vitest-environment jsdom
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { buildVisualizationDocument } from '../components/conversation/visualizationSecurity'

interface Snapshot {
  groups: Array<{ id: string; label: string; variant: string | null; visible: boolean; controls: Array<Record<string, unknown>> }>
  variant: string | null
}
type TweakInstance = {
  supported: boolean
  addSlider: (object: object, property: string, options: object) => TweakInstance
  addColorPicker: (object: object, property: string, options?: object) => TweakInstance
  addToggle: (object: object, property: string, options?: object) => TweakInstance
  addSelect: (object: object, property: string, options: object) => TweakInstance
  dispose: () => void
}
type TweakClass = new (options: { container?: Element | null; onChange?: () => void }) => TweakInstance

const FRAGMENT = `
<div class="viz-carousel" aria-label="Player designs">
  <section data-variant="Minimal" aria-label="Minimal player"><input id="note"><div id="minimal" aria-label="Minimal player"></div></section>
  <section data-variant="Editorial" aria-label="Editorial player" hidden><div id="editorial" aria-label="Editorial player"></div></section>
  <section data-variant="Studio" hidden></section>
</div>
<div id="shared" aria-label="Shared theme"></div>`

const posted: Array<{ method: string; params: Record<string, unknown> }> = []

beforeAll(() => {
  const html = buildVisualizationDocument(FRAGMENT, 'dark', 'en', 'view-test', { widgetState: null, displayMode: 'inline', maxHeight: 10_000 }, {
    design: 'Design', designs: 'Designs', previousDesign: 'Previous design', nextDesign: 'Next design'
  })
  vi.stubGlobal('ResizeObserver', class { observe(): void {} disconnect(): void {} })
  vi.spyOn(window, 'postMessage').mockImplementation(message => { posted.push(message as (typeof posted)[number]) })
  document.body.innerHTML = FRAGMENT
  new Function(/<body><script>([\s\S]*?)<\/script>/.exec(html)![1])()
  document.dispatchEvent(new Event('DOMContentLoaded', { bubbles: true }))
})

beforeEach(() => { posted.length = 0 })

const Tweak = (): TweakClass => (globalThis as unknown as { Tweak: TweakClass }).Tweak
const variants = (): HTMLElement[] => [...document.querySelectorAll<HTMLElement>('[data-variant]')]
const lastSnapshot = (): Snapshot => posted.filter(message => message.method === 'visualization/designControls').at(-1)!.params as unknown as Snapshot
const flush = (): Promise<void> => Promise.resolve()

function fromHost(method: string, params: Record<string, unknown>): void {
  const event = new MessageEvent('message', { data: { method, params: { ...params, viewId: 'view-test' } } })
  Object.defineProperty(event, 'source', { value: window })
  window.dispatchEvent(event)
}

describe('visualization variant carousel', () => {
  it('switches designs by toggling hidden without replacing their DOM', () => {
    const [minimal, editorial, studio] = variants()
    const note = document.getElementById('note') as HTMLInputElement
    note.value = 'kept'
    const controls = document.querySelector('.viz-carousel-controls')!
    const [previous, next] = controls.querySelectorAll('button')
    const picker = controls.querySelector('select')!

    expect(controls.getAttribute('aria-label')).toBe('Player designs')
    expect(controls.textContent).toContain('1 / 3')
    expect(previous.disabled).toBe(true)
    expect([...picker.options].map(option => option.text)).toEqual(['Minimal', 'Editorial', 'Studio'])

    next.click()
    expect([minimal.hidden, editorial.hidden, studio.hidden]).toEqual([true, false, true])
    expect(controls.textContent).toContain('2 / 3')
    expect(picker.value).toBe('1')

    picker.value = '2'
    picker.dispatchEvent(new Event('change'))
    expect(studio.hidden).toBe(false)
    expect(next.disabled).toBe(true)

    controls.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }))
    expect(minimal.hidden).toBe(false)
    expect(document.getElementById('note')).toBe(note)
    expect(note.value).toBe('kept')
  })
})

describe('visualization design controls helper', () => {
  it('registers grouped controls with the host and resends visibility when the design changes', async () => {
    const minimalState = { radius: 18, accent: '#7C3AED' }
    const sharedState = { dense: false, tone: 'calm' }
    new (Tweak())({ container: document.getElementById('minimal') })
      .addSlider(minimalState, 'radius', { label: 'Corner radius', min: 0, max: 40, unit: 'px', reference: 'player.radius' })
      .addColorPicker(minimalState, 'accent', { label: 'Accent', reference: '--player accent' })
    new (Tweak())({ container: document.getElementById('editorial') }).addToggle(sharedState, 'dense', { label: 'Dense' })
    new (Tweak())({ container: document.getElementById('shared') }).addSelect(sharedState, 'tone', { label: 'Tone', options: ['calm', { label: 'Loud', value: 'loud' }] })
    await flush()

    const snapshot = lastSnapshot()
    expect(snapshot.variant).toBe('Minimal')
    expect(snapshot.groups.map(group => [group.label, group.variant, group.visible])).toEqual([
      ['Minimal player', 'Minimal', true],
      ['Editorial player', 'Editorial', false],
      ['Shared theme', null, true]
    ])
    expect(snapshot.groups[0].controls).toEqual([
      { id: expect.any(String), label: 'Corner radius', reference: 'player.radius', kind: 'slider', min: 0, max: 40, step: 1, unit: 'px', initial: 18 },
      { id: expect.any(String), label: 'Accent', kind: 'color', initial: '#7c3aed' }
    ])
    expect(snapshot.groups[2].controls[0]).toMatchObject({ options: [{ label: 'calm', value: 'calm' }, { label: 'Loud', value: 'loud' }] })

    ;(document.querySelectorAll<HTMLButtonElement>('.viz-carousel-controls button')[1]).click()
    await flush()
    expect(lastSnapshot().variant).toBe('Editorial')
    expect(lastSnapshot().groups.map(group => group.visible)).toEqual([false, true, true])
  })

  it('updates the bound property before calling onChange and ignores invalid host values', async () => {
    const state = { radius: 10, on: false }
    const seen: unknown[] = []
    const tweak = new (Tweak())({ container: null, onChange: () => seen.push({ ...state }) })
    tweak.addSlider(state, 'radius', { min: 0, max: 20, step: 5 }).addToggle(state, 'on')
    await flush()
    const [radius, on] = lastSnapshot().groups.at(-1)!.controls.map(control => control.id as string)

    fromHost('visualization/designControls/set', { values: { [radius]: 13, [on]: 'yes' } })
    expect(seen).toEqual([{ radius: 15, on: false }])

    fromHost('visualization/designControls/set', { values: { [radius]: 99, [on]: true } })
    expect(state).toEqual({ radius: 20, on: true })
    tweak.dispose()
  })

  it('enforces control, option, and instance limits', async () => {
    const state: Record<string, number | string> = { choice: 'o0' }
    const tweak = new (Tweak())({ container: null })
    for (let index = 0; index < 14; index++) {
      state[`v${index}`] = 1
      tweak.addSlider(state, `v${index}`, { min: 0, max: 2 })
    }
    const options = Array.from({ length: 14 }, (_, index) => `o${index}`)
    const selects = new (Tweak())({ container: null }).addSelect(state, 'choice', { options })
    await flush()

    const groups = lastSnapshot().groups
    expect(groups.at(-2)!.controls).toHaveLength(12)
    expect((groups.at(-1)!.controls[0].options as unknown[])).toHaveLength(12)
    expect(selects.supported).toBe(true)

    const extra = Array.from({ length: 16 }, () => new (Tweak())({ container: null }))
    expect(extra.at(-1)!.supported).toBe(false)
    extra.at(-1)!.addToggle({ on: true }, 'on')
    expect(extra.at(-1)!.supported).toBe(false)
  })
})
