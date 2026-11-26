import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { Hammer } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { setThreadCreator, useThreadCreator } from '../../stores/creatorModeStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import type { SlashSystemActionInfo } from './CommandSearchPopover'
import { ComposerCreatorLabel } from './ComposerShell'
import { CREATOR_MASCOT_OUTFIT } from './creatorMascotOutfit'

export const CREATOR_SYSTEM_ACTION_ID = 'creator'

export function isCreatorSlashCommand(trimmed: string): boolean {
  return trimmed.toLowerCase() === '/creator'
}

interface ComposerCreatorControls {
  action: SlashSystemActionInfo | null
  pill: ReactNode
  toggle: () => void
  mascotOutfit: typeof CREATOR_MASCOT_OUTFIT | undefined
  mascotOutfitSignal: number
}

export function useComposerCreatorControls({
  available,
  creator,
  onChange
}: {
  available: boolean
  creator: boolean
  onChange: (creator: boolean) => void
}): ComposerCreatorControls {
  const t = useT()
  const [mascotOutfitSignal, setMascotOutfitSignal] = useState(0)
  const change = useCallback((next: boolean): void => {
    setMascotOutfitSignal((value) => value + 1)
    onChange(next)
  }, [onChange])
  const action = useMemo((): SlashSystemActionInfo | null => available ? {
    id: CREATOR_SYSTEM_ACTION_ID,
    label: t('composer.system.creator'),
    description: t('composer.system.creator.description'),
    keywords: ['creator', 'plugin', 'extension'],
    icon: <Hammer size={15} strokeWidth={2} aria-hidden />
  } : null, [available, t])
  const toggle = useCallback(() => {
    if (!creator && useConversationStore.getState().turnStatus === 'idle') useFeatureGuideStore.getState().request('creator')
    change(!creator)
  }, [creator, change])
  const pill = available && creator ? (
    <ComposerCreatorLabel
      onDisable={() => change(false)}
      label={t('composer.system.creator')}
      title={t('composer.creatorPill.title')}
      ariaLabel={t('composer.system.creator.disable')}
    />
  ) : null
  return { action, pill, toggle, mascotOutfit: available && creator ? CREATOR_MASCOT_OUTFIT : undefined, mascotOutfitSignal }
}

export function useThreadComposerCreator(threadId: string, available: boolean): ComposerCreatorControls {
  const t = useT()
  const pendingCreator = useUIStore((s) => (
    s.pendingThreadCreation?.requestId === threadId
      ? s.pendingThreadCreation.creator === true
      : null
  ))
  const threadCreator = useThreadCreator(threadId)
  const creator = pendingCreator ?? threadCreator
  const onChange = useCallback((next: boolean): void => {
    if (pendingCreator !== null) return
    void setThreadCreator(threadId, next).catch((err: unknown) => {
      addToast(t('composer.creator.changeFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    })
  }, [pendingCreator, threadId, t])
  return useComposerCreatorControls({ available: available && threadId.length > 0, creator, onChange })
}
