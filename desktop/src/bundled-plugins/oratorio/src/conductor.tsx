import { MascotOutfit, SidebarRow } from '@dotcraft/plugin'
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
import { useOratorioI18n } from './oratorio-i18n'
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
  await host.navigation.openThread(conductor.threadId, conductor.workspacePath)
  if (host.session.threadId === conductor.threadId) reconcileConductorBinding(host, conductor.threadId)
}

let boundConductor: string | null = null

export function reconcileConductorBinding(host: DesktopPluginHost, threadId: string): void {
  if (boundConductor === threadId) return
  boundConductor = threadId
  void bindConductor(host, threadId).then((settled) => {
    if (!settled) boundConductor = null
  }, (error: unknown) => {
    boundConductor = null
    host.ui.showToast({ message: error instanceof Error ? error.message : String(error), tone: 'error' })
  })
}

async function bindConductor(host: DesktopPluginHost, threadId: string): Promise<boolean> {
  const { bindings } = await host.appServer.request('thread/appBindings/list', { threadId })
  const existing = (bindings ?? []).find((binding) => binding.appId === ORATORIO_APP_ID
    && binding.state !== 'revoked' && binding.state !== 'cancelled')
  if (existing?.state === 'active') return true
  if (existing && existing.state !== 'failed') return false
  const status = await host.appBindings.getConnectionStatus(ORATORIO_APP_ID)
  if (status.state !== 'connected') return false
  if (existing) await host.appServer.request('thread/appBindings/revoke', { threadId, bindingId: existing.bindingId, reason: 'activation_failed' })
  await host.appBindings.enableThread(threadId, ORATORIO_APP_ID)
  return true
}

export function conductorSpace(label: DesktopLocalizedText): DesktopPluginSpaceContribution {
  return {
    id: SPACE_ID,
    label,
    icon: OratorioBatonIcon,
    order: 10,
    views: ['board'],
    sidebar: ConductorSpaceSidebar,
    open: openConductor
  }
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
