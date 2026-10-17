export const EXA_KEY_MASK = '********'

export type ExaKeyCommit =
  | { kind: 'none' }
  | { kind: 'clear' }
  | { kind: 'set'; value: string }

export function isSecretStored(value: unknown): boolean {
  return typeof value === 'string' && value !== ''
}

export function resolveExaKeyCommit(draft: string | null, stored: boolean): ExaKeyCommit {
  if (draft === null) return { kind: 'none' }
  const value = draft.trim()
  if (value === EXA_KEY_MASK) return { kind: 'none' }
  if (value === '') return stored ? { kind: 'clear' } : { kind: 'none' }
  return { kind: 'set', value }
}
