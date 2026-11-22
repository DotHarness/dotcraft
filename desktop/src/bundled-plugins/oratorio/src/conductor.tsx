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
}

export async function bindConductor(host: DesktopPluginHost, threadId: string): Promise<boolean> {
  const { bindings } = await host.appServer.request('thread/appBindings/list', { threadId })
  if ((bindings ?? []).some((binding) => binding.appId === ORATORIO_APP_ID && binding.state !== 'revoked' && binding.state !== 'failed')) return true
  const status = await host.appBindings.getConnectionStatus(ORATORIO_APP_ID)
  if (status.state !== 'connected') return false
  await host.appServer.request('thread/appBindings/enable', { threadId, appId: ORATORIO_APP_ID })
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
