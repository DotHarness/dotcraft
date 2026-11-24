import { MascotOutfit, PersonalMascot, SidebarRow } from '@dotcraft/plugin'
import type {
  DesktopLocalizedText,
  DesktopPluginHost,
  DesktopPluginOwnedThread,
  DesktopPluginSpaceContribution,
  DesktopPluginSpaceSidebarProps,
  DesktopPluginSurfaceWrapperProps
} from '@dotcraft/plugin'
import { OratorioBatonIcon } from './OratorioBatonIcon'
import { CONDUCTOR_OUTFIT } from './conductorOutfit'
import conductorInstructions from './prompts/conductor.md?raw'
import { BoardIcon, ConductorIcon } from './OratorioSpaceIcons'
import { oratorioLocalizedText, useOratorioI18n } from './oratorio-i18n'
import './oratorio.css'

export const ORATORIO_APP_ID = 'com.dotharness.oratorio'
const CONDUCTOR_KEY = 'conductor'
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
  openingConductor = conductor.threadId
  await host.navigation.openThread(conductor.threadId, conductor.workspacePath)
  settleConductorOpen(host)
}

let openingConductor: string | null = null
let bindingConductor: string | null = null

export function settleConductorOpen(host: DesktopPluginHost): void {
  if (!openingConductor || host.session.threadId !== openingConductor) return
  const threadId = openingConductor
  openingConductor = null
  reconcileConductorBinding(host, threadId)
}

function reconcileConductorBinding(host: DesktopPluginHost, threadId: string): void {
  if (bindingConductor === threadId) return
  bindingConductor = threadId
  void bindConductor(host, threadId).catch((error: unknown) => {
    host.ui.showToast({ message: error instanceof Error ? error.message : String(error), tone: 'error' })
  }).finally(() => {
    if (bindingConductor === threadId) bindingConductor = null
  })
}

async function bindConductor(host: DesktopPluginHost, threadId: string): Promise<void> {
  const { bindings } = await host.appServer.request('thread/appBindings/list', { threadId })
  const existing = (bindings ?? []).find((binding) => binding.appId === ORATORIO_APP_ID
    && binding.state !== 'revoked' && binding.state !== 'cancelled')
  if (existing && existing.state !== 'failed') return
  const status = await host.appBindings.getConnectionStatus(ORATORIO_APP_ID)
  if (status.state !== 'connected') return
  if (existing) await host.appServer.request('thread/appBindings/revoke', { threadId, bindingId: existing.bindingId, reason: 'activation_failed' })
  await host.appBindings.enableThread(threadId, ORATORIO_APP_ID)
}

export function conductorSpace(label: DesktopLocalizedText): DesktopPluginSpaceContribution {
  return {
    id: SPACE_ID,
    label,
    icon: OratorioBatonIcon,
    order: 10,
    views: ['board'],
    sidebar: ConductorSpaceSidebar,
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

function ConductorSpaceSidebar({ host, location }: DesktopPluginSpaceSidebarProps): JSX.Element {
  const { t } = useOratorioI18n()
  return (
    <>
      <SidebarRow
        icon={<ConductorIcon />}
        label={t('conductor')}
        active={location.kind === 'thread'}
        threadId={findConductor(host)?.threadId}
        onClick={() => void openConductor(host)}
      />
      <SidebarRow
        icon={<BoardIcon />}
        label={t('board')}
        active={location.kind === 'view'}
        onClick={() => host.navigation.openMainView('board')}
      />
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
