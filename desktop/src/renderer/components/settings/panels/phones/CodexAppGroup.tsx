import { useCallback, useEffect, useState, type JSX } from 'react'
import { Plus } from 'lucide-react'

import { SettingsEmptyRow, SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { ActionTooltip } from '../../../ui/ActionTooltip'
import { Button } from '../../../ui/Button'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SkeletonRow } from '../../../ui/Skeleton'
import { useT } from '../../../../contexts/LocaleContext'
import { connectCodexRemote, useCodexRemoteStore } from '../../../../stores/codexRemoteStore'
import { addToast } from '../../../../stores/toastStore'
import { useUIStore } from '../../../../stores/uiStore'
import type { CodexRemoteClient } from '../../../../../shared/codexRemote'
import { CodexClientRow, codexClientName } from './CodexClientRow'
import { CodexPairDialog } from './CodexPairDialog'
import { PhonesNotice } from './PhonesSegment'

export function CodexAppGroup(): JSX.Element | null {
  const t = useT()
  const status = useCodexRemoteStore((state) => state.status)
  const loadFailure = useCodexRemoteStore((state) => state.loadFailure)
  const pendingEnabled = useCodexRemoteStore((state) => state.pendingEnabled)
  const clients = useCodexRemoteStore((state) => state.clients)
  const clientsFailure = useCodexRemoteStore((state) => state.clientsFailure)
  const revokingClientId = useCodexRemoteStore((state) => state.revokingClientId)
  const confirm = useConfirmDialog()
  const [pairing, setPairing] = useState(false)
  const [menu, setMenu] = useState<{ id: string; position: ContextMenuPosition } | null>(null)
  const closePairing = useCallback(() => setPairing(false), [])

  useEffect(() => connectCodexRemote(), [])

  const running = status !== null && status.state !== 'off'
  const on = status?.state === 'on'
  const switching = pendingEnabled !== null
  const failure = loadFailure
    ? { title: t('settings.codexApp.loadFailed'), reason: loadFailure.message || t('settings.phones.hubNoAnswer') }
    : status?.state === 'failed'
      ? { title: t('settings.codexApp.failed.title'), reason: status.failureMessage || t('settings.codexApp.failed.reason') }
      : null

  async function toggle(next: boolean): Promise<void> {
    const failure = await useCodexRemoteStore.getState().setEnabled(next)
    if (failure && !next) addToast(t('settings.codexApp.disableFailed'), 'error')
  }

  async function remove(client: CodexRemoteClient): Promise<void> {
    const name = codexClientName(client) ?? t('settings.codexApp.devices.unknown')
    const confirmed = await confirm({
      title: t('settings.phones.remove.title', { name }),
      message: t('settings.codexApp.devices.remove.message', { name }),
      confirmLabel: t('settings.phones.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    const failure = await useCodexRemoteStore.getState().revokeClient(client.clientId)
    if (failure) addToast(t('settings.phones.remove.failed', { name }), 'error')
  }

  const menuClient = menu ? clients?.find((client) => client.clientId === menu.id) : undefined

  function openProviders(): void {
    useUIStore.getState().setActiveSettingsTab('llmService')
  }

  const addDisabledReason = !status?.signedIn
    ? t('settings.codexApp.access.requiresSignIn')
    : !on
      ? t('settings.codexApp.add.connecting')
      : undefined

  return (
    <>
      {failure && (
        <PhonesNotice
          title={failure.title}
          reason={failure.reason}
          action={
            loadFailure ? (
              <Button variant="secondary" onClick={() => void useCodexRemoteStore.getState().load()}>
                {t('settings.phones.retry')}
              </Button>
            ) : undefined
          }
        />
      )}
      <SettingsGroup
        title={t('settings.codexApp.title')}
        reserveHeaderAction
        headerAction={running && (
          <ActionTooltip label="" disabledReason={addDisabledReason}>
            <Button
              variant="primary"
              iconLeft={<Plus size={15} aria-hidden />}
              aria-label={t('settings.codexApp.add.label')}
              disabled={!on}
              onClick={() => setPairing(true)}
            >
              {t('settings.phones.add')}
            </Button>
          </ActionTooltip>
        )}
      >
        <SettingsRow
          label={t('settings.codexApp.access.label')}
          description={t('settings.codexApp.access.description')}
          control={
            <ActionTooltip
              label=""
              disabledReason={status && !status.signedIn ? t('settings.codexApp.access.requiresSignIn') : undefined}
            >
              <PillSwitch
                checked={pendingEnabled ?? running}
                disabled={switching || !status?.signedIn}
                aria-busy={switching}
                aria-label={t('settings.codexApp.access.label')}
                onChange={(next) => void toggle(next)}
              />
            </ActionTooltip>
          }
        />

        {status && !status.signedIn && (
          <SettingsRow
            label={t('settings.codexApp.signIn.label')}
            description={t('settings.codexApp.signIn.description')}
            control={
              <Button variant="secondary" onClick={openProviders}>
                {t('settings.codexApp.signIn.action')}
              </Button>
            }
          />
        )}

        {on && clientsFailure && (
          <SettingsEmptyRow
            tone="error"
            action={
              <Button variant="secondary" onClick={() => void useCodexRemoteStore.getState().loadClients()}>
                {t('settings.phones.retry')}
              </Button>
            }
          >
            {t('settings.codexApp.devices.loadFailed')}
          </SettingsEmptyRow>
        )}

        {on && !clientsFailure && clients === null && (
          <SettingsRow>
            <div role="status" aria-busy="true" aria-label={t('settings.codexApp.devices.loading')} style={{ flex: 1 }}>
              <SkeletonRow lines={['38%', '56%']} />
            </div>
          </SettingsRow>
        )}

        {on &&
          clients?.map((client) => (
            <CodexClientRow
              key={client.clientId}
              client={client}
              removing={revokingClientId === client.clientId}
              menuOpen={menu?.id === client.clientId}
              onMenu={(anchor) => {
                const rect = anchor.getBoundingClientRect()
                setMenu({ id: client.clientId, position: { x: rect.right - 200, y: rect.bottom + 4 } })
              }}
            />
          ))}

        {menu && menuClient && (
          <ContextMenu
            position={menu.position}
            onClose={() => setMenu(null)}
            items={[{ label: t('settings.phones.remove'), danger: true, onClick: () => void remove(menuClient) }]}
          />
        )}

        {pairing && status && <CodexPairDialog hostName={status.hostName} onClose={closePairing} />}
      </SettingsGroup>
    </>
  )
}
