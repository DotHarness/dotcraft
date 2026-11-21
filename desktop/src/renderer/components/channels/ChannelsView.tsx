import { useEffect, useState, type ReactNode } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { operationFailureMessage, usePluginStore } from '../../stores/pluginStore'
import { replaceCurrentAppNavigationLocation, runWithoutAppNavigationRecording } from '../../stores/appNavigationStore'
import { readDesktopPluginSettings } from '../../plugins/desktopPluginSettings'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { RefreshIcon } from '../ui/AppIcons'
import { SkeletonCatalogGrid } from '../ui/Skeleton'
import {
  CatalogCompactGrid, CatalogScrollArea, CatalogSearchBox, CatalogSection,
  CatalogToolbarIconButton, CatalogTopBar, styles
} from '../catalog/CatalogSurface'
import { ChannelCatalogItem } from './ChannelCatalogItem'
import { ChannelPluginDetail } from './ChannelPluginDetail'
import { channelRequest } from './channelPluginRequests'
import {
  channelLogoPath, channelStatusLabels, channelSubtitle, channelTitle, deriveChannelStatus,
  groupChannelPlugins, isChannelPlugin, missingChannelSettings, normalizeChannelName, type ChannelPlugin
} from './channelPluginModel'
import { useWorkspaceChannels } from './useWorkspaceChannels'

export function ChannelsView({ navigation, active }: { navigation: ReactNode; active: boolean }): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const confirm = useConfirmDialog()
  const epoch = useConnectionStore((s) => s.connectionEpoch)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const scope = useUIStore((s) => s.selectedChannelKey)
  const select = useUIStore((s) => s.setSelectedChannelKey)
  const pluginStore = usePluginStore()
  const runtime = useWorkspaceChannels(active)
  const [query, setQuery] = useState('')
  const [installing, setInstalling] = useState<string | null>(null)
  const [startInSetup, setStartInSetup] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [readiness, setReadiness] = useState<Record<string, string[]>>({})
  const groups = groupChannelPlugins(pluginStore.plugins, runtime.statuses)
  const selectedPlugin = scope?.startsWith('channel:')
    ? pluginStore.plugins.find((entry) => entry.key === scope.slice('channel:'.length))
    : undefined
  const channelPlugin = selectedPlugin && isChannelPlugin(selectedPlugin) ? selectedPlugin : undefined
  const ready = pluginStore.hasCompleteCatalog
  const manageable = capabilities?.channelManagement === true
  const canConfigure = capabilities?.pluginConfiguration === true
  const canManagePlugins = capabilities?.pluginManagement === true
  const current = (): boolean => useConnectionStore.getState().connectionEpoch === epoch

  useEffect(() => {
    if (!active || !canConfigure) return
    let disposed = false
    for (const plugin of pluginStore.plugins) {
      if (!plugin.installed || !isChannelPlugin(plugin)) continue
      void readDesktopPluginSettings(plugin.id).then((snapshot) => {
        if (!disposed) setReadiness((previous) => ({
          ...previous,
          [plugin.id]: missingChannelSettings(plugin.channel, snapshot)
        }))
      }).catch(() => {})
    }
    return () => { disposed = true }
  }, [active, canConfigure, epoch, pluginStore.plugins])

  useEffect(() => {
    if (scope && ready && !channelPlugin) {
      runWithoutAppNavigationRecording(() => select(null))
      replaceCurrentAppNavigationLocation()
    }
  }, [channelPlugin, ready, scope, select])

  function openPlugin(plugin: ChannelPlugin, setup = false): void {
    setStartInSetup(setup ? plugin.key : null)
    select(`channel:${plugin.key}`)
  }

  async function refresh(): Promise<void> {
    if (refreshing) return
    setRefreshing(true)
    try {
      if (capabilities?.pluginMarketplaces === true) {
        const failures = await pluginStore.refreshMarketplace()
        if (current()) for (const failure of failures) addToast(failure.message, 'error')
      } else if (capabilities?.pluginManagement === true) {
        await pluginStore.fetchPlugins()
      }
      if (current()) await runtime.refreshStatus()
    } catch (err) {
      if (current()) addToast(t('channels.loadFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setRefreshing(false)
    }
  }

  async function install(plugin: ChannelPlugin): Promise<void> {
    if (installing) return
    openPlugin(plugin, true)
    setInstalling(plugin.key)
    try {
      const result = await pluginStore.installPlugin(plugin.key)
      if (!current()) return
      if (result.outcome === 'notApplied') {
        setStartInSetup(null)
        addToast(operationFailureMessage(result) ?? t('plugins.installFailed'), 'error')
        return
      }
      await pluginStore.fetchPlugins()
      if (current()) addToast(t('plugins.installSuccess', { name: channelTitle(plugin, locale) }), 'success')
    } catch (err) {
      if (!current()) return
      setStartInSetup(null)
      addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setInstalling(null)
    }
  }

  async function remove(plugin: ChannelPlugin): Promise<void> {
    const name = channelTitle(plugin, locale)
    if (!await confirm({
      title: t('plugins.uninstallConfirm.title', { name }),
      message: t('channels.plugins.uninstallMessage', { name }),
      confirmLabel: t('plugins.uninstall'),
      cancelLabel: t('common.cancel'),
      danger: true
    }) || !current()) return
    try {
      const result = await pluginStore.removePlugin(plugin.id)
      if (!current()) return
      if (result.outcome === 'notApplied') {
        addToast(operationFailureMessage(result) ?? t('plugins.uninstallFailed'), 'error')
        return
      }
      setStartInSetup(null)
      await pluginStore.fetchPlugins()
      if (current()) {
        await runtime.refreshStatus()
        addToast(t('plugins.uninstallSuccess', { name }), 'success')
      }
    } catch (err) {
      if (current()) addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
    }
  }

  async function chooseVariant(plugin: ChannelPlugin): Promise<void> {
    const previous = statusFor(plugin.channel.channelName)
    if (!manageable || !plugin.installed) {
      openPlugin(plugin)
      return
    }
    try {
      if (previous?.pluginId !== plugin.id) {
        await channelRequest('channel/setEnabled', {
          name: plugin.channel.channelName, pluginId: plugin.id, enabled: false, select: true
        })
      }
      if (!current()) return
      openPlugin(plugin)
      await runtime.refreshStatus()
    } catch (err) {
      if (current()) addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
    }
  }

  function statusFor(channelName: string) {
    return runtime.statuses?.get(normalizeChannelName(channelName))
  }

  if (channelPlugin) {
    const status = statusFor(channelPlugin.channel.channelName)
    const selected = status?.pluginId === channelPlugin.id
    const variants = groups.find((variants) => variants.some((entry) => entry.key === channelPlugin.key)) ?? []
    return <ChannelPluginDetail
      key={`${channelPlugin.key}:${channelPlugin.installed}:${channelPlugin.version ?? ''}`}
      plugin={channelPlugin}
      variants={variants}
      status={selected ? status : undefined}
      active={active}
      startInSetup={channelPlugin.installed && startInSetup === channelPlugin.key && canConfigure}
      installing={installing === channelPlugin.key}
      onInstall={canManagePlugins ? () => void install(channelPlugin) : undefined}
      onVariantChange={chooseVariant}
      onBack={() => select(null)}
      onRuntimeChange={runtime.refreshStatus}
      onConfigChange={(id, missing) => setReadiness((previous) => ({ ...previous, [id]: missing }))}
      onRemove={canManagePlugins && channelPlugin.installed && channelPlugin.removable ? () => void remove(channelPlugin) : undefined}
      onEnable={canManagePlugins && channelPlugin.installed && !channelPlugin.enabled ? async () => {
        try {
          const result = await pluginStore.togglePluginEnabled(channelPlugin.id, true)
          if (current() && result.outcome === 'notApplied') {
            addToast(operationFailureMessage(result) ?? t('plugins.updateFailed'), 'error')
          }
        } catch (err) {
          if (current()) addToast(t('channels.saveFailed', { error: errorMessage(err) }), 'error')
        }
      } : undefined}
    />
  }

  const matches = (...parts: string[]): boolean => parts.join(' ').toLowerCase().includes(query.trim().toLowerCase())
  const pluginItems = groups.flatMap((group) => {
    const plugin = group[0]
    const title = channelTitle(plugin, locale)
    const subtitle = channelSubtitle(plugin, locale)
    if (!matches(title, subtitle, ...group.map(({ id }) => id))) return []
    const observed = statusFor(plugin.channel.channelName)
    const selected = observed?.pluginId === plugin.id
    const configured = readiness[plugin.id]?.length === 0 && (!plugin.channel.requiresInteractiveSetup || selected)
    const status = deriveChannelStatus(selected ? observed : undefined, configured)
    return [{ installed: plugin.installed, item: (
      <ChannelCatalogItem
        key={plugin.channel.channelName}
        title={title}
        subtitle={subtitle}
        badgeText={group.length > 1 ? String(group.length) : undefined}
        logoPath={channelLogoPath(plugin)}
        status={status}
        statusLabel={t(channelStatusLabels[status])}
        active={false}
        onOpen={() => openPlugin(plugin)}
        action={canManagePlugins && !plugin.installed && plugin.installable && !plugin.channel.compatibilityError ? {
          label: t('plugins.install'), run: () => void install(plugin), loading: installing === plugin.key
        } : plugin.installed && canConfigure && status === 'notConfigured' ? {
          label: t('channels.plugins.setUp'), run: () => openPlugin(plugin, true)
        } : undefined}
      />
    ) }]
  })
  const missingItems = [...(runtime.statuses?.values() ?? [])].filter((entry) =>
    entry.pluginId && ready && matches(entry.name, entry.pluginId) &&
    !pluginStore.plugins.some((plugin) => isChannelPlugin(plugin) && plugin.id === entry.pluginId))
  const installedItems = pluginItems.filter((entry) => entry.installed)
  const availableItems = pluginItems.filter((entry) => !entry.installed)
  const error = pluginStore.error
  const loading = pluginStore.loading || runtime.loading

  return (
    <div style={styles.page}>
      <CatalogTopBar navigation={navigation} actions={(
        <CatalogToolbarIconButton label={t('channels.modules.refresh')} onClick={() => void refresh()}
          icon={<RefreshIcon size={15} />} />
      )} />
      <header style={styles.browseHeader}>
        <h1 style={styles.heroTitle}>{t('channels.heroTitle')}</h1>
        <div style={styles.searchRow}>
          <CatalogSearchBox value={query} placeholder={t('channels.searchPlaceholder')} onChange={setQuery} />
        </div>
      </header>
      <CatalogScrollArea>
        {installedItems.length > 0 && (
          <CatalogSection title={t('channels.plugins.installed')}>
            <CatalogCompactGrid>{installedItems.map(({ item }) => item)}</CatalogCompactGrid>
          </CatalogSection>
        )}
        {availableItems.length > 0 && (
          <CatalogSection title={t('channels.plugins.available')}>
            <CatalogCompactGrid>{availableItems.map(({ item }) => item)}</CatalogCompactGrid>
          </CatalogSection>
        )}
        {missingItems.map((entry) => (
          <p key={entry.name} style={styles.emptyText} role="status">
            {t('channels.plugins.missing', { name: entry.name })}
          </p>
        ))}
        {pluginItems.length + missingItems.length === 0 && (
          loading ? <SkeletonCatalogGrid count={4} ariaLabel={t('channels.loading')} />
            : <p style={styles.emptyText}>{t('channels.modules.empty')}</p>
        )}
        {(error || runtime.statusError) && (
          <p style={styles.emptyText} role="status">
            {error ? t('channels.loadFailed', { error }) : t('channels.statusUnavailable')}
          </p>
        )}
      </CatalogScrollArea>
    </div>
  )
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
