import { platform as hostPlatform } from 'node:os'

const MODIFIER_BITS = { Alt: 1, Control: 2, Meta: 4, Shift: 8 }
const KEYPAD = 3

const ALIASES = new Map(Object.entries({
  alt: 'Alt', option: 'Alt', control: 'Control', controlormeta: 'ControlOrMeta', ctrl: 'ControlOrMeta',
  cmd: 'Meta', command: 'Meta', meta: 'Meta', win: 'Meta', super: 'Meta', shift: 'Shift',
  capslock: 'CapsLock', shiftlock: 'CapsLock', esc: 'Escape', escape: 'Escape', enter: 'Enter', return: 'Enter',
  linefeed: 'Enter', backspace: 'Backspace', delete: 'Delete', del: 'Delete', tab: 'Tab', space: 'Space',
  left: 'ArrowLeft', arrowleft: 'ArrowLeft', right: 'ArrowRight', arrowright: 'ArrowRight', up: 'ArrowUp',
  arrowup: 'ArrowUp', down: 'ArrowDown', arrowdown: 'ArrowDown', home: 'Home', begin: 'Home', end: 'End',
  insert: 'Insert', pageup: 'PageUp', prior: 'PageUp', pagedown: 'PageDown', next: 'PageDown', menu: 'ContextMenu',
  exclam: '!', quotedbl: '"', numbersign: '#', dollar: '$', percent: '%', ampersand: '&', apostrophe: "'",
  parenleft: '(', parenright: ')', asterisk: '*', plus: '+', comma: ',', minus: '-', period: '.', slash: '/',
  colon: ':', semicolon: ';', less: '<', equal: '=', greater: '>', question: '?', at: '@', bracketleft: '[',
  backslash: '\\', bracketright: ']', asciicircum: '^', underscore: '_', grave: '`', braceleft: '{', bar: '|',
  braceright: '}', asciitilde: '~', kpenter: 'NumpadEnter', kpmultiply: 'NumpadMultiply', kpadd: 'NumpadAdd',
  kpsubtract: 'NumpadSubtract', kpdecimal: 'NumpadDecimal', kpdivide: 'NumpadDivide',
  ...Object.fromEntries(Array.from({ length: 10 }, (_, i) => [`kp${i}`, `Numpad${i}`]))
}))

const LAYOUT = new Map()

function define(code, key, keyCode, { shift, text = key.length === 1 ? key : '', location = 0 } = {}) {
  const entry = { key, code, keyCode, text, location }
  if (shift) entry.shifted = { ...entry, key: shift, text: shift }
  LAYOUT.set(code, entry)
}

for (const [code, key, keyCode, shift] of [
  ['Backquote', '`', 192, '~'], ['Minus', '-', 189, '_'], ['Equal', '=', 187, '+'], ['Backslash', '\\', 220, '|'],
  ['BracketLeft', '[', 219, '{'], ['BracketRight', ']', 221, '}'], ['Semicolon', ';', 186, ':'],
  ['Quote', "'", 222, '"'], ['Comma', ',', 188, '<'], ['Period', '.', 190, '>'], ['Slash', '/', 191, '?'],
  ['Space', ' ', 32]
]) define(code, key, keyCode, { shift })
for (let i = 0; i < 10; i += 1) define(`Digit${i}`, String(i), 48 + i, { shift: ')!@#$%^&*('[i] })
for (let i = 0; i < 26; i += 1) {
  const letter = String.fromCharCode(97 + i)
  define(`Key${letter.toUpperCase()}`, letter, 65 + i, { shift: letter.toUpperCase() })
}
for (let i = 1; i <= 20; i += 1) define(`F${i}`, `F${i}`, 111 + i)
for (const [name, keyCode] of Object.entries({
  Escape: 27, Backspace: 8, Tab: 9, CapsLock: 20, PageUp: 33, PageDown: 34, End: 35, Home: 36, ArrowLeft: 37,
  ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Insert: 45, Delete: 46, ContextMenu: 93, AltGraph: 225
})) define(name, name, keyCode)
define('Enter', 'Enter', 13, { text: '\r' })
for (const [name, keyCode, left, right] of [['Shift', 16], ['Control', 17], ['Alt', 18], ['Meta', 91, 91, 92]]) {
  define(`${name}Left`, name, left ?? keyCode, { location: 1 })
  define(`${name}Right`, name, right ?? keyCode, { location: 2 })
}
define('NumpadEnter', 'Enter', 13, { text: '\r', location: KEYPAD })
for (let i = 0; i < 10; i += 1) define(`Numpad${i}`, String(i), 96 + i, { location: KEYPAD })
for (const [name, key, keyCode] of [['Multiply', '*', 106], ['Add', '+', 107], ['Subtract', '-', 109], ['Decimal', '.', 110], ['Divide', '/', 111]]) {
  define(`Numpad${name}`, key, keyCode, { location: KEYPAD })
}

const LOOKUP = new Map()
for (const entry of LAYOUT.values()) {
  LOOKUP.set(entry.code, entry)
  if (entry.location) continue
  if (entry.key.length === 1) LOOKUP.set(entry.key, entry)
  if (entry.shifted) LOOKUP.set(entry.shifted.key, { ...entry.shifted, shifted: undefined })
}
LOOKUP.set('\n', LAYOUT.get('Enter'))
LOOKUP.set('\r', LAYOUT.get('Enter'))
for (const name of ['Alt', 'Control', 'Meta', 'Shift']) LOOKUP.set(name, LAYOUT.get(`${name}Left`))

function splitChord(text) {
  const tokens = []
  let token = ''
  for (const char of text) {
    if (char === '+' && token) {
      tokens.push(token)
      token = ''
    } else {
      token += char
    }
  }
  if (token) tokens.push(token)
  return tokens
}

function canonical(token) {
  return LOOKUP.has(token) ? token : ALIASES.get(token.toLowerCase().replaceAll('_', '')) ?? token
}

function lookup(token, platform) {
  const entry = LOOKUP.get(token === 'ControlOrMeta' ? (platform === 'darwin' ? 'Meta' : 'Control') : token)
  if (!entry) throw new Error(`Unknown key: "${token}"`)
  return entry
}

function maskOf(modifiers) {
  let mask = 0
  for (const name of modifiers) mask |= MODIFIER_BITS[name] ?? 0
  return mask
}

export function modifierMask(keys, platform = hostPlatform()) {
  const primary = platform === 'darwin' ? 'Meta' : 'Control'
  return maskOf((Array.isArray(keys) ? keys : []).map((key) => key === 'ControlOrMeta' ? primary : key))
}

function clipboardAction(code, modifiers) {
  if (modifiers.size === 1) {
    if (code === 'Insert' && modifiers.has('Control')) return 'copy'
    if (code === 'Insert' && modifiers.has('Shift')) return 'paste'
    if (code === 'Delete' && modifiers.has('Shift')) return 'cut'
  }
  const primary = modifiers.has('Meta') !== modifiers.has('Control')
  if (!primary || !(modifiers.size === 1 || (modifiers.size === 2 && modifiers.has('Shift')))) return undefined
  const shift = modifiers.has('Shift')
  if (code === 'KeyC') return shift ? undefined : 'copy'
  if (code === 'KeyV') return shift ? 'paste-plain-text' : 'paste'
  if (code === 'KeyX') return shift ? undefined : 'cut'
  return undefined
}

function isNativeClipboard(code, modifiers) {
  const primary = modifiers.has('Meta') || modifiers.has('Control')
  if ((code === 'KeyC' || code === 'KeyX') && modifiers.size === 2 && modifiers.has('Shift') && primary) return false
  return (primary && (code === 'KeyC' || code === 'KeyV' || code === 'KeyX')) ||
    (code === 'Insert' && (modifiers.has('Control') || modifiers.has('Shift'))) ||
    (code === 'Delete' && modifiers.has('Shift'))
}

export function parseChord(keys, platform = hostPlatform()) {
  const tokens = (Array.isArray(keys) ? keys : [keys]).flatMap((key) => splitChord(String(key))).map(canonical)
  if (tokens.length === 0) throw new Error('keypress requires at least one key')
  const primary = platform === 'darwin' ? 'Meta' : 'Control'
  const pressed = new Set()
  const codes = []
  let blocked = false
  const steps = tokens.map((token) => {
    const base = lookup(token, platform)
    const entry = pressed.has('Shift') && base.shifted ? base.shifted : base
    const up = maskOf(pressed)
    if (MODIFIER_BITS[entry.key]) {
      pressed.add(entry.key)
    } else {
      codes.push(entry.code)
      blocked ||= isNativeClipboard(entry.code, pressed)
    }
    const typed = pressed.size === 0 || (pressed.size === 1 && pressed.has('Shift'))
    return {
      key: entry.key,
      code: entry.code,
      keyCode: entry.keyCode,
      location: entry.location,
      keypad: entry.location === KEYPAD,
      text: typed ? entry.text : '',
      commands: entry.code === 'KeyA' && pressed.size === 1 && pressed.has(primary) ? ['selectAll'] : [],
      down: maskOf(pressed),
      up
    }
  })
  let shortcut
  if (codes.length === 1) shortcut = clipboardAction(codes[0], pressed) ?? (isNativeClipboard(codes[0], pressed) ? 'blocked' : undefined)
  shortcut ??= blocked ? 'blocked' : undefined
  return { steps, shortcut }
}
