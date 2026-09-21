import type { ReactNode } from 'react'
import type { AvatarPose, AvatarExpression } from './characters.js'
import type { Appearance } from './appearanceModel.js'
import { MascotRig, type BodyPaint, type Faceplate, type MascotExpression } from './MascotRig.js'
export function AppearanceRig({ appearance, pose, expression: override, top, accessory, faceplate, held, back, front, surface, paint }: {
  appearance: Appearance; pose: AvatarPose; expression?: AvatarExpression
  top?: ReactNode; accessory?: ReactNode; faceplate?: Faceplate; held?: ReactNode; back?: ReactNode; front?: ReactNode; surface?: ReactNode; paint?: BodyPaint
}) {
  const derived: MascotExpression = pose === 'sleep' ? 'sleep' : ['done', 'greeting', 'acknowledge'].includes(pose) ? 'happy'
    : ['working', 'waiting', 'blocked'].includes(pose) ? 'operator' : 'neutral'
  const expression = override === 'base' ? 'neutral' : override ?? derived
  return <MascotRig size={1024} expression={expression} top={top} accessory={accessory} faceplate={faceplate} held={held} back={back} front={front} surface={surface} paint={paint}
    baseFace={appearance.baseFace}
    light={pose === 'blocked' ? 'error' : pose === 'done' ? 'success' : 'default'}
    avatar={appearance.palette === -1 ? undefined : { palette: appearance.palette }} />
}
