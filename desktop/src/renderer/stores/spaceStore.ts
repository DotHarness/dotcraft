import { create } from 'zustand'
import type { DesktopPluginSpaceLocation } from '@dotcraft/plugin'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { openProjectConnection, openProjectThread } from '../components/sidebar/projectConnectionActions'
import { projectIdentity } from '../components/sidebar/projectThreads'
import {
  findDesktopPluginMainView,
  useDesktopPluginRegistry,
  type ActiveDesktopPluginSpace
} from '../plugins/desktopPluginRegistry'
import { addToast } from './toastStore'
import {
  captureAppNavigationLocation,
  returnToAppNavigationLocation,
  type AppNavigationLocation
} from './appNavigationStore'
import { useOwnedThreadsStore } from './ownedThreadsStore'
import { useThreadStore } from './threadStore'
import { useUIStore, type DesktopPluginMainView } from './uiStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'

type SpaceLocation =
  | { kind: 'view'; viewKey: DesktopPluginMainView }
  | { kind: 'thread' }

interface ReturnTarget {
  projectKey: string | null
  location: AppNavigationLocation
}

interface SpaceState {
  last: Readonly<Record<string, SpaceLocation>>
  returnTo: ReturnTarget | null
  outside: ReturnTarget | null
  entering: boolean
}

export const useSpaceStore = create<SpaceState>(() => ({
  last: {},
  returnTo: null,
  outside: null,
  entering: false
}))

export interface ActiveSpace {
  space: ActiveDesktopPluginSpace
  location: DesktopPluginSpaceLocation
}

export function resolveActiveSpace(
  spaces: readonly ActiveDesktopPluginSpace[],
  activeMainView: string,
  activeThreadId: string | null,
  records: readonly OwnedThreadRecord[]
): ActiveSpace | null {
  if (activeMainView === 'conversation') {
    const record = activeThreadId ? records.find((candidate) => candidate.threadId === activeThreadId && candidate.space) : undefined
    const space = record && spaces.find((candidate) => candidate.pluginId === record.pluginId && candidate.id === record.space)
    return space && record ? { space, location: { kind: 'thread', threadId: record.threadId } } : null
  }
  for (const space of spaces) {
    const index = space.viewKeys.indexOf(activeMainView as DesktopPluginMainView)
    if (index >= 0) return { space, location: { kind: 'view', viewId: space.views[index] } }
  }
  return null
}

function currentActiveSpace(): ActiveSpace | null {
  return resolveActiveSpace(
    useDesktopPluginRegistry.getState().spaces,
    useUIStore.getState().activeMainView,
    useThreadStore.getState().activeThreadId,
    useOwnedThreadsStore.getState().records
  )
}

export function useActiveSpace(): ActiveSpace | null {
  const spaces = useDesktopPluginRegistry((state) => state.spaces)
  const activeMainView = useUIStore((state) => state.activeMainView)
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const records = useOwnedThreadsStore((state) => state.records)
  return resolveActiveSpace(spaces, activeMainView, activeThreadId, records)
}

function foregroundProjectKey(): string | null {
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const foreground = [...projects, ...(chat ? [chat] : [])].find((project) => project.state === 'foreground')
  return foreground ? projectIdentity(foreground) : null
}

function captureOutside(): ReturnTarget {
  return { projectKey: foregroundProjectKey(), location: captureAppNavigationLocation() }
}

export function enterSpace(space: ActiveDesktopPluginSpace): void {
  if (!currentActiveSpace()) useSpaceStore.setState({ returnTo: captureOutside(), entering: true })
  const last = useSpaceStore.getState().last[space.spaceKey]
  const failed = (error: unknown): void => {
    useSpaceStore.setState({ entering: false })
    addToast(error instanceof Error ? error.message : String(error), 'error')
  }
  if (last?.kind === 'view' && findDesktopPluginMainView(last.viewKey)) {
    useUIStore.getState().setActiveMainView(last.viewKey)
  } else {
    try {
      void Promise.resolve(space.open(space.host)).catch(failed)
    } catch (error) {
      failed(error)
    }
  }
}

function openNewChat(): void {
  useUIStore.getState().setActiveMainView('conversation')
  useThreadStore.getState().setActiveThreadId(null)
}

export async function leaveSpace(): Promise<void> {
  const target = useSpaceStore.getState().returnTo
  useSpaceStore.setState({ returnTo: null, entering: false })
  if (!target) {
    openNewChat()
    return
  }
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const project = [...projects, ...(chat ? [chat] : [])].find((candidate) => projectIdentity(candidate) === target.projectKey)
  if (project && project.state !== 'foreground') {
    if (target.location.kind === 'conversation' && target.location.threadId) {
      await openProjectThread(project, target.location.threadId)
      return
    }
    if (!await openProjectConnection(project)) return
    if (target.location.kind === 'conversation') openNewChat()
    else returnToAppNavigationLocation(target.location)
    return
  }
  returnToAppNavigationLocation(target.location)
  if (currentActiveSpace()) openNewChat()
}

export function startSpaceTracking(): () => void {
  let previous: ActiveSpace | null = currentActiveSpace()
  const update = (): void => {
    const active = currentActiveSpace()
    const state = useSpaceStore.getState()
    if (!active && useUIStore.getState().pendingProjectThreadOpen) return
    if (!active) {
      if (previous) {
        if (state.returnTo) useSpaceStore.setState({ returnTo: null })
      } else if (!state.entering) {
        const outside = captureOutside()
        if (JSON.stringify(outside) !== JSON.stringify(state.outside)) useSpaceStore.setState({ outside })
      }
    } else {
      const location: SpaceLocation = active.location.kind === 'view'
        ? { kind: 'view', viewKey: active.space.viewKeys[active.space.views.indexOf(active.location.viewId)] }
        : { kind: 'thread' }
      const last = state.last[active.space.spaceKey]
      const sameLast = JSON.stringify(last) === JSON.stringify(location)
      if (state.entering || (!state.returnTo && state.outside) || !sameLast) {
        useSpaceStore.setState({
          entering: false,
          returnTo: state.returnTo ?? state.outside,
          ...(sameLast ? {} : { last: { ...state.last, [active.space.spaceKey]: location } })
        })
      }
    }
    previous = active
  }
  let scheduled = false
  let stopped = false
  const schedule = (): void => {
    if (scheduled) return
    scheduled = true
    queueMicrotask(() => {
      scheduled = false
      if (!stopped) update()
    })
  }
  const stops = [
    useUIStore.subscribe(schedule),
    useThreadStore.subscribe(schedule),
    useOwnedThreadsStore.subscribe(schedule),
    useDesktopPluginRegistry.subscribe(schedule)
  ]
  update()
  return () => {
    stopped = true
    stops.forEach((stop) => stop())
  }
}
