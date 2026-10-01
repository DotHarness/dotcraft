import { modifierMask, parseChord } from './browser-keys.mjs'

const LOAD_START_WINDOW_MS = 250
const LOAD_WAIT_MS = 3000
const BUTTON_MASKS = { left: 1, right: 2, middle: 4 }
const BUTTON_NAMES = new Map([[undefined, 'left'], [1, 'left'], [2, 'middle'], [3, 'right'], ['left', 'left'], ['middle', 'middle'], ['right', 'right']])
const NATIVE_CLIPBOARD_ERROR = 'Native clipboard shortcuts are not supported; use tab.clipboard to read or write the virtual clipboard.'
const CREDENTIAL_FIELD = {
  attributes: ['type', 'autocomplete', 'id', 'name', 'placeholder', 'aria-label', 'title'],
  pattern: /user[-_ ]?name|e[-_ ]?mail|one[-_ ]?time[-_ ]?code|password|passcode|passwd|\botp\b|\b(?:2fa|mfa)\b|phone|mobile|\btel\b/i.source
}

export function asObject(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : {}
}

export function asArray(value) {
  return Array.isArray(value) ? value : []
}

export function pointOf(options, name) {
  const { x, y } = asObject(options)
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    throw new Error(`InvalidArgument: ${name} requires finite x and y coordinates in an object, for example { x: 940, y: 444 }.`)
  }
  return { x, y }
}

export function mouseButton(value) {
  const button = BUTTON_NAMES.get(value)
  if (!button) throw new Error(`InvalidArgument: Unsupported mouse button: ${String(value)}. Use left, middle, or right.`)
  return button
}

export function keysOf(options, method) {
  if (!Array.isArray(options?.keys) || options.keys.length === 0) throw new Error(`${method} requires a non-empty keys array`)
  return options.keys
}

function mouse(tab, params) {
  return tab.cdp('Input.dispatchMouseEvent', params)
}

async function moveCursor(tab, point, waitForArrival = true) {
  try {
    const moved = tab.api.moveMouse({
      tabId: tab.numericId,
      x: point.x,
      y: point.y,
      ...(waitForArrival ? {} : { waitForArrival: false })
    })
    if (waitForArrival) await moved
    else moved.catch(() => {})
  } catch {}
}

function waitForPageLoad(tab) {
  return new Promise((resolve, reject) => {
    let started = false
    let timer
    const finish = (error) => {
      stop()
      clearTimeout(timer)
      if (error) reject(error)
      else resolve()
    }
    const stop = tab.api.addEventListener('onCDPEvent', (event) => {
      const { source, method, params } = event
      if (Number(source.tabId) !== tab.numericId) return
      if (method === 'Page.navigationBlocked') {
        finish(new Error(`NavigationFailed: ${params.errorDescription || 'The page navigation was blocked.'}`))
      } else if (method === 'Page.navigatedWithinDocument') {
        finish()
      } else if (method === 'Page.frameStartedLoading' || method === 'Page.frameNavigated') {
        if (started) return
        started = true
        clearTimeout(timer)
        timer = setTimeout(finish, LOAD_WAIT_MS)
      } else if ((method === 'Page.domContentEventFired' || method === 'Page.loadEventFired') && started) {
        finish()
      }
    })
    timer = setTimeout(finish, LOAD_START_WINDOW_MS)
  })
}

export async function movePointer(tab, point, modifiers = 0) {
  await moveCursor(tab, point)
  await mouse(tab, { type: 'mouseMoved', x: point.x, y: point.y, button: 'none', buttons: 0, modifiers })
}

export async function clickAt(tab, point, { clickCount = 1, button = 'left', modifiers = 0 } = {}) {
  await tab.cdp('Page.enable')
  const loaded = waitForPageLoad(tab)
  const settled = loaded.catch(() => {})
  try {
    await movePointer(tab, point, modifiers)
    for (let count = 1; count <= clickCount; count += 1) {
      await mouse(tab, { type: 'mousePressed', x: point.x, y: point.y, button, buttons: BUTTON_MASKS[button], clickCount: count, modifiers })
      await mouse(tab, { type: 'mouseReleased', x: point.x, y: point.y, button, buttons: 0, clickCount: count, modifiers })
    }
  } catch (error) {
    await settled
    throw error
  }
  await loaded
}

export async function dragPath(tab, points, modifiers = 0) {
  const [first, ...rest] = points
  if (!first) throw new Error('InvalidArgument: tab.cua.drag requires a non-empty path of { x, y } points.')
  const release = (point) => mouse(tab, { type: 'mouseReleased', x: point.x, y: point.y, button: 'left', buttons: 0, clickCount: 1, modifiers })
  await movePointer(tab, first, modifiers)
  await mouse(tab, { type: 'mousePressed', x: first.x, y: first.y, button: 'left', buttons: 1, clickCount: 1, modifiers })
  let last = first
  let released = false
  try {
    for (const point of rest) {
      last = point
      await moveCursor(tab, point, false)
      await mouse(tab, { type: 'mouseMoved', x: point.x, y: point.y, button: 'left', buttons: 1, modifiers })
    }
    released = true
    await release(last)
  } finally {
    if (!released) await release(last).catch(() => {})
  }
}

export async function scrollAt(tab, point, distance, modifiers = 0) {
  await movePointer(tab, point, modifiers)
  await mouse(tab, { type: 'mouseWheel', x: point.x, y: point.y, deltaX: distance.scrollX, deltaY: distance.scrollY, modifiers })
}

async function clipboardPage({ action, items, replace, credentialField }) {
  const focused = (root) => {
    const element = root.activeElement
    if (!element) return null
    return (element.shadowRoot ? focused(element.shadowRoot) : element.contentDocument ? focused(element.contentDocument) : null) ?? element
  }
  const target = focused(document) ?? document.body
  const view = target.ownerDocument.defaultView
  const field = target instanceof view.HTMLInputElement || target instanceof view.HTMLTextAreaElement
  const fire = (data) => target.dispatchEvent(new view.ClipboardEvent(action, { bubbles: true, cancelable: true, composed: true, clipboardData: data }))
  const notify = () => target.dispatchEvent(new view.InputEvent('input', { bubbles: true }))
  const credential = (element) => {
    if (!['input', 'textarea', 'select'].includes(element.localName)) return false
    if (element.getAttribute('type')?.toLowerCase() === 'hidden') return true
    return new RegExp(credentialField.pattern, 'i').test(credentialField.attributes.map((name) => element.getAttribute(name) ?? '').join(' '))
  }
  const scrub = (root) => {
    let changed = false
    for (const input of root.querySelectorAll('input[value]')) {
      if (!credential(input)) continue
      input.removeAttribute('value')
      changed = true
    }
    return changed
  }
  const data = new view.DataTransfer()
  if (action === 'paste') {
    let text = ''
    for (const entry of items.flatMap((item) => item.entries)) {
      if (typeof entry.text === 'string') {
        data.setData(entry.mimeType, entry.text)
        if (entry.mimeType === 'text/plain') text = entry.text
      } else {
        data.items.add(new view.File([Uint8Array.from(atob(entry.base64), (char) => char.charCodeAt(0))], 'clipboard', { type: entry.mimeType }))
      }
    }
    if (!fire(data) || target.disabled || target.readOnly || (!text && !replace)) return []
    if (field) {
      if (target.selectionStart == null) {
        Object.getOwnPropertyDescriptor(Object.getPrototypeOf(target), 'value').set.call(target, replace ? text : target.value + text)
      } else {
        target.setRangeText(text, target.selectionStart, target.selectionEnd, 'end')
      }
      notify()
    } else if (target.isContentEditable) {
      target.ownerDocument.execCommand('insertText', false, text)
    }
    return []
  }
  const cut = action === 'cut'
  if (credential(target)) return []
  if (!fire(data)) {
    const types = Array.from(data.types).filter((type) => type !== 'Files')
    if (types.length === 0) return []
    const entries = types.map((mimeType) => ({ mimeType, text: data.getData(mimeType) }))
    for (const entry of entries) {
      if (entry.mimeType !== 'text/html' || !/<input[\s/>]/i.test(entry.text)) continue
      const template = target.ownerDocument.createElement('template')
      template.innerHTML = entry.text
      if (scrub(template.content)) entry.text = template.innerHTML
    }
    return [{ entries }]
  }
  let text = ''
  let html = ''
  if (field) {
    const start = target.selectionStart
    const end = target.selectionEnd
    if (start == null || start === end) return []
    text = target.value.slice(start, end)
    if (cut && !target.readOnly && !target.disabled) {
      target.setRangeText('', start, end, 'end')
      notify()
    }
  } else {
    const selection = view.getSelection()
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return []
    const holder = target.ownerDocument.createElement('div')
    for (let index = 0; index < selection.rangeCount; index += 1) holder.append(selection.getRangeAt(index).cloneContents())
    scrub(holder)
    text = selection.toString()
    html = holder.innerHTML
    if (cut && target.isContentEditable) target.ownerDocument.execCommand('delete')
  }
  const entries = [{ mimeType: 'text/plain', text }]
  if (html) entries.push({ mimeType: 'text/html', text: html })
  return [{ entries }]
}

async function runClipboardPage(tab, args) {
  const result = await tab.eval(`(async () => {
    try {
      return { ok: true, value: await (${clipboardPage.toString()})(${JSON.stringify(args)}) };
    } catch (error) {
      return { ok: false, error: error instanceof Error ? error.message : String(error) };
    }
  })()`)
  if (result.ok !== true) throw new Error(result.error)
  return result.value
}

export async function pasteText(tab, text, replace = false) {
  await runClipboardPage(tab, { action: 'paste', items: [{ entries: [{ mimeType: 'text/plain', text }] }], replace })
}

async function runClipboardShortcut(tab, shortcut) {
  if (shortcut === 'copy' || shortcut === 'cut') {
    const copied = await runClipboardPage(tab, { action: shortcut, credentialField: CREDENTIAL_FIELD })
    if (copied.length > 0) await tab.clipboard.write(copied)
    return
  }
  const plain = shortcut === 'paste-plain-text'
  const items = (await tab.clipboard.read())
    .map((item) => ({ ...item, entries: item.entries.filter((entry) => !plain || entry.mimeType === 'text/plain') }))
    .filter((item) => item.entries.length > 0)
  if (items.length === 0) throw new Error('The virtual clipboard has no data to paste.')
  await runClipboardPage(tab, { action: 'paste', items, replace: false })
}

export async function pressKeys(tab, keys) {
  const { steps, shortcut } = parseChord(keys)
  if (shortcut === 'blocked') throw new Error(NATIVE_CLIPBOARD_ERROR)
  if (shortcut) {
    await runClipboardShortcut(tab, shortcut)
    return
  }
  const pressed = []
  try {
    for (const step of steps) {
      await tab.cdp('Input.dispatchKeyEvent', {
        type: step.text ? 'keyDown' : 'rawKeyDown',
        modifiers: step.down,
        windowsVirtualKeyCode: step.keyCode,
        code: step.code,
        ...(step.commands.length > 0 ? { commands: step.commands } : {}),
        key: step.key,
        text: step.text,
        unmodifiedText: step.text,
        location: step.location,
        isKeypad: step.keypad
      })
      pressed.push(step)
    }
  } finally {
    for (const step of pressed.reverse()) {
      await tab.cdp('Input.dispatchKeyEvent', {
        type: 'keyUp',
        modifiers: step.up,
        windowsVirtualKeyCode: step.keyCode,
        code: step.code,
        key: step.key,
        location: step.location,
        isKeypad: step.keypad
      })
    }
  }
}

export async function locatorClick(locator, clickCount, options) {
  const { force, button, modifiers } = asObject(options)
  const point = await locator.locatorOperation('prepareClick', { force: force === true })
  await clickAt(locator.tab, point, { clickCount, button: mouseButton(button), modifiers: modifierMask(modifiers) })
}

export async function locatorFill(locator, value) {
  if (value == null) throw new Error('locator.fill requires a value')
  const result = await locator.locatorOperation('fill', { value })
  if (result.needsInput) await pasteText(locator.tab, value, true)
}

export async function locatorType(locator, value) {
  if (value == null) throw new Error('locator.type requires a value')
  await locator.locatorOperation('focus', { requireEditable: true })
  await pasteText(locator.tab, value)
}

export async function locatorPress(locator, key) {
  if (key == null) throw new Error('locator.press requires a value')
  await locator.locatorOperation('focus', { requireEditable: false })
  await pressKeys(locator.tab, [key])
}

export async function locatorSetChecked(locator, checked, options) {
  const read = async () => asObject(await locator.locatorOperation('checked'))
  const before = await read()
  if (before.checked === checked) return
  if (before.isRadio && !checked) throw new Error('Cannot uncheck a radio button')
  await locatorClick(locator, 1, options)
  if ((await read()).checked !== checked) throw new Error(`Click did not change checked state to ${checked}`)
}
