interface DriverKeyPress {
  key: string
  modifiers: string[]
}

const MODIFIERS: Record<string, string> = {
  ctrl: 'ctrl',
  control: 'ctrl',
  control_l: 'ctrl',
  control_r: 'ctrl',
  shift: 'shift',
  shift_l: 'shift',
  shift_r: 'shift',
  alt: 'alt',
  alt_l: 'alt',
  alt_r: 'alt',
  option: 'alt'
}

const WINDOWS_LOGO = new Set(['win', 'windows', 'super', 'super_l', 'super_r', 'meta', 'meta_l', 'meta_r', 'cmd', 'command'])

const KEYS: Record<string, string> = {
  return: 'return',
  enter: 'return',
  kp_enter: 'return',
  tab: 'tab',
  iso_left_tab: 'tab',
  escape: 'escape',
  esc: 'escape',
  space: 'space',
  backspace: 'backspace',
  delete: 'delete',
  del: 'delete',
  kp_delete: 'delete',
  insert: 'insert',
  home: 'home',
  kp_home: 'home',
  end: 'end',
  kp_end: 'end',
  pageup: 'pageup',
  page_up: 'pageup',
  prior: 'pageup',
  kp_prior: 'pageup',
  pagedown: 'pagedown',
  page_down: 'pagedown',
  next: 'pagedown',
  kp_next: 'pagedown',
  up: 'up',
  kp_up: 'up',
  down: 'down',
  kp_down: 'down',
  left: 'left',
  kp_left: 'left',
  right: 'right',
  kp_right: 'right',
  capslock: 'capslock',
  caps_lock: 'capslock',
  numlock: 'numlock',
  num_lock: 'numlock',
  period: '.',
  comma: ',',
  minus: '-',
  plus: '+',
  equal: '=',
  slash: '/',
  backslash: '\\',
  semicolon: ';',
  apostrophe: "'",
  grave: '`',
  bracketleft: '[',
  bracketright: ']'
}

export function parseKeyChord(chord: string): DriverKeyPress {
  const parts = chord.split('+').map((part) => part.trim()).filter(Boolean)
  if (parts.length === 0) throw new Error('invalid_key: key must not be empty.')
  const modifiers: string[] = []
  let key: string | undefined
  parts.forEach((part, index) => {
    const lower = part.toLowerCase()
    if (WINDOWS_LOGO.has(lower)) {
      throw new Error('invalid_key: Windows-logo key combinations are not allowed.')
    }
    const modifier = MODIFIERS[lower]
    if (modifier && index < parts.length - 1) {
      if (!modifiers.includes(modifier)) modifiers.push(modifier)
      return
    }
    if (index !== parts.length - 1) throw new Error(`invalid_key: '${part}' is not a modifier.`)
    key = normalizeKeyName(part, lower)
  })
  return { key: key!, modifiers }
}

function normalizeKeyName(part: string, lower: string): string {
  const mapped = KEYS[lower] ?? MODIFIERS[lower]
  if (mapped) return mapped
  const functionKey = /^f([1-9]|1[0-2])$/.exec(lower)
  if (functionKey) return lower
  const keypadDigit = /^kp_(\d)$/.exec(lower)
  if (keypadDigit) return keypadDigit[1]
  if ([...part].length === 1) return part.length === 1 ? part.toLowerCase() : part
  throw new Error(`invalid_key: '${part}' is not a supported key name.`)
}
