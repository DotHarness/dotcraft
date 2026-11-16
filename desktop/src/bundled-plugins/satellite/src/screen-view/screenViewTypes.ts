import type { DesktopPluginScreenFrame, DesktopPluginScreenStatus } from '@dotcraft/plugin'

export type ScreenViewStatus = DesktopPluginScreenStatus
export type ScreenViewFramePayload = DesktopPluginScreenFrame
export type ScreenViewMode = 'dock' | 'theater'
export interface ScreenViewDockPosition { x: number; y: number }

export const SCREEN_VIEW_CAPABILITY = 'screen-v1'
export const FRAME_STALL_MS = 3000

export function requestedFrameWidth(cssWidth: number, devicePixelRatio: number): number {
  return Math.min(3840, Math.max(320, Math.ceil((cssWidth * devicePixelRatio) / 64) * 64))
}
