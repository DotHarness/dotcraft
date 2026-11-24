import { useCallback, useMemo, type ReactNode } from 'react'
import { Hammer } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { setThreadCreator, useThreadCreator } from '../../stores/creatorModeStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { addToast } from '../../stores/toastStore'
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
  const action = useMemo((): SlashSystemActionInfo | null => available ? {
    id: CREATOR_SYSTEM_ACTION_ID,
    label: t('composer.system.creator'),
    description: creator ? t('composer.system.creator.disable') : t('composer.system.creator.enable'),
    keywords: ['creator', 'plugin', 'extension'],
    icon: <Hammer size={15} strokeWidth={2} aria-hidden />
  } : null, [available, creator, t])
  const toggle = useCallback(() => {
    if (!creator && useConversationStore.getState().turnStatus === 'idle') useFeatureGuideStore.getState().request('creator')
    onChange(!creator)
  }, [creator, onChange])
  const pill = available && creator ? (
    <ComposerCreatorLabel
      onDisable={() => onChange(false)}
      label={t('composer.system.creator')}
      title={t('composer.creatorPill.title')}
      ariaLabel={t('composer.system.creator.disable')}
    />
  ) : null
  return { action, pill, toggle, mascotOutfit: available && creator ? CREATOR_MASCOT_OUTFIT : undefined }
}

export function useThreadComposerCreator(threadId: string, available: boolean): ComposerCreatorControls {
  const t = useT()
  const creator = useThreadCreator(threadId)
  const onChange = useCallback((next: boolean): void => {
    void setThreadCreator(threadId, next).catch((err: unknown) => {
      addToast(t('composer.creator.changeFailed', {
        error: err instanceof Error ? err.message : String(err)
      }), 'error')
    })
  }, [threadId, t])
  return useComposerCreatorControls({ available: available && threadId.length > 0, creator, onChange })
}
