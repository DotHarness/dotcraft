import * as path from 'path'
import { CUSTOM_TARGET_PREFIX } from '../../shared/openTargets'
import type { CommandLookup } from './detection'

export type CustomTargetInput = 'path' | 'jsonArgument' | 'jsonStdin'

export interface CustomOpenTarget {
  id: string
  label: string
  icon?: string
  command: string
  args: string[]
  input: CustomTargetInput
}

const HANDLER_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/

const INPUTS: Record<string, CustomTargetInput> = {
  path: 'path',
  jsonargument: 'jsonArgument',
  jsonstdin: 'jsonStdin'
}

function field(record: Record<string, unknown>, key: string): unknown {
  const lowered = key.toLowerCase()
  const match = Object.keys(record).find((candidate) => candidate.toLowerCase() === lowered)
  return match === undefined ? undefined : record[match]
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function nonEmptyString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value.trim() : null
}

export async function readCustomOpenTargets(
  config: Record<string, unknown> | null,
  lookup: CommandLookup,
  warn: (message: string) => void
): Promise<CustomOpenTarget[]> {
  const handlers = asRecord(field(asRecord(field(config ?? {}, 'Desktop')) ?? {}, 'CustomFileHandlers'))
  if (!handlers) return []
  const result: CustomOpenTarget[] = []
  for (const [handlerId, value] of Object.entries(handlers)) {
    const skip = (reason: string): void => warn(`[open-targets] skipped custom file handler "${handlerId}": ${reason}`)
    if (!HANDLER_ID_PATTERN.test(handlerId)) {
      skip('invalid id')
      continue
    }
    const entry = asRecord(value)
    if (!entry) {
      skip('not an object')
      continue
    }
    const label = nonEmptyString(field(entry, 'Label'))
    if (!label) {
      skip('missing Label')
      continue
    }
    const rawArgs = field(entry, 'Args') ?? []
    if (!Array.isArray(rawArgs) || rawArgs.some((arg) => typeof arg !== 'string')) {
      skip('Args must be a list of strings')
      continue
    }
    const rawInput = field(entry, 'Input') ?? 'path'
    const input = typeof rawInput === 'string' ? INPUTS[rawInput.toLowerCase()] : undefined
    if (!input) {
      skip('Input must be path, jsonArgument, or jsonStdin')
      continue
    }
    const commandValue = nonEmptyString(field(entry, 'Command'))
    if (!commandValue) {
      skip('missing Command')
      continue
    }
    const command = path.isAbsolute(commandValue)
      ? (await lookup.exists(commandValue) ? commandValue : null)
      : await lookup.which(commandValue)
    if (!command) {
      skip(`command not found: ${commandValue}`)
      continue
    }
    const icon = nonEmptyString(field(entry, 'Icon'))
    result.push({
      id: `${CUSTOM_TARGET_PREFIX}${handlerId}`,
      label,
      ...(icon && (icon.startsWith('data:image/') || path.isAbsolute(icon)) ? { icon } : {}),
      command,
      args: rawArgs as string[],
      input
    })
  }
  return result
}
