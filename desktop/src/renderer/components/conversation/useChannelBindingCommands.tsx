import { useCallback, useEffect, useMemo } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAppBindingStore, type ThreadAppBinding } from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import {
  activeChannelBindings,
  bindableChannels,
  bindingChannelOf,
  useChannelBindingStore
} from '../../stores/channelBindingStore'
import { addToast } from '../../stores/toastStore'
import { ChannelIconBadge, getChannelVisualMeta } from '../ui/channelMeta'
import type { SlashSystemActionInfo } from './CommandSearchPopover'

const BIND_ACTION_PREFIX = 'bind:'
const UNBIND_ACTION_PREFIX = 'unbind:'
const NO_BINDINGS: ThreadAppBinding[] = []

export interface ChannelBindingCommands {
  actions: SlashSystemActionInfo[]
  selectAction(actionId: string): boolean
  runSlashCommand(text: string): Promise<boolean> | null
  isExactQuery(query: string | null): boolean
}

export function useChannelBindingCommands({
  threadId,
  enabled
}: {
  threadId: string
  enabled: boolean
}): ChannelBindingCommands {
  const t = useT()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const available = enabled && capabilities?.channelStatus === true
  const channels = useChannelBindingStore((s) => s.channels)
  const bindings = useAppBindingStore((s) => s.bindingsByThread[threadId] ?? NO_BINDINGS)

  useEffect(() => {
    if (!enabled) return
    void useChannelBindingStore.getState().ensureChannels(capabilities)
  }, [capabilities, enabled])

  const bindable = useMemo(() => available ? bindableChannels(channels) : [], [available, channels])
  const active = useMemo(() => available ? activeChannelBindings(bindings) : [], [available, bindings])

  const startBinding = useCallback(async (channelName: string): Promise<boolean> => {
    const label = getChannelVisualMeta(channelName).label
    try {
      await useChannelBindingStore.getState().requestBinding(threadId, channelName)
      return true
    } catch (err) {
      addToast(t('channelBinding.error.create', {
        channel: label,
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
      return false
    }
  }, [t, threadId])

  const stopBindings = useCallback(async (targets: ThreadAppBinding[]): Promise<boolean> => {
    if (targets.length === 0) {
      addToast(t('channelBinding.error.notBound'), 'info')
      return true
    }
    for (const binding of targets) {
      try {
        await useChannelBindingStore.getState().revokeBinding(threadId, binding.bindingId)
      } catch (err) {
        addToast(t('channelBinding.error.revoke', {
          channel: getChannelVisualMeta(bindingChannelOf(binding)).label,
          error: err instanceof Error ? err.message : String(err)
        }), 'error')
        return false
      }
    }
    return true
  }, [t, threadId])

  const actions = useMemo((): SlashSystemActionInfo[] => [
    ...bindable.map((channel) => {
      const name = channel.name.toLowerCase()
      return {
        id: `${BIND_ACTION_PREFIX}${name}`,
        label: `/bind ${name}`,
        description: t('channelBinding.bind.description', { channel: getChannelVisualMeta(name).label }),
        keywords: ['bind', name],
        icon: <ChannelIconBadge channelName={name} size={15} framed={false} />
      }
    }),
    ...active.map((binding) => {
      const name = bindingChannelOf(binding)
      return {
        id: `${UNBIND_ACTION_PREFIX}${binding.bindingId}`,
        label: active.length === 1 ? '/unbind' : `/unbind ${name}`,
        description: t('channelBinding.stop', { channel: getChannelVisualMeta(name).label }),
        keywords: ['unbind', name],
        icon: <ChannelIconBadge channelName={name} size={15} framed={false} muted />
      }
    })
  ], [active, bindable, t])

  const selectAction = useCallback((actionId: string): boolean => {
    if (actionId.startsWith(BIND_ACTION_PREFIX)) {
      void startBinding(actionId.slice(BIND_ACTION_PREFIX.length))
      return true
    }
    if (actionId.startsWith(UNBIND_ACTION_PREFIX)) {
      const bindingId = actionId.slice(UNBIND_ACTION_PREFIX.length)
      void stopBindings(active.filter((binding) => binding.bindingId === bindingId))
      return true
    }
    return false
  }, [active, startBinding, stopBindings])

  const runSlashCommand = useCallback((text: string): Promise<boolean> | null => {
    if (!available) return null
    const match = /^\/(bind|unbind)(?:\s+(\S+))?$/i.exec(text.trim())
    if (!match) return null
    const command = match[1].toLowerCase()
    const channel = match[2]?.toLowerCase()
    if (command === 'unbind') {
      return stopBindings(channel ? active.filter((binding) => bindingChannelOf(binding) === channel) : active)
    }
    const target = channel
      ? bindable.find((candidate) => candidate.name.toLowerCase() === channel)
      : bindable.length === 1 ? bindable[0] : undefined
    if (!target) {
      addToast(channel
        ? t('channelBinding.error.unavailable', { channel: getChannelVisualMeta(channel).label })
        : bindable.length === 0
          ? t('channelBinding.error.noChannel')
          : t('channelBinding.error.pickChannel', { command: `/bind ${bindable[0].name.toLowerCase()}` }), 'warning')
      return Promise.resolve(false)
    }
    return startBinding(target.name)
  }, [active, available, bindable, startBinding, stopBindings, t])

  const isExactQuery = useCallback(
    (query: string | null) => query?.toLowerCase() === 'unbind' && active.length === 1,
    [active.length]
  )

  return useMemo(
    () => ({ actions, selectAction, runSlashCommand, isExactQuery }),
    [actions, isExactQuery, runSlashCommand, selectAction]
  )
}
