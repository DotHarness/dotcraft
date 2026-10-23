import type { ConfigFieldOptionWire } from '../../../shared/channelModules'

type GroupChoice = 'all' | 'partial' | 'none'

export function knownValues(options: ConfigFieldOptionWire[], saved: string[]): string[] {
  const kept = new Set(saved)
  return options.map((option) => option.value).filter((value) => kept.has(value))
}

export function unknownValues(options: ConfigFieldOptionWire[], saved: string[]): string[] {
  const known = new Set(options.map((option) => option.value))
  return [...new Set(saved)].filter((value) => !known.has(value))
}

export function groupChoice(values: string[], chosen: string[]): GroupChoice {
  const kept = values.filter((value) => chosen.includes(value)).length
  return kept === 0 ? 'none' : kept === values.length ? 'all' : 'partial'
}

export function toggleGroup(options: ConfigFieldOptionWire[], values: string[], chosen: string[]): string[] {
  const next = groupChoice(values, chosen) === 'all'
    ? chosen.filter((value) => !values.includes(value))
    : [...chosen, ...values]
  return knownValues(options, next)
}

export function toggleValue(options: ConfigFieldOptionWire[], value: string, chosen: string[]): string[] {
  return knownValues(options, chosen.includes(value) ? chosen.filter((item) => item !== value) : [...chosen, value])
}
