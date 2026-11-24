import { useCallback, useEffect, useMemo, type CSSProperties } from 'react'
import { Hammer, PlugZap, ScanSearch } from 'lucide-react'
import { AppearanceAvatar } from '@dotcraft/avatar/react'
import { useT } from '../../contexts/LocaleContext'
import { usePetStore } from '../../pet/petStore'
import { useFeatureGuideStore } from '../../stores/featureGuideStore'
import { useUIStore } from '../../stores/uiStore'
import { readConfigValue, useConfig } from '../../stores/configStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useProvidersStore } from '../../stores/providersStore'
import { SUBAGENT_MODELS_MIN_CHATS } from '../../../shared/featureGuides'
import { normalizeProviderList } from '../settings/providerInfo'
import { providerUsable } from '../settings/providers/providerDraft'
import { resolveToolKind } from '../../utils/toolKind'
import { MascotBubble } from '../conversation/MascotBubble'
import { FeatureGuideCapture, FeatureGuideCoachmark, FeatureGuideMedia, FeatureGuideSpotlight } from './FeatureGuide'
import creatorDark from '../../assets/featureGuides/creator-dark.webp'
import creatorLight from '../../assets/featureGuides/creator-light.webp'
import subagentModelsDark from '../../assets/featureGuides/subagent-models-dark.webp'
import subagentModelsLight from '../../assets/featureGuides/subagent-models-light.webp'
import styles from './FeatureGuide.module.css'

const CREATOR_MEDIA = { light: creatorLight, dark: creatorDark }
const SUBAGENT_MODELS_MEDIA = { light: subagentModelsLight, dark: subagentModelsDark }
let subagentModelsMounts = 0

export function CreatorFeatureGuide(): JSX.Element | null {
  const t = useT()
  const active = useFeatureGuideStore((state) => state.active === 'creator')
  const close = useFeatureGuideStore((state) => state.close)
  if (!active) return null
  return (
    <FeatureGuideSpotlight
      media={(
        <FeatureGuideMedia focus="30% 50%" height={208}>
          <FeatureGuideCapture source={CREATOR_MEDIA} style={{ left: '50%', bottom: -18, width: 400, transform: 'translateX(-50%)' }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.creator.title')}
      body={t('featureGuides.creator.body')}
      points={[
        { icon: <Hammer size={16} />, title: t('featureGuides.creator.build'), body: t('featureGuides.creator.buildBody') },
        { icon: <PlugZap size={16} />, title: t('featureGuides.creator.activate'), body: t('featureGuides.creator.activateBody') },
        { icon: <ScanSearch size={16} />, title: t('featureGuides.creator.check'), body: t('featureGuides.creator.checkBody') }
      ]}
      primaryLabel={t('featureGuides.creator.action')}
      onPrimary={() => close('creator', true)}
      onClose={() => close('creator', false)}
    />
  )
}

export function useSubagentChatSignal(threadId: string): void {
  const spawned = useConversationStore((state) =>
    state.turnStatus === 'idle' && (state.turns.at(-1)?.items.some((item) => resolveToolKind(item) === 'spawnAgent') ?? false))
  useEffect(() => {
    if (spawned && threadId) useFeatureGuideStore.getState().noteSubagentChat(threadId)
  }, [spawned, threadId])
}

function useSubagentModelsGuideEligible(): boolean {
  const config = useConfig()
  const choices = readConfigValue(config, 'SubAgent.Models')
  const listResult = useProvidersStore((state) => state.listResult)
  const subagentChats = useFeatureGuideStore((state) => state.settings?.subagentChats.length ?? 0)
  const idle = useConversationStore((state) => state.turnStatus === 'idle')
  const usableProviders = useMemo(() => normalizeProviderList(listResult).filter(providerUsable).length, [listResult])
  const hasChoices = Array.isArray(choices) && choices.length > 0
  useEffect(() => {
    if (hasChoices) useFeatureGuideStore.getState().settle('subagentModels')
  }, [hasChoices])
  return idle && config != null && !hasChoices && usableProviders >= 2 && subagentChats >= SUBAGENT_MODELS_MIN_CHATS
}

export function SubagentModelsFeatureGuide({ style }: { style: CSSProperties }): JSX.Element | null {
  const t = useT()
  const eligible = useSubagentModelsGuideEligible()
  const active = useFeatureGuideStore((state) => state.active === 'subagentModels')
  const close = useFeatureGuideStore((state) => state.close)
  useEffect(() => {
    if (eligible) useFeatureGuideStore.getState().request('subagentModels')
  }, [eligible])
  useEffect(() => {
    subagentModelsMounts += 1
    return () => {
      subagentModelsMounts -= 1
      queueMicrotask(() => {
        if (subagentModelsMounts === 0 && useFeatureGuideStore.getState().active === 'subagentModels') {
          useFeatureGuideStore.getState().close('subagentModels', false)
        }
      })
    }
  }, [])
  if (!active) return null
  return (
    <FeatureGuideCoachmark
      style={style}
      media={(
        <FeatureGuideMedia focus="15% 40%" height={140}>
          <FeatureGuideCapture source={SUBAGENT_MODELS_MEDIA} style={{ left: 20, top: 22, width: 360 }} />
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.subagentModels.title')}
      body={t('featureGuides.subagentModels.body')}
      primaryLabel={t('featureGuides.subagentModels.action')}
      onPrimary={() => {
        close('subagentModels', true)
        useUIStore.getState().openSettings('llmService')
      }}
      onClose={() => close('subagentModels', false)}
    />
  )
}

export function FindOutWhyFeatureGuide({ onLookIntoIt }: { onLookIntoIt: () => void }): JSX.Element {
  const t = useT()
  const appearance = usePetStore((state) => state.appearance)
  const close = useFeatureGuideStore((state) => state.close)
  const dismiss = useCallback(() => close('findOutWhy', false), [close])
  return (
    <FeatureGuideCoachmark
      style={{ position: 'relative' }}
      media={(
        <FeatureGuideMedia focus="70% 30%" height={188}>
          <div className={styles.mascotStage}>
            <MascotBubble tone="error" title={t('mascot.error.title')} body={t('mascot.error.body')} />
            <span className={styles.mascot}>
              <AppearanceAvatar appearance={appearance} size={40} label="" />
            </span>
          </div>
        </FeatureGuideMedia>
      )}
      title={t('featureGuides.findOutWhy.title')}
      body={t('featureGuides.findOutWhy.body')}
      primaryLabel={t('featureGuides.findOutWhy.action')}
      onPrimary={onLookIntoIt}
      onClose={dismiss}
    />
  )
}
