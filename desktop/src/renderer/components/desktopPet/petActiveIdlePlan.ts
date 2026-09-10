import type { MascotActiveIdleMotion, MascotActiveIdlePlan, MascotIdleDirection } from '@dotcraft/avatar/react'

/** Room a trip needs sideways: the longest leg (rocket) plus the character itself. */
const TRIP_ROOM = 300
/** Height above the pet the rocket apex and the hover climb need. */
const ROCKET_ROOM = 80
const HOVER_ROOM = 150

export function petActiveIdlePlan(
  position: { x: number; y: number; size: number },
  viewport: { width: number; height: number }
): MascotActiveIdlePlan | null {
  const roomLeft = position.x
  const roomRight = viewport.width - position.x - position.size
  const direction: MascotIdleDirection = roomLeft < TRIP_ROOM ? 'right' : 'left'
  if ((direction === 'right' ? roomRight : roomLeft) < TRIP_ROOM) return null
  const motions: MascotActiveIdleMotion[] = ['hop']
  if (position.y >= ROCKET_ROOM) motions.push('rocket')
  if (position.y >= HOVER_ROOM) motions.push('hover')
  return { direction, motions }
}
