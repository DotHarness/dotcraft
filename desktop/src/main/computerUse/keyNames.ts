interface DriverKeyPress {
  key: string
  modifiers: string[]
}

const MODIFIERS: Record<string, string> = { ctrl: 'ctrl', control: 'ctrl', shift: 'shift', alt: 'alt' }

const WINDOWS_LOGO = new Set(['win', 'windows', 'super', 'meta', 'cmd', 'command'])

const KEY_ALIASES: Record<string, string> = { enter: 'return', esc: 'escape' }

export function parseKeyChord(chord: string): DriverKeyPress {
  const names = chord.split('+').map((part) => part.trim().toLowerCase()).filter(Boolean)
  if (names.length === 0) throw new Error('invalid_key: key must not be empty.')
  if (names.some((name) => WINDOWS_LOGO.has(name))) {
    throw new Error('invalid_key: Windows-logo key combinations are not allowed.')
  }
  const modifiers: string[] = []
  for (const name of names.slice(0, -1)) {
    const modifier = MODIFIERS[name]
    if (!modifier) throw new Error(`invalid_key: '${name}' is not a modifier.`)
    if (!modifiers.includes(modifier)) modifiers.push(modifier)
  }
  const key = names[names.length - 1]
  return { key: KEY_ALIASES[key] ?? key, modifiers }
}
