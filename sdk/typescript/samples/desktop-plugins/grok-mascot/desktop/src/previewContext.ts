import type { DesktopPluginComposerMascotSurfaceContext } from '@dotcraft/plugin'
import type { MascotStateInput } from './mascotState'

export const PREVIEW_STATES: readonly MascotStateInput[] = [
  { activity: 'idle', expression: 'neutral', light: 'default' },
  { activity: 'focused', expression: 'happy', light: 'default' },
  { activity: 'working', expression: 'operator', light: 'default' },
  { activity: 'decision', expression: 'operator', light: 'default' },
  { activity: 'dragging', expression: 'operator', light: 'default' },
  { activity: 'success', expression: 'happy', light: 'success' },
  { activity: 'error', expression: 'neutral', light: 'error' },
  { activity: 'sleeping', expression: 'sleep', light: 'default' }
]

export function previewContext(
  state: MascotStateInput,
  size: number
): DesktopPluginComposerMascotSurfaceContext {
  return {
    workspacePath: null,
    threadId: null,
    mode: 'agent',
    busy: false,
    awaitingApproval: false,
    variant: 'default',
    minimalChrome: false,
    size,
    submitRevision: 0,
    reasoningEffort: 'medium',
    speed: 'standard',
    contextMax: false,
    reducedMotion: false,
    ...state
  }
}
