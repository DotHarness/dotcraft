import type { ReactNode } from 'react'
export type MascotExpression = 'neutral' | 'happy' | 'operator' | 'sleep'
export type MascotLight = 'default' | 'error' | 'success'
export interface ComposerMascotContext {
  size: number
  activity: 'error' | 'success' | 'sleeping' | 'dragging' | 'decision' | 'working' | 'focused' | 'idle'
  expression: MascotExpression
  light: MascotLight
  submitRevision: number
  reasoningEffort: 'off' | 'low' | 'medium' | 'high' | 'extraHigh'
  speed: 'standard' | 'fast'
  contextMax: boolean
  reducedMotion: boolean
}
export interface ComposerMascotProps {
  name?: string
  motion?: 'system' | 'on' | 'off'
  focused?: boolean
  dragOver?: boolean
  bounceSignal?: number
  interaction?: { expression?: MascotExpression; light?: MascotLight; hold?: 'sign'; bubble?: ReactNode }
  reasoningEffort?: ComposerMascotContext['reasoningEffort']
  speed?: ComposerMascotContext['speed']
  contextMax?: boolean
  anchorOffset?: number
  anchorPushSignal?: number
  handoff?: boolean
  renderCharacter?: (character: ReactNode, context: ComposerMascotContext) => ReactNode
  renderMenu?: (position: { x: number; y: number }, close: () => void) => ReactNode
  onNameRendered?: (name: string | undefined) => void
}
