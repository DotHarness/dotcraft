import { useEffect, useState } from 'react'
import { MascotOutfit, PersonalMascot, SidebarRow, Spinner } from '@dotcraft/plugin'
import type {
  DesktopLocalizedText,
  DesktopPluginHost,
  DesktopPluginLinkedChat,
  DesktopPluginOwnedThread,
  DesktopPluginSpaceContribution,
  DesktopPluginSpaceSidebarProps,
  DesktopPluginSurfaceWrapperProps
} from '@dotcraft/plugin'
import { OratorioBatonIcon } from './OratorioBatonIcon'
import { CONDUCTOR_OUTFIT } from './conductorOutfit'
import {
  buildConductorMenuEntries,
  useConductorThread,
  type ConductorLifecycleHooks
} from './conductor-lifecycle'
import { CONDUCTOR_KEY } from './conductor-proactive'
import conductorInstructions from './prompts/conductor.md?raw'
import { BoardIcon, ConductorIcon } from './OratorioSpaceIcons'
import { oratorioLocalizedText, useOratorioI18n } from './oratorio-i18n'
import './oratorio.css'

const SPACE_ID = 'conductor'
const CONDUCTOR_TITLE = 'Oratorio'

const CONDUCTOR_INSTRUCTIONS = conductorInstructions.trimEnd()

export function findConductor(host: DesktopPluginHost): DesktopPluginOwnedThread | null {
  return host.threads.findOwned(CONDUCTOR_KEY)
}

export async function openConductor(host: DesktopPluginHost): Promise<void> {
  const conductor = await host.threads.ensureOwned({
    key: CONDUCTOR_KEY,
    space: SPACE_ID,
    title: CONDUCTOR_TITLE,
    config: {
      developerInstructions: CONDUCTOR_INSTRUCTIONS,
      approvalPolicy: 'autoApprove'
    }
  })
  await host.navigation.openThread(conductor.threadId, conductor.workspacePath)
}

export function conductorSpace(
  label: DesktopLocalizedText,
  hooks?: ConductorLifecycleHooks
): DesktopPluginSpaceContribution {
  const Sidebar = (props: DesktopPluginSpaceSidebarProps): JSX.Element => (
    <ConductorSpaceSidebar {...props} hooks={hooks} />
  )
  return {
    id: SPACE_ID,
    label,
    icon: OratorioBatonIcon,
    order: 10,
    views: ['board'],
    sidebar: Sidebar,
    contextMenu: (host) => buildConductorMenuEntries(host, hooks),
    guide: {
      title: oratorioLocalizedText('guideTitle'),
      body: oratorioLocalizedText('introDescription'),
      action: oratorioLocalizedText('guideAction'),
      media: ConductorGuideMedia
    },
    open: openConductor
  }
}

function ConductorGuideMedia(): JSX.Element {
  return (
    <MascotOutfit {...CONDUCTOR_OUTFIT}>
      <PersonalMascot size={120} />
    </MascotOutfit>
  )
}

const GUIDE_LOOKBACK_MS = 7 * 24 * 60 * 60 * 1000

export async function requestConductorGuide(host: DesktopPluginHost): Promise<void> {
  if (host.session.busy || findConductor(host)) return
  const projects = await host.workspaces.listLocalProjects()
  const since = Date.now() - GUIDE_LOOKBACK_MS
  const recent = projects.filter(({ lastOpenedAt }) => lastOpenedAt && Date.parse(lastOpenedAt) >= since)
  if (recent.length >= 3) host.ui.requestSpaceGuide(SPACE_ID)
}

function useConductorLinkedChats(host: DesktopPluginHost, threadId: string | undefined): readonly DesktopPluginLinkedChat[] {
  const [chats, setChats] = useState<readonly DesktopPluginLinkedChat[]>([])

  useEffect(() => {
    if (!threadId) {
      setChats([])
      return
    }
    let active = true
    void host.threads.linked(threadId).then((list) => {
      if (active) setChats(list)
    }, () => {})
    const unsubscribe = host.threads.onLinkedChange(threadId, (list) => {
      if (active) setChats(list)
    })
    return () => {
      active = false
      unsubscribe()
    }
  }, [host, threadId])

  return chats
}

function FollowedChatStatus({ state, waitingLabel }: { state: DesktopPluginLinkedChat['state']; waitingLabel: string }): JSX.Element | null {
  if (state === 'waiting') {
    return (
      <span className="dc-status-badge" data-size="compact" data-tone="warning">
        <span className="dc-status-badge__label">{waitingLabel}</span>
      </span>
    )
  }
  if (state === 'working') {
    return (
      <span className="dc-status-indicator">
        <Spinner label="Working" />
      </span>
    )
  }
  if (state === 'failed') {
    return (
      <span className="dc-status-indicator">
        <span className="dc-status-indicator__dot" data-tone="error" />
      </span>
    )
  }
  if (state === 'done') {
    return (
      <span className="dc-status-indicator">
        <span className="dc-status-indicator__dot" data-tone="unread" />
      </span>
    )
  }
  return null
}

function ConductorSpaceSidebar({
  host,
  location,
  hooks
}: DesktopPluginSpaceSidebarProps & { hooks?: ConductorLifecycleHooks }): JSX.Element {
  const { t } = useOratorioI18n()
  const conductor = useConductorThread(host)
  const conductorThreadId = conductor?.threadId
  const linkedChats = useConductorLinkedChats(host, conductorThreadId)
  return (
    <>
      <SidebarRow
        icon={<ConductorIcon />}
        label={t('conductor')}
        active={location.kind === 'thread'}
        threadId={conductorThreadId}
        contextMenu={conductor ? buildConductorMenuEntries(host, hooks) : undefined}
        onClick={() => void openConductor(host)}
      />
      <SidebarRow
        icon={<BoardIcon />}
        label={t('board')}
        active={location.kind === 'view'}
        onClick={() => host.navigation.openMainView('board')}
      />
      {linkedChats.length > 0 && (
        <div className="ora-following">
          <div className="ora-following__heading">{t('following')}</div>
          {linkedChats.map((chat) => (
            <button
              key={`${chat.projectId}:${chat.threadId}`}
              type="button"
              className="ora-following__row"
              onClick={() => void host.navigation.openThread(chat.threadId, chat.workspacePath)}
            >
              <span className="ora-following__meta">
                <span className="ora-following__title">{chat.title}</span>
                <span className="ora-following__project">{chat.projectName}</span>
              </span>
              <span className="ora-following__status">
                <FollowedChatStatus state={chat.state} waitingLabel={t('waitingOnYou')} />
              </span>
            </button>
          ))}
        </div>
      )}
    </>
  )
}

export function ConductorPermissions({ host, context, children }: DesktopPluginSurfaceWrapperProps<'composer.toolbar.permissions'>): JSX.Element | null {
  return context.threadId && context.threadId === findConductor(host)?.threadId ? null : <>{children}</>
}

export function ConductorMascot({ host, context, children }: DesktopPluginSurfaceWrapperProps<'composer.mascot'>): JSX.Element {
  if (!context.threadId || context.threadId !== findConductor(host)?.threadId) return <>{children}</>
  return <MascotOutfit {...CONDUCTOR_OUTFIT}>{children}</MascotOutfit>
}
