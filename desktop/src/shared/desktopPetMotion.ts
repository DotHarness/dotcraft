import type { PetPoint } from './desktopPet'

export const PET_RETURN_DURATION = 720
export interface PetReturnFrame extends PetPoint {
  scaleX: number
  scaleY: number
  rotation: number
  travel: number
}

export function samplePetReturn(start: PetPoint, end: PetPoint, progress: number): PetReturnFrame {
  const t = Math.max(0, Math.min(1, progress))
  const launch = 80 / PET_RETURN_DURATION
  const touchdown = 600 / PET_RETURN_DURATION
  if (t < launch) {
    const crouch = Math.sin(t / launch * Math.PI / 2)
    return { ...start, scaleX: 1 + 0.12 * crouch, scaleY: 1 - 0.14 * crouch, rotation: 0, travel: 0 }
  }
  if (t >= touchdown) {
    const settle = Math.sin((t - touchdown) / (1 - touchdown) * Math.PI)
    return { ...end, scaleX: 1 + 0.12 * settle, scaleY: 1 - 0.14 * settle, rotation: 0, travel: 1 }
  }
  const flight = (t - launch) / (touchdown - launch)
  const travel = flight * flight * (3 - 2 * flight)
  const height = Math.min(110, Math.max(36, Math.hypot(end.x - start.x, end.y - start.y) * 0.16))
  const release = Math.max(0, 1 - flight / 0.18)
  const stretch = Math.sin(Math.PI * flight)
  return {
    x: start.x + (end.x - start.x) * travel,
    y: start.y + (end.y - start.y) * travel - 4 * flight * (1 - flight) * height,
    scaleX: 1 + 0.12 * release - 0.04 * stretch,
    scaleY: 1 - 0.14 * release + 0.05 * stretch,
    rotation: Math.sign(end.x - start.x) * 5 * stretch,
    travel
  }
}
