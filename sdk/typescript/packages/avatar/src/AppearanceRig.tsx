import type { ReactNode } from 'react'
import type { AvatarPose, AvatarExpression } from './characters.js'
import type { Appearance } from './appearanceModel.js'
import type { PaletteLookup } from './palette.js'
import { MascotRig, type BodyPaint, type Faceplate, type MascotExpression } from './MascotRig.js'
export function AppearanceRig({ appearance, pose, expression: override, top, headBack, accessory, faceplate, held, back, front, surface, paint, paletteRegistry }: {
  appearance: Appearance; pose: AvatarPose; expression?: AvatarExpression
  top?: ReactNode; headBack?: ReactNode; accessory?: ReactNode; faceplate?: Faceplate; held?: ReactNode; back?: ReactNode; front?: ReactNode; surface?: ReactNode; paint?: BodyPaint; paletteRegistry?: PaletteLookup
}) {
  const derived: MascotExpression = pose === 'sleep' ? 'sleep' : ['done', 'greeting', 'acknowledge'].includes(pose) ? 'happy'
    : ['working', 'waiting', 'blocked'].includes(pose) ? 'operator' : 'neutral'
  const expression = override === 'base' ? 'neutral' : override ?? derived
  return <MascotRig size={1024} expression={expression} top={top} headBack={headBack} accessory={accessory} faceplate={faceplate} held={held} back={back} front={front} surface={surface} paint={paint}
    baseFace={appearance.baseFace}
    light={pose === 'blocked' ? 'error' : pose === 'done' ? 'success' : 'default'}
    avatar={appearance.palette === -1 ? undefined : { palette: appearance.palette }} paletteRegistry={paletteRegistry} antenna={appearance.antenna} />
}
