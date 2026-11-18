import type { TopLevelMenuId } from './locales/types'

export interface ApplicationMenuCommand {
  type: 'item'
  commandId: number
  label: string
  enabled: boolean
  accelerator?: string
}

export interface ApplicationMenuSeparator {
  type: 'separator'
}

export type ApplicationMenuEntry = ApplicationMenuCommand | ApplicationMenuSeparator

export type ApplicationMenuSnapshot = Record<TopLevelMenuId, ApplicationMenuEntry[]>
