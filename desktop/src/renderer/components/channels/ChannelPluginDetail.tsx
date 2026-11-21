import { useEffect, useRef, useState } from 'react'
import type { ModuleStatusEntry } from '../../../shared/channelModules'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { addToast } from '../../stores/toastStore'
import { usePluginStore } from '../../stores/pluginStore'
import { mutateDesktopPluginSettings, readDesktopPluginSettings, type PluginSettingsSnapshot } from '../../plugins/desktopPluginSettings'
import { Button } from '../ui/Button'
import { Select } from '../ui/Select'
import { PluginDetailActionsMenu } from '../plugins/PluginDetailActionsMenu'
import { MorphingActionPill } from '../plugins/MorphingActionPill'
import { ChannelModuleDetailPage, type ChannelModuleDetailMode } from './ChannelModuleDetailPage'
import { ModuleConfigForm } from './ModuleConfigForm'
import { ChannelLoginDialog } from './ChannelLoginDialog'
import { useChannelLogin } from './useChannelLogin'
import { capabilityLabels, transportLabel } from './channelInfoLabels'
import {
  channelLogoPath, channelSettingOperations, channelSubtitle, channelTitle, missingChannelSettings,
  type ChannelPlugin, type ChannelStatus
} from './channelPluginModel'
import { channelRequest } from './channelPluginRequests'

export function ChannelPluginDetail({
  plugin, variants, status, active, startInSetup,
  installing, onInstall, onVariantChange, onBack, onRuntimeChange, onConfigChange,
  onRemove, onEnable
}: {
  plugin: ChannelPlugin
  variants: ChannelPlugin[]
  status?: ChannelStatus
  active: boolean
  startInSetup: boolean
  installing: boolean
  onInstall?: () => void
  onVariantChange: (plugin: ChannelPlugin) => Promise<void>
  onBack: () => void
  onRuntimeChange: () => Promise<void>
  onConfigChange: (pluginId: string, missingRequiredFields: string[]) => void
  onRemove?: () => void
  onEnable?: () => Promise<void>
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const marketplace = usePluginStore((s) => s.marketplaces.find((entry) => entry.name === plugin.marketplaceName))
  const epoch = useConnectionStore((s) => s.connectionEpoch)
  const management = useConnectionStore((s) => s.status === 'connected' && s.capabilities?.channelManagement === true)
  const pluginConfiguration = useConnectionStore((s) => s.status === 'connected' && s.capabilities?.pluginConfiguration === true)
  const [mode, setMode] = useState<ChannelModuleDetailMode>(startInSetup ? 'manage' : 'preview')
  const [snapshot, setSnapshot] = useState<PluginSettingsSnapshot | null>(null)
  const [draft, setDraft] = useState<Record<string, unknown>>({})
  const [configError, setConfigError] = useState<string | null>(null)
  const [loadingKey, setLoadingKey] = useState(0)
  const [saving, setSaving] = useState(false)
  const [busy, setBusy] = useState(false)
  const [logs, setLogs] = useState<string[] | null>(null)
  const [logsLoading, setLogsLoading] = useState(false)
  const [attempted, setAttempted] = useState(false)
  const mounted = useRef(false)
  const settingsRead = useRef(0)
  const draftEdited = useRef(false)
  const connectionPending = useRef(false)
  const cancelPending = useRef(false)
  const { id, channel } = plugin
  const title = channelTitle(plugin, locale)
  const subtitle = channelSubtitle(plugin, locale)
  const logoPath = channelLogoPath(plugin)
  const sameWorkspace = (): boolean => useConnectionStore.getState().connectionEpoch === epoch
  const current = (): boolean => mounted.current && sameWorkspace()
  const failed = status?.runtimeState === 'failed'
  const enabled = status?.pluginId === id && status.enabled
  const running = Boolean(enabled && (status.running || status.runtimeState === 'starting' ||
    status.runtimeState === 'running' || status.runtimeState == null))
  const configAllowed = plugin.installed && pluginConfiguration
  const controls = configAllowed && management && plugin.enabled && !channel.compatibilityError
  const canDisconnect = management && enabled
  const login = useChannelLogin(
    active && controls && channel.requiresInteractiveSetup ? id : null,
    Boolean(running) || attempted,
    status?.running === true,
    failed
  )

  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  useEffect(() => {
    if (!configAllowed) return
    const read = ++settingsRead.current
    const isCurrent = (): boolean => settingsRead.current === read &&
      useConnectionStore.getState().connectionEpoch === epoch
    void readDesktopPluginSettings(id).then((result) => {
      if (!isCurrent()) return
      if (!draftEdited.current) {
        setSnapshot(result)
        setDraft(result.workspace)
      }
      setConfigError(null)
    }).catch((err) => {
      if (isCurrent()) setConfigError(errorMessage(err))
    })
    return () => { settingsRead.current += 1 }
  }, [configAllowed, epoch, id, loadingKey])

  function changeMode(next: ChannelModuleDetailMode): void {
    if (next === 'preview') {
      draftEdited.current = false
      setDraft(snapshot?.workspace ?? {})
    }
    setMode(next)
  }

  async function saveConfig(): Promise<void> {
    if (!configAllowed || !snapshot || !current()) return
    settingsRead.current += 1
    setSaving(true)
    try {
      const operations = channelSettingOperations(snapshot, draft)
      const result = operations.length > 0
        ? await mutateDesktopPluginSettings(id, 'workspace', operations)
        : snapshot
      if (!current()) return
      draftEdited.current = false
      setDraft(result.workspace)
      setSnapshot(result)
      onConfigChange(id, missingChannelSettings(channel, result))
      addToast(t(operations.length > 0 && running ? 'channels.modules.configSavedRestart' : 'channels.plugins.saved'), 'success')
      setMode('preview')
    } catch (err) {
      if (current()) addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setSaving(false)
    }
  }

  async function setEnabled(next: boolean): Promise<void> {
    if (busy || connectionPending.current || !current() || (next ? !controls : !canDisconnect)) return
    connectionPending.current = true
    setBusy(true)
    cancelPending.current = false
    try {
      if (next && (!snapshot || missingChannelSettings(channel, snapshot).length > 0)) {
        const read = ++settingsRead.current
        const saved = await readDesktopPluginSettings(id)
        if (!current() || settingsRead.current !== read) return
        const connection = useConnectionStore.getState()
        if (connection.status !== 'connected' || !connection.capabilities?.channelManagement ||
          !connection.capabilities.pluginConfiguration) return
        if (!draftEdited.current) {
          setSnapshot(saved)
          setDraft(saved.workspace)
        }
        setConfigError(null)
        const missing = missingChannelSettings(channel, saved)
        onConfigChange(id, missing)
        if (missing.length > 0) {
          setMode('manage')
          const labels = missing.map((key) => {
            const descriptor = channel.configDescriptors?.find((entry) => entry.key === key)
            return descriptor?.localizedDisplayLabel?.[locale] ?? descriptor?.displayLabel ?? key
          })
          addToast(t('channels.modules.missingRequired', { fields: labels.join(', ') }), 'error')
          return
        }
      }

      setAttempted(next)
      if (!next) login.hide()
      else if (channel.requiresInteractiveSetup) login.show()
      await channelRequest('channel/setEnabled', { name: channel.channelName, pluginId: id, enabled: next })
      if (current() && !cancelPending.current) await onRuntimeChange()
      if (next && cancelPending.current && sameWorkspace()) {
        await channelRequest('channel/setEnabled', { name: channel.channelName, pluginId: id, enabled: false })
        if (current()) await onRuntimeChange()
      }
    } catch (err) {
      if (!current()) return
      if (next) login.hide()
      setAttempted(false)
      addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
    } finally {
      connectionPending.current = false
      setBusy(false)
    }
  }

  async function loadLogs(): Promise<void> {
    setLogsLoading(true)
    try {
      const result = await channelRequest('channel/logs', { name: channel.channelName })
      if (current()) setLogs(result.lines)
    } catch (err) {
      if (current()) addToast(t('channels.loadFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setLogsLoading(false)
    }
  }

  const permissionsNotice = channel.compatibilityError
    ? t('channels.plugins.incompatible', { version: channel.minHostVersion ?? '' })
    : plugin.installed && !(management && pluginConfiguration) ? t('channels.plugins.managementUnavailable')
    : status?.failureCode === 'ChannelPluginNodeUnavailable' ? t('channels.plugins.nodeRequired')
    : status?.failureCode === 'ChannelPluginActivationRequired' ? t('channels.plugins.connectAgain')
    : status?.failureCode === 'ChannelPluginWebSocketRequired' ? t('channels.modules.wsRequired')
    : null
  const capabilities = capabilityLabels(channel.capabilitySummary, t)
  const info = channel.interface
  const moduleStatus: ModuleStatusEntry | undefined = status && {
    processState: failed ? 'crashed' : status.runtimeState === 'starting' ? 'starting' : running ? 'running' : 'stopped',
    connected: status.running,
    failureCode: status.failureCode
  }

  return (
    <>
      <ChannelModuleDetailPage
        title={title}
        subtitle={subtitle}
        logoPath={logoPath}
        previewPrompt={info?.localizedPreviewPrompt?.[locale] ?? info?.previewPrompt ?? subtitle}
        description={info?.localizedLongDescription?.[locale] ?? info?.longDescription ?? subtitle}
        setupSteps={info?.localizedSetupSteps?.[locale] ?? info?.setupSteps}
        infoItems={[
          { label: t('channels.detail.package'), value: channel.packageName },
          { label: t('channels.detail.source'), value: marketplace?.displayName ?? plugin.marketplaceName ?? t('channels.modules.source.user') },
          { label: t('channels.detail.transports'), value: channel.supportedTransports.map(transportLabel).join(', ') },
          ...(capabilities.length ? [{ label: t('channels.detail.capabilities'), value: capabilities.join(', ') }] : [])
        ]}
        mode={mode}
        onModeChange={changeMode}
        onBack={() => {
          if (mode === 'manage') changeMode('preview')
          onBack()
        }}
        actions={(
          <>
            <PluginDetailActionsMenu
              onManage={configAllowed ? () => setMode('manage') : undefined}
              onRemove={onRemove}
            />
            {(canDisconnect || controls) ? (
              <MorphingActionPill label={t(enabled ? 'appBinding.disconnect' : 'appBinding.connect')}
                loading={busy} onClick={() => void setEnabled(!enabled)} />
            ) : !plugin.installed && plugin.installable && onInstall ? (
              <MorphingActionPill label={t('plugins.install')} disabled={Boolean(channel.compatibilityError)}
                loading={installing} onClick={onInstall} />
            ) : plugin.installed && !plugin.enabled && onEnable ? (
              <MorphingActionPill label={t('channels.plugins.enable')} loading={busy}
                onClick={() => {
                  setBusy(true)
                  void onEnable().finally(() => setBusy(false))
                }}
              />
            ) : null}
          </>
        )}
        details={variants.length > 1 || permissionsNotice ? (
          <>
            {variants.length > 1 && (
              <Select
                value={plugin.key}
                disabled={busy}
                ariaLabel={t('channels.modules.variant.active')}
                onValueChange={(key) => {
                  const variant = variants.find((entry) => entry.key === key)
                  if (variant) {
                    setBusy(true)
                    void onVariantChange(variant).finally(() => setBusy(false))
                  }
                }}
                options={variants.map((entry) => ({
                  value: entry.key,
                  label: t('channels.modules.variant.option', { name: channelTitle(entry, locale), variant: entry.channel.variant })
                }))}
              />
            )}
            {permissionsNotice && <span role="status">{permissionsNotice}</span>}
          </>
        ) : undefined}
        renderManage={({ onCancel }) => (
          snapshot ? (
            <ModuleConfigForm
              plugin={plugin}
              config={draft}
              configuredSecrets={snapshot.configuredSecrets ?? []}
              onChange={(next) => {
                draftEdited.current = true
                setDraft(next)
              }}
              onSave={() => void saveConfig()}
              saving={saving}
              readOnly={!configAllowed}
              moduleStatus={moduleStatus}
              onStart={controls ? () => void setEnabled(true) : undefined}
              onCancel={onCancel}
              moduleLogLines={logs}
              logsLoading={logsLoading}
              onLoadLogs={management && status ? () => void loadLogs() : undefined}
            />
          ) : configError ? (
            <div role="status">
              <p>{t('channels.loadFailed', { error: configError })}</p>
              <Button variant="secondary" onClick={() => setLoadingKey((key) => key + 1)}>
                {t('channels.login.retry')}
              </Button>
            </div>
          ) : <p role="status">{t('channels.loading')}</p>
        )}
      />
      {login.open && (
        <ChannelLoginDialog
          channelName={title} logoPath={logoPath} phase={login.phase} qrDataUrl={login.qrDataUrl}
          onCancel={() => {
            login.hide()
            setAttempted(false)
            if (connectionPending.current) cancelPending.current = true
            else void setEnabled(false)
          }}
          onDone={login.hide}
          onRetry={() => void setEnabled(true)}
        />
      )}
    </>
  )
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
