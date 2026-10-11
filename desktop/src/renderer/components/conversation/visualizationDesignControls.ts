import type { VisualizationChangesContext } from '../../../shared/composerContext'

export type VisualizationDesignValue = number | string | boolean

interface ControlBase { id: string; label: string; reference?: string }

export type VisualizationDesignControl = ControlBase & (
  | { kind: 'slider'; min: number; max: number; step: number; unit?: string; initial: number }
  | { kind: 'color'; initial: string }
  | { kind: 'toggle'; initial: boolean }
  | { kind: 'select'; options: Array<{ label: string; value: string }>; initial: string }
)

export interface VisualizationDesignGroup {
  id: string
  label: string
  variant: string | null
  visible: boolean
  controls: VisualizationDesignControl[]
}

export interface VisualizationDesignControls {
  groups: VisualizationDesignGroup[]
  variant: string | null
}

export interface VisualizationDesignEdit {
  group: string
  label: string
  reference?: string
  from: VisualizationDesignValue
  to: VisualizationDesignValue
}

export type VisualizationDesignValues = Record<string, VisualizationDesignValue>

const MAX_GROUPS = 16
const MAX_CONTROLS = 12
const MAX_OPTIONS = 12
const MAX_TEXT = 80
const REFERENCE = /^[A-Za-z0-9_\-./:@$#]+$/
const HEX = /^#[0-9a-f]{6}$/

export function readVisualizationDesignControls(value: unknown): VisualizationDesignControls | null {
  const params = record(value)
  if (!params || !Array.isArray(params.groups)) return null
  const groups = params.groups.slice(0, MAX_GROUPS).flatMap((item): VisualizationDesignGroup[] => {
    const group = record(item)
    const id = text(group?.id)
    if (!group || !id || !Array.isArray(group.controls)) return []
    const controls = group.controls.slice(0, MAX_CONTROLS).flatMap(readControl)
    return controls.length === 0 ? [] : [{
      id,
      label: text(group.label) ?? '',
      variant: text(group.variant) ?? null,
      visible: group.visible !== false,
      controls
    }]
  })
  return { groups, variant: text(params.variant) ?? null }
}

export function reconcileVisualizationDesignValues(
  controls: VisualizationDesignControls,
  previous: VisualizationDesignValues
): VisualizationDesignValues {
  const values: VisualizationDesignValues = {}
  for (const control of controls.groups.flatMap(group => group.controls)) {
    const current = previous[control.id]
    values[control.id] = typeof current === typeof control.initial ? current : control.initial
  }
  return values
}

export function initialVisualizationDesignValues(controls: VisualizationDesignControls): VisualizationDesignValues {
  return Object.fromEntries(controls.groups.flatMap(group => group.controls.map(control => [control.id, control.initial])))
}

export function collectVisualizationDesignEdits(
  controls: VisualizationDesignControls,
  values: VisualizationDesignValues
): VisualizationDesignEdit[] {
  return controls.groups.flatMap(group => group.controls.flatMap(control => {
    const to = values[control.id]
    if (to === undefined || to === control.initial) return []
    return [{ group: group.label, label: control.label, ...(control.reference ? { reference: control.reference } : {}), from: control.initial, to }]
  }))
}

export function buildVisualizationChangesContext(
  path: string,
  title: string,
  controls: VisualizationDesignControls,
  values: VisualizationDesignValues
): VisualizationChangesContext | null {
  const edits = collectVisualizationDesignEdits(controls, values)
  if (edits.length === 0) return null
  const lines = edits.map(edit =>
    `${edit.group ? `${edit.group} · ` : ''}${edit.label}${edit.reference ? ` (${edit.reference})` : ''}: ${String(edit.from)} → ${String(edit.to)}`)
  return {
    id: `visualization-changes:${path}`,
    kind: 'visualizationChanges',
    path,
    title,
    text: [...(controls.variant ? [`Visible design: ${controls.variant}`] : []), ...lines].join('\n')
  }
}

function readControl(value: unknown): VisualizationDesignControl[] {
  const control = record(value)
  const id = text(control?.id)
  const label = text(control?.label)
  if (!control || !id || !label) return []
  const reference = typeof control.reference === 'string' && control.reference.length <= MAX_TEXT && REFERENCE.test(control.reference)
    ? { reference: control.reference }
    : {}
  const base = { id, label, ...reference }
  switch (control.kind) {
    case 'slider': {
      const { min, max, step, initial } = control
      if (![min, max, step, initial].every(isFiniteNumber) || (min as number) >= (max as number) || (step as number) <= 0) return []
      const unit = text(control.unit)?.slice(0, 16)
      const clamped = Math.min(max as number, Math.max(min as number, initial as number))
      return [{ ...base, kind: 'slider', min: min as number, max: max as number, step: step as number, ...(unit ? { unit } : {}), initial: clamped }]
    }
    case 'color':
      return typeof control.initial === 'string' && HEX.test(control.initial) ? [{ ...base, kind: 'color', initial: control.initial }] : []
    case 'toggle':
      return typeof control.initial === 'boolean' ? [{ ...base, kind: 'toggle', initial: control.initial }] : []
    case 'select': {
      if (!Array.isArray(control.options)) return []
      const options = control.options.slice(0, MAX_OPTIONS).flatMap(item => {
        const option = record(item)
        const optionValue = typeof option?.value === 'string' && option.value.length <= MAX_TEXT ? option.value : null
        return optionValue === null ? [] : [{ label: text(option?.label) ?? optionValue, value: optionValue }]
      })
      return typeof control.initial === 'string' && options.some(option => option.value === control.initial)
        ? [{ ...base, kind: 'select', options, initial: control.initial }]
        : []
    }
    default:
      return []
  }
}

function record(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null
}

function text(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim().slice(0, MAX_TEXT)
  return trimmed || undefined
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}
