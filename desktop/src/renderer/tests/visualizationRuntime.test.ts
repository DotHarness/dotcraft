// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import colorsSource from '../components/conversation/visualizationRuntime/colors.js?raw'
import hostActionsSource from '../components/conversation/visualizationRuntime/hostActions.js?raw'
import keysSource from '../components/conversation/visualizationRuntime/keys.js?raw'
import selectsSource from '../components/conversation/visualizationRuntime/selects.js?raw'

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
  install(hostActionsSource)
  install(keysSource)
  install(colorsSource)
  install(selectsSource)
  document.dispatchEvent(new Event('DOMContentLoaded'))
})

afterEach(() => {
  send.mockReset()
  request.mockReset()
  displayMode = 'inline'
  document.body.replaceChildren()
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
})
