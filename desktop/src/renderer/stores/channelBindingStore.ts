import { create } from 'zustand'
import { useAppBindingStore, type ThreadAppBinding } from './appBindingStore'
import type { ServerCapabilities } from './connectionStore'

export interface ChannelRuntimeStatus {
  name: string
  category: string
  enabled: boolean
  running: boolean
  runtimeState?: string
  failureCode?: string
}

export interface PendingChannelBinding {
  threadId: string
  bindingId: string
  bindingRequestId: string
  code: string
  channelName: string
  expiresAt: string
}

interface ChannelBindingStore {
  channels: ChannelRuntimeStatus[]
  pendingByThread: Record<string, PendingChannelBinding>

  ensureChannels(capabilities: ServerCapabilities | null): Promise<void>
  requestBinding(threadId: string, channelName: string): Promise<PendingChannelBinding>
  cancelPending(threadId: string): Promise<void>
  clearPending(threadId: string, bindingId?: string): void
  revokeBinding(threadId: string, bindingId: string): Promise<void>
  reset(): void
}

const CHANNEL_STATUS_TTL_MS = 30_000
const CHANNEL_APP_ID_PREFIX = 'com.dotharness.channel.'
const BINDABLE_CATEGORIES = new Set(['social', 'external'])

let channelsLoadedFor: ServerCapabilities | null = null
let channelsLoadedAt = 0
let channelsInFlight: Promise<void> | null = null

export const useChannelBindingStore = create<ChannelBindingStore>((set, get) => ({
  channels: [],
  pendingByThread: {},

  async ensureChannels(capabilities) {
    if (capabilities?.channelStatus !== true) {
      channelsLoadedFor = capabilities
      if (get().channels.length > 0) set({ channels: [] })
      return
    }
    const fresh = channelsLoadedFor === capabilities && Date.now() - channelsLoadedAt < CHANNEL_STATUS_TTL_MS
    if (fresh) return
    if (channelsInFlight) return channelsInFlight
    channelsInFlight = window.api.appServer.sendRequest('channel/status', {})
      .then((result) => {
        channelsLoadedFor = capabilities
        channelsLoadedAt = Date.now()
        set({ channels: (result as { channels?: ChannelRuntimeStatus[] }).channels ?? [] })
      })
      .catch(() => {})
      .finally(() => { channelsInFlight = null })
    return channelsInFlight
  },

  async requestBinding(threadId, channelName) {
    const channel = channelName.trim().toLowerCase()
    const previous = get().pendingByThread[threadId]
    if (previous) await revokeQuietly(threadId, previous.bindingId)
    await useAppBindingStore.getState().fetchThreadBindings(threadId)
    const stale = (useAppBindingStore.getState().bindingsByThread[threadId] ?? [])
      .filter((binding) => isChannelBinding(binding)
        && binding.state === 'connecting'
        && bindingChannelOf(binding) === channel)
    for (const binding of stale) await revokeQuietly(threadId, binding.bindingId)

    const result = await window.api.appServer.sendRequest('thread/channelBindings/request/create', {
      threadId,
      channelName: channel
    }) as Omit<PendingChannelBinding, 'threadId'>
    const pending: PendingChannelBinding = {
      threadId,
      bindingId: result.bindingId,
      bindingRequestId: result.bindingRequestId,
      code: result.code,
      channelName: result.channelName || channel,
      expiresAt: result.expiresAt
    }
    set((state) => ({ pendingByThread: { ...state.pendingByThread, [threadId]: pending } }))
    return pending
  },

  async cancelPending(threadId) {
    const pending = get().pendingByThread[threadId]
    if (!pending) return
    get().clearPending(threadId, pending.bindingId)
    await revokeQuietly(threadId, pending.bindingId)
    await useAppBindingStore.getState().fetchThreadBindings(threadId)
  },

  clearPending(threadId, bindingId) {
    const pending = get().pendingByThread[threadId]
    if (!pending || (bindingId && pending.bindingId !== bindingId)) return
    set((state) => {
      const next = { ...state.pendingByThread }
      delete next[threadId]
      return { pendingByThread: next }
    })
  },

  async revokeBinding(threadId, bindingId) {
    await window.api.appServer.sendRequest('thread/appBindings/revoke', { threadId, bindingId })
    await useAppBindingStore.getState().fetchThreadBindings(threadId)
  },

  reset() {
    channelsLoadedFor = null
    channelsLoadedAt = 0
    channelsInFlight = null
    set({ channels: [], pendingByThread: {} })
  }
}))

export function bindableChannels(channels: ChannelRuntimeStatus[]): ChannelRuntimeStatus[] {
  return channels.filter((channel) => channel.running && BINDABLE_CATEGORIES.has(channel.category.toLowerCase()))
}

export function isChannelBinding(binding: ThreadAppBinding): boolean {
  return binding.bindingKind === 'channel'
    || binding.channelTarget != null
    || binding.appId.startsWith(CHANNEL_APP_ID_PREFIX)
}

export function bindingChannelOf(binding: ThreadAppBinding): string {
  const name = binding.channelTarget?.channelName
    ?? (binding.appId.startsWith(CHANNEL_APP_ID_PREFIX) ? binding.appId.slice(CHANNEL_APP_ID_PREFIX.length) : binding.appId)
  return name.trim().toLowerCase()
}

export function activeChannelBindings(bindings: ThreadAppBinding[] | undefined): ThreadAppBinding[] {
  return (bindings ?? []).filter((binding) => binding.state === 'active' && isChannelBinding(binding))
}

async function revokeQuietly(threadId: string, bindingId: string): Promise<void> {
  await window.api.appServer.sendRequest('thread/appBindings/revoke', { threadId, bindingId }).catch(() => {})
}
