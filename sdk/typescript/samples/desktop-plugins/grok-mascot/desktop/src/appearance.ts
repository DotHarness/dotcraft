import type { DesktopPluginSettings, DesktopPluginSettingsMutation } from '@dotcraft/plugin'
import { SHIPPED_SHAPES } from './characterArt'

export const GROK_COLORS = [
  'black',
  'brown',
  'red',
  'orange',
  'yellow',
  'green',
  'cyan',
  'blue',
  'violet',
  'magenta',
  'gray'
] as const

export const GROK_SHAPES = SHIPPED_SHAPES

export type GrokColor = (typeof GROK_COLORS)[number]
export type GrokShape = (typeof GROK_SHAPES)[number]
export type GrokColorChoice = 'auto' | GrokColor
export type GrokShapeChoice = 'auto' | GrokShape

export interface GrokAppearance {
  readonly color: GrokColorChoice
  readonly shape: GrokShapeChoice
  readonly statusRing: boolean
}

export function choiceValue(choice: 'auto' | string): string | null {
  return choice === 'auto' ? null : choice
}

let current: GrokAppearance | null = null
let store: DesktopPluginSettings | null = null

export async function initializeAppearance(settings: DesktopPluginSettings): Promise<void> {
  store = settings
  current = (await settings.get<GrokAppearance>()).value
}

export function getAppearance(): GrokAppearance | null {
  return current
}

export function setAppearance(patch: Partial<GrokAppearance>): GrokAppearance | null {
  if (!current) return null
  current = { ...current, ...patch }
  void store?.mutate<GrokAppearance>('personal', mutationsOf(patch))
    .catch((error: unknown) => console.error('Grok could not write its appearance:', error))
  return current
}

export function subscribeAppearance(listener: (appearance: GrokAppearance) => void): () => void {
  return store?.onChange<GrokAppearance>((snapshot) => {
    current = snapshot.value
    listener(current)
  }) ?? (() => {})
}

function mutationsOf(patch: Partial<GrokAppearance>): DesktopPluginSettingsMutation[] {
  return Object.entries(patch).map(([key, value]) => ({ op: 'set', key, value }))
}
