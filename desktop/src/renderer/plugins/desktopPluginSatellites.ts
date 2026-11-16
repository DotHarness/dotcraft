import type {
  DesktopPluginPendingRoute,
  DesktopPluginRemoteToolHost,
  DesktopPluginRemoteToolHostSnapshot,
  DesktopPluginSatellites
} from '@dotcraft/plugin'
import type { DesktopPluginCleanupScope } from './desktopPluginHost'
import { useConnectionStore } from '../stores/connectionStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useConversationStore } from '../stores/conversationStore'
import { cancelThreadRouteReapply, useThreadRouteStore } from '../stores/threadRouteStore'

function requireActive(scope: DesktopPluginCleanupScope): void {
  if (!scope.active) throw new Error('Desktop extension is not active.')
}

function own(scope: DesktopPluginCleanupScope, cleanup: () => void): () => void {
  const release = (): void => {
    if (scope.cleanups.delete(release)) cleanup()
  }
  scope.cleanups.add(release)
  return release
}

export function createDesktopPluginSatellites(scope: DesktopPluginCleanupScope): DesktopPluginSatellites {
  return {
    list: () => { requireActive(scope); return window.api.satellites.list() },
    createInvite: () => { requireActive(scope); return window.api.satellites.createInvite() },
    revoke: (peerId) => { requireActive(scope); return window.api.satellites.revoke(peerId) },
    activity: (peerId) => { requireActive(scope); return window.api.satellites.activity(peerId) },
    shareStatus: () => { requireActive(scope); return window.api.satellites.shareStatus() },
    async wasInvitedHere(inviteId) {
      requireActive(scope)
      const settings = await window.api.settings.get()
      return scope.active && (settings.createdSatelliteInviteIds ?? []).some(entry => entry.inviteId === inviteId)
    },
    onEvent(listener) {
      requireActive(scope)
      return own(scope, window.api.satellites.onEvent(event => {
        if (scope.active) listener(event)
      }))
    }
  }
}

function readSnapshot(): DesktopPluginRemoteToolHostSnapshot {
  const route = useThreadRouteStore.getState()
  const workspace = useWorkspaceProjectsStore.getState()
  const project = workspace.projects.find(project => project.projectId === workspace.foregroundProjectId)
  return {
    supported: useConnectionStore.getState().capabilities?.remoteToolHost === true,
    remote: project?.kind === 'remote' || useConversationStore.getState().remoteWorkspaceActive,
    hosts: route.hosts,
    routes: route.routes,
    pendingRoute: route.pendingRoute,
    deferredRoutes: route.deferredRoutes,
    connecting: route.connecting
  }
}

function sameSnapshot(left: DesktopPluginRemoteToolHostSnapshot, right: DesktopPluginRemoteToolHostSnapshot): boolean {
  return left.supported === right.supported && left.remote === right.remote
    && left.hosts === right.hosts && left.routes === right.routes
    && left.pendingRoute === right.pendingRoute && left.deferredRoutes === right.deferredRoutes
    && left.connecting === right.connecting
}

let pendingOwner: symbol | null = null
const deferredOwners = new Map<string, symbol>()

export function createDesktopPluginRemoteToolHost(scope: DesktopPluginCleanupScope): DesktopPluginRemoteToolHost {
  const owner = Symbol()
  const isActive = (): boolean => scope.active
  let snapshot = readSnapshot()
  let pending: DesktopPluginPendingRoute | null | undefined
  const deferred = new Map<string, DesktopPluginPendingRoute | null>()
  const restorations = new Set<string>()
  own(scope, () => {
    const state = useThreadRouteStore.getState()
    if (pendingOwner === owner) {
      if (pending !== undefined && state.pendingRoute === pending) state.setPendingRoute(null)
      pendingOwner = null
    }
    for (const [threadId, route] of deferred) {
      if (deferredOwners.get(threadId) !== owner) continue
      if (state.deferredRoutes[threadId] === route) state.clearDeferredRoute(threadId)
      deferredOwners.delete(threadId)
    }
    for (const threadId of restorations) cancelThreadRouteReapply(threadId, isActive)
  })
  const active = (): void => requireActive(scope)
  return {
    getSnapshot() {
      const next = readSnapshot()
      if (!sameSnapshot(snapshot, next)) snapshot = next
      return snapshot
    },
    onChange(listener) {
      active()
      let current = readSnapshot()
      const publish = (): void => {
        if (!scope.active) return
        const next = readSnapshot()
        if (sameSnapshot(current, next)) return
        current = next
        snapshot = next
        listener(next)
      }
      const stops = [
        useThreadRouteStore.subscribe(publish),
        useConnectionStore.subscribe(publish),
        useWorkspaceProjectsStore.subscribe(publish),
        useConversationStore.subscribe(publish)
      ]
      return own(scope, () => stops.forEach(stop => stop()))
    },
    list(threadId) { active(); return useThreadRouteStore.getState().list(threadId) },
    connect(threadId, hostId, workspaceId) { active(); return useThreadRouteStore.getState().connect(threadId, hostId, workspaceId) },
    disconnect(threadId) { active(); return useThreadRouteStore.getState().disconnect(threadId) },
    setPendingRoute(route) {
      active()
      pending = route
      pendingOwner = owner
      useThreadRouteStore.getState().setPendingRoute(route)
    },
    deferRoute(threadId, route) {
      active()
      deferred.set(threadId, route)
      deferredOwners.set(threadId, owner)
      useThreadRouteStore.getState().deferRoute(threadId, route)
    },
    clearDeferredRoute(threadId) {
      active()
      deferred.delete(threadId)
      deferredOwners.delete(threadId)
      useThreadRouteStore.getState().clearDeferredRoute(threadId)
    },
    applyDeferredRoute(threadId) {
      active()
      deferred.delete(threadId)
      deferredOwners.delete(threadId)
      return useThreadRouteStore.getState().applyDeferredRoute(threadId)
    },
    maybeReapply(threadId, options) {
      active()
      restorations.add(threadId)
      useThreadRouteStore.getState().maybeReapply(threadId, { ...options, isActive })
    }
  }
}
