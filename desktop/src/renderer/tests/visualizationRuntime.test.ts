// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import calendarSource from '../components/conversation/visualizationRuntime/calendar.js?raw'
import colorsSource from '../components/conversation/visualizationRuntime/colors.js?raw'
import hostActionsSource from '../components/conversation/visualizationRuntime/hostActions.js?raw'
import keysSource from '../components/conversation/visualizationRuntime/keys.js?raw'
import rangesSource from '../components/conversation/visualizationRuntime/ranges.js?raw'
import selectsSource from '../components/conversation/visualizationRuntime/selects.js?raw'
import tabsSource from '../components/conversation/visualizationRuntime/tabs.js?raw'
import tooltipsSource from '../components/conversation/visualizationRuntime/tooltips.js?raw'

const send = vi.fn()
const request = vi.fn()
const handlers = new Map<string, (params: unknown) => void>()
let displayMode = 'inline'
const host = {
  send,
  request,
  on: (method: string, handler: (params: unknown) => void) => handlers.set(method, handler),
  icon: () => null,
  displayMode: () => displayMode,
  downloadMethod: 'visualization/download',
  maxDownloadBytes: 64
}

function install(source: string): void {
  (new Function(`return (${source})`)() as (value: typeof host) => void)(host)
}

beforeAll(() => {
  URL.createObjectURL = vi.fn(() => `blob:null/${crypto.randomUUID()}`)
  URL.revokeObjectURL = vi.fn()
  Object.defineProperty(document.documentElement, 'clientWidth', { configurable: true, value: 320 })
  Object.defineProperty(document.documentElement, 'clientHeight', { configurable: true, value: 200 })
  install(hostActionsSource)
  install(tooltipsSource)
  install(tabsSource)
  install(calendarSource)
  install(keysSource)
  install(rangesSource)
  install(colorsSource)
  install(selectsSource)
  document.dispatchEvent(new Event('DOMContentLoaded'))
})

afterEach(() => {
  send.mockReset()
  request.mockReset()
  displayMode = 'inline'
  document.body.querySelectorAll(':not(#viz-tooltip)').forEach(node => node.remove())
})

describe('visualization tabs', () => {
  it('selects tabs by click and arrow keys and keeps panels in sync', () => {
    document.body.insertAdjacentHTML('beforeend', `
      <div class="nav nav-pills" role="tablist" aria-label="Platform">
        <button class="nav-link active" id="mac" role="tab" aria-controls="mac-panel" aria-selected="true" type="button">macOS</button>
        <button class="nav-link" id="win" role="tab" aria-controls="win-panel" aria-selected="false" type="button" disabled>Windows</button>
        <button class="nav-link" id="linux" role="tab" aria-controls="linux-panel" aria-selected="false" type="button">Linux</button>
      </div>
      <div id="mac-panel" role="tabpanel" aria-labelledby="mac">macOS</div>
      <div id="win-panel" role="tabpanel" aria-labelledby="win" hidden>Windows</div>
      <div id="linux-panel" role="tabpanel" aria-labelledby="linux" hidden>Linux</div>`)
    const [mac, , linux] = [...document.querySelectorAll<HTMLButtonElement>('[role="tab"]')]

    linux.click()
    expect(linux.getAttribute('aria-selected')).toBe('true')
    expect(linux.classList.contains('active')).toBe(true)
    expect(mac.getAttribute('aria-selected')).toBe('false')
    expect(document.getElementById('linux-panel')!.hidden).toBe(false)
    expect(document.getElementById('mac-panel')!.hidden).toBe(true)

    linux.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    expect(mac.getAttribute('aria-selected')).toBe('true')
    expect(document.activeElement).toBe(mac)
    expect(document.getElementById('mac-panel')!.hidden).toBe(false)

    mac.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }))
    expect(linux.getAttribute('aria-selected')).toBe('true')
    expect(document.getElementById('win-panel')!.hidden).toBe(true)
  })
})

describe('visualization tooltips', () => {
  it('shows a positioned tooltip for SVG marks and flips below when the top has no room', () => {
    document.body.insertAdjacentHTML('beforeend',
      '<svg><rect data-tooltip="North · 42 units" width="10" height="10"></rect></svg>')
    const mark = document.querySelector('rect')!
    mark.getBoundingClientRect = () => new DOMRect(20, 4, 10, 10)

    mark.dispatchEvent(new PointerEvent('pointerover', { bubbles: true, composed: true, pointerType: 'mouse' }))

    const tooltip = document.querySelector('[role="tooltip"]') as HTMLElement
    expect(tooltip.hidden).toBe(false)
    expect(tooltip.textContent).toBe('North · 42 units')
    expect(tooltip.dataset.placement).toBe('bottom')

    document.body.dispatchEvent(new PointerEvent('pointerover', { bubbles: true, pointerType: 'mouse' }))
    expect(tooltip.hidden).toBe(true)
  })

  it('pins a tooltip on tap and dismisses it on the next tap elsewhere', () => {
    document.body.insertAdjacentHTML('beforeend', '<button type="button" data-tooltip="Reset view">Reset</button><p>Elsewhere</p>')
    const button = document.querySelector('button')!

    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }))
    const tooltip = document.querySelector('[role="tooltip"]') as HTMLElement
    expect(tooltip.hidden).toBe(false)
    expect(button.getAttribute('aria-describedby')).toBe(tooltip.id)

    document.querySelector('p')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }))
    expect(tooltip.hidden).toBe(true)
    expect(button.hasAttribute('aria-describedby')).toBe(false)
  })
})

describe('visualization host actions', () => {
  it('reports only the first runtime error', () => {
    window.dispatchEvent(new ErrorEvent('error', { error: new TypeError('chart.draw is not a function'), message: 'x' }))
    window.dispatchEvent(new ErrorEvent('error', { error: new Error('second') }))

    expect(send).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledWith('visualization/error', { message: 'TypeError: chart.draw is not a function' })
  })

  it('relays web links instead of navigating the frame', () => {
    document.body.insertAdjacentHTML('beforeend', '<a href="https://example.com/docs">Docs</a><a href="javascript:alert(1)">Bad</a>')
    const [docs, bad] = [...document.querySelectorAll('a')]

    const docsClick = new MouseEvent('click', { bubbles: true, cancelable: true })
    docs.dispatchEvent(docsClick)
    const badClick = new MouseEvent('click', { bubbles: true, cancelable: true })
    bad.dispatchEvent(badClick)

    expect(docsClick.defaultPrevented).toBe(true)
    expect(badClick.defaultPrevented).toBe(true)
    expect(send).toHaveBeenCalledTimes(1)
    expect(send).toHaveBeenCalledWith('visualization/openLink', { url: 'https://example.com/docs' })
  })

  it('relays blob downloads from detached anchors and caps their size', async () => {
    const small = document.createElement('a')
    small.href = URL.createObjectURL(new Blob(['a,b\n1,2'], { type: 'text/csv' }))
    small.download = 'table.csv'
    small.click()
    await vi.waitFor(() => expect(send).toHaveBeenCalledWith('visualization/download', {
      name: 'table.csv',
      mimeType: 'text/csv',
      data: btoa('a,b\n1,2')
    }))

    const large = document.createElement('a')
    large.href = URL.createObjectURL(new Blob(['x'.repeat(65)]))
    large.download = 'large.txt'
    large.click()
    expect(send).toHaveBeenCalledWith('visualization/download', { name: 'large.txt', size: 65 })
  })
})

describe('viz-calendar', () => {
  it('lays out overlapping events in lanes and emits selection when interactive', () => {
    const calendar = document.createElement('viz-calendar') as HTMLElement & { events: unknown }
    calendar.setAttribute('date', '2026-07-14')
    calendar.setAttribute('start', '11:00')
    calendar.setAttribute('end', '14:00')
    calendar.setAttribute('interactive', '')
    const standup = { id: 'standup', title: 'Standup', start: '12:00', end: '12:30' }
    calendar.events = [standup, { title: 'Email block', start: '12:00', end: '12:30', tone: 'green', detail: 'Inbox' }]
    const selected = vi.fn()
    calendar.addEventListener('eventselect', event => selected((event as CustomEvent).detail))
    document.body.append(calendar)

    const events = [...calendar.shadowRoot!.querySelectorAll<HTMLButtonElement>('.event')]
    expect(events).toHaveLength(2)
    expect(new Set(events.map(event => event.style.left)).size).toBe(2)
    expect(events[1].getAttribute('data-tooltip')).toContain('Inbox')

    events[0].click()
    expect(selected).toHaveBeenCalledWith({ event: standup, index: 0 })
  })

  it('shows an inline error for invalid data and recovers when data is fixed', () => {
    const calendar = document.createElement('viz-calendar') as HTMLElement & { events: unknown }
    calendar.setAttribute('date', '2026-02-30')
    document.body.append(calendar)
    expect(calendar.shadowRoot!.querySelector('[role="alert"]')).not.toBeNull()

    calendar.setAttribute('date', '2026-02-28')
    calendar.events = []
    expect(calendar.shadowRoot!.querySelector('[role="alert"]')).toBeNull()
    expect(calendar.shadowRoot!.textContent).toContain('No events')
  })
})

describe('visualization fullscreen keys', () => {
  it('relays Escape and arrows to the host only when the view leaves them unhandled', async () => {
    document.body.insertAdjacentHTML('beforeend', '<button id="plain" type="button">Plain</button><input id="field"><button id="owned" type="button">Owned</button>')
    document.getElementById('owned')!.addEventListener('keydown', event => event.preventDefault())
    const press = (id: string, key: string): void => {
      document.getElementById(id)!.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
    }

    press('plain', 'Escape')
    await new Promise(resolve => setTimeout(resolve))
    expect(send).not.toHaveBeenCalled()

    displayMode = 'fullscreen'
    press('plain', 'ArrowRight')
    press('field', 'ArrowLeft')
    press('owned', 'Escape')
    press('field', 'Escape')
    await new Promise(resolve => setTimeout(resolve))

    expect(send.mock.calls).toEqual([
      ['visualization/key', { key: 'ArrowRight' }],
      ['visualization/key', { key: 'Escape' }]
    ])
  })
})

describe('visualization form controls', () => {
  it('keeps the range fill in step with the value however it changes', async () => {
    document.body.insertAdjacentHTML('beforeend', '<input class="form-range" type="range" min="10" max="30" value="15">')
    const range = document.querySelector<HTMLInputElement>('.form-range')!
    await new Promise(resolve => setTimeout(resolve))
    expect(range.style.getPropertyValue('--viz-range-progress')).toBe('25%')

    range.value = '30'
    expect(range.style.getPropertyValue('--viz-range-progress')).toBe('100%')
  })

  it('opens the host color picker instead of the native one and reports previews, commit, and cancel as input events', async () => {
    document.body.insertAdjacentHTML('beforeend', '<label>Accent <input type="color" value="#4f6cce"></label>')
    const input = document.querySelector<HTMLInputElement>('input[type=color]')!
    const events: string[] = []
    input.addEventListener('input', () => events.push(`input ${input.value}`))
    input.addEventListener('change', () => events.push(`change ${input.value}`))
    let settle!: { resolve: (value: unknown) => void; reject: (error: Error) => void }
    request.mockImplementation(() => new Promise((resolve, reject) => { settle = { resolve, reject } }))

    const click = new MouseEvent('click', { bubbles: true, cancelable: true })
    input.dispatchEvent(click)
    expect(click.defaultPrevented).toBe(true)
    expect(request).toHaveBeenCalledWith('visualization/pickColor', { color: '#4f6cce', label: 'Accent', rect: { x: 0, y: 0, width: 0, height: 0 } })

    handlers.get('visualization/colorPreview')!({ color: '#22AA55' })
    settle.resolve({ color: '#3366ff' })
    await new Promise(resolve => setTimeout(resolve))
    expect(events).toEqual(['input #22aa55', 'input #3366ff', 'change #3366ff'])

    input.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
    handlers.get('visualization/colorPreview')!({ color: '#000000' })
    settle.reject(new Error('Cancelled'))
    await new Promise(resolve => setTimeout(resolve))
    expect(input.value).toBe('#3366ff')
    expect(events.slice(3)).toEqual(['input #000000', 'input #3366ff'])
  })

  it('opens the host menu for a popup select and writes only a chosen option back', async () => {
    document.body.insertAdjacentHTML('beforeend', `<select aria-label="Channel"><option>Stable</option><optgroup label="Early"><option>Preview</option><option disabled>Nightly</option></optgroup><option hidden>Internal</option></select><select multiple><option>A</option></select>`)
    const [select, list] = [...document.querySelectorAll('select')]
    const events: string[] = []
    select.addEventListener('input', () => events.push(`input ${select.value}`))
    select.addEventListener('change', () => events.push(`change ${select.value}`))
    request.mockResolvedValueOnce({ index: 1 }).mockRejectedValueOnce(new Error('Cancelled'))

    const press = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    select.dispatchEvent(press)
    expect(press.defaultPrevented).toBe(true)
    expect(request).toHaveBeenCalledWith('visualization/selectMenu', {
      label: 'Channel',
      options: [
        { label: 'Stable', disabled: false },
        { label: 'Preview', disabled: false, group: 'Early' },
        { label: 'Nightly', disabled: true, group: 'Early' }
      ],
      selectedIndex: 0,
      rect: { x: 0, y: 0, width: 0, height: 0 }
    })
    await new Promise(resolve => setTimeout(resolve))
    expect(events).toEqual(['input Preview', 'change Preview'])

    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
    await new Promise(resolve => setTimeout(resolve))
    expect(select.value).toBe('Preview')
    expect(events).toHaveLength(2)

    const listPress = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    list.dispatchEvent(listPress)
    expect(listPress.defaultPrevented).toBe(false)
    expect(request).toHaveBeenCalledTimes(2)
  })

  it('steps a closed select with the arrow keys, skipping disabled options', () => {
    document.body.insertAdjacentHTML('beforeend', '<select><option>One</option><option disabled>Two</option><option>Three</option></select>')
    const select = document.querySelector('select')!
    const changes: string[] = []
    select.addEventListener('change', () => changes.push(select.value))

    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }))
    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }))
    select.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true, cancelable: true }))

    expect(changes).toEqual(['Three', 'One'])
    expect(request).not.toHaveBeenCalled()
  })
})
