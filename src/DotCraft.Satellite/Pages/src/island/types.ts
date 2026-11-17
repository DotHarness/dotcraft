import type { IslandTheme } from '@dotcraft/island'

export type IslandMode = 'compact' | 'running' | 'approval' | 'standby' | 'paused' | 'offline'

export interface IslandPeer {
  id: string
  name: string
  meta: string
  canOpenFolder: boolean
}

export interface IslandState {
  visible: boolean
  theme: IslandTheme
  reduceMotion: boolean
  mode: IslandMode
  summary: string
  label: string
  since: string
  pause: string
  resume: string
  expand: string
  collapse: string
  disconnect: string
  openFolder: string
  watching: boolean
  watchingLabel: string
  peers: IslandPeer[]
  running: {
    peerId: string
    operation: string
    command: string
    commandFull: string
    concurrency: number
  } | null
  approval: {
    peerId: string
    title: string
    operation: string
    target: string
    targetFull: string
    countdown: string
    fraction: number
    queued: number
    deny: string
    allow: string
  } | null
}

export interface IslandBounds {
  x: number
  y: number
  width: number
  height: number
}

export type IslandAction = 'allow' | 'deny' | 'pause' | 'resume' | 'disconnect' | 'openFolder'

export type IslandMessage =
  | { type: 'bounds'; pill: IslandBounds | null }
  | { type: 'press' }
  | { type: 'action'; name: IslandAction; peer?: string }
