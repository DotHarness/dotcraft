import { useEffect, useMemo } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAppBindingStore, type ThreadAppBinding } from '../../stores/appBindingStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { activeChannelBindings, bindingChannelOf } from '../../stores/channelBindingStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ChannelIconBadge, getChannelVisualMeta } from '../ui/channelMeta'
import styles from './ThreadChannelBindingChips.module.css'

const NO_BINDINGS: ThreadAppBinding[] = []

export function useThreadChannelBindings(threadId: string): ThreadAppBinding[] {
  const bindings = useAppBindingStore((s) => s.bindingsByThread[threadId] ?? NO_BINDINGS)
  const canUseAppBinding = useConnectionStore((s) => s.capabilities?.appBindingVersion === 1)
  useEffect(() => {
    if (canUseAppBinding) void useAppBindingStore.getState().fetchThreadBindings(threadId)
  }, [canUseAppBinding, threadId])
  return useMemo(() => activeChannelBindings(bindings), [bindings])
}

function channelBindingTargetName(binding: ThreadAppBinding): string | null {
  return binding.channelTarget?.displayName?.trim() || null
}

export function ThreadChannelBindingChips({ bindings }: { bindings: ThreadAppBinding[] }): JSX.Element | null {
  const t = useT()
  if (bindings.length === 0) return null
  return (
    <>
      {bindings.map((binding) => {
        const channelName = bindingChannelOf(binding)
        const channel = getChannelVisualMeta(channelName).label
        const target = channelBindingTargetName(binding)
        const tooltip = target
          ? t('channelBinding.continuingInTarget', { channel, target })
          : t('channelBinding.continuingIn', { channel })
        return (
          <ActionTooltip
            key={binding.bindingId}
            label={tooltip}
            wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
          >
            <span className={styles.chip} aria-label={tooltip}>
              <ChannelIconBadge channelName={channelName} size={12} framed={false} tooltip={tooltip} />
              <span className={styles.label}>{target ?? channel}</span>
            </span>
          </ActionTooltip>
        )
      })}
    </>
  )
}
