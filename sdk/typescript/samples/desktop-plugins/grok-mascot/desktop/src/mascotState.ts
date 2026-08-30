import type { DesktopPluginComposerMascotSurfaceContext, DesktopPluginMascotActivity } from '@dotcraft/plugin'
import type { CharacterState } from './characterMotion'

export type MascotStateInput = Pick<
  DesktopPluginComposerMascotSurfaceContext,
  'activity' | 'expression' | 'light'
>

const ACTIVITY_STATE: Record<DesktopPluginMascotActivity, CharacterState> = {
  idle: 'idle',
  focused: 'listening',
  dragging: 'dragging',
  working: 'working',
  decision: 'thinking',
  success: 'celebrate',
  error: 'alerting',
  sleeping: 'sleeping'
}

const EXPRESSION_STATE: Record<MascotStateInput['expression'], CharacterState | null> = {
  neutral: null,
  happy: 'happy',
  operator: 'working',
  sleep: 'sleeping'
}

/**
 * Three axes onto one state: activity is the base, `expression` speaks only while the
 * activity is ambient, and a non-default `light` outranks both.
 */
export function characterStateFor(context: MascotStateInput): CharacterState {
  if (context.expression === 'sleep' || context.activity === 'sleeping') return 'sleeping'
  if (context.light === 'error') return 'alerting'
  if (context.light === 'success') return 'celebrate'
  const base = ACTIVITY_STATE[context.activity] ?? 'idle'
  if (base !== 'idle' && base !== 'listening') return base
  return EXPRESSION_STATE[context.expression] ?? base
}
