import { useEffect, useMemo, useState } from 'react'
import type { CSSProperties, MouseEvent } from 'react'
import { Box, ChevronLeft, Code2, Ellipsis, ExternalLink, Link, MessageCircle, Server, Settings, Trash2, Wrench } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { usePluginStore, type PluginDiagnosticEntry, type PluginEntry } from '../../stores/pluginStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore, type PluginCatalogSurface } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { PillSwitch } from '../ui/PillSwitch'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SkillsManageList, SkillsView, filterLocalSkills } from '../skills/SkillsView'
import {
  CatalogFilterMenu,
  CatalogSearchBox,
  CatalogTabs,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { RefreshIcon } from '../ui/AppIcons'
import { PluginCatalogItem, PluginIcon, pluginSourceLabel, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { PluginInstallDialog } from './PluginInstallDialog'
import { AppBindingPanel } from './AppBindingPanel'
import { getPluginDesktopExtensionContents } from '../../utils/pluginDesktopExtensions'

type Surface = PluginCatalogSurface
type PluginMode = 'browse' | 'manage'
type PublisherFilter = 'dotcraft' | 'all'
type CategoryFilter = string
const DOTCRAFT_PLUGIN_FALLBACK_URL = 'https://github.com/DotHarness/dotcraft'
const FIXED_PLUGIN_CATEGORIES = [
  'coding',
  'design',
  'engineering',
  'lifestyle',
  'productivity',
  'research',
  'uncategorized'
]

export function PluginsView(): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const pluginManagement = capabilities?.pluginManagement === true
  const {
    plugins,
    diagnostics,
    loading,
    error,
    fetchPlugins,
    selectedPlugin,
    detailLoading,
    selectPlugin,
    clearSelection,
    installPlugin,
    removePlugin,
    togglePluginEnabled
  } = usePluginStore()
  const {
    skills,
    loading: skillsLoading,
    error: skillsError,
    fetchSkills,
    toggleSkillEnabled
  } = useSkillsStore()
  const surface = useUIStore((s) => s.pluginCatalogSurface)
  const setSurface = useUIStore((s) => s.setPluginCatalogSurface)
  const [mode, setMode] = useState<PluginMode>('browse')
  const [query, setQuery] = useState('')
  const [skillManageQuery, setSkillManageQuery] = useState('')
  const [publisherFilter, setPublisherFilter] = useState<PublisherFilter>('all')
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all')
  const [savedPluginId, setSavedPluginId] = useState<string | null>(null)
  const [savedSkillName, setSavedSkillName] = useState<string | null>(null)
  const [installTarget, setInstallTarget] = useState<PluginEntry | null>(null)
  const [installingId, setInstallingId] = useState<string | null>(null)
  const [enablingLspId, setEnablingLspId] = useState<string | null>(null)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)

  useEffect(() => {
    if (pluginManagement) void fetchPlugins()
  }, [fetchPlugins, pluginManagement])

  useEffect(() => {
    if (!pluginManagement) return
    const handleFocus = (): void => {
      void fetchPlugins()
    }
    window.addEventListener('focus', handleFocus)
    return () => window.removeEventListener('focus', handleFocus)
  }, [fetchPlugins, pluginManagement])

  useEffect(() => {
    if (!savedPluginId) return
    const timer = window.setTimeout(() => setSavedPluginId(null), 1500)
    return () => window.clearTimeout(timer)
  }, [savedPluginId])

  useEffect(() => {
    if (!savedSkillName) return
    const timer = window.setTimeout(() => setSavedSkillName(null), 1500)
    return () => window.clearTimeout(timer)
  }, [savedSkillName])

  useEffect(() => {
    if (mode === 'manage') void fetchSkills()
  }, [fetchSkills, mode])

  const browsePlugins = useMemo(
    () => filterPlugins(plugins, query, publisherFilter, categoryFilter),
    [plugins, query, publisherFilter, categoryFilter]
  )
  const managePlugins = useMemo(() => filterPlugins(plugins, query, 'all', 'all'), [plugins, query])
  const manageSkills = useMemo(
    () => filterLocalSkills(skills, skillManageQuery, 'all'),
    [skills, skillManageQuery]
  )
  const visibleDiagnostics = useMemo(() => filterVisibleDiagnostics(diagnostics), [diagnostics])
  const categoryOptions = useMemo(() => buildCategoryOptions(plugins, t), [plugins, t])
  const sections = useMemo(() => buildSections(browsePlugins, categoryFilter, t), [browsePlugins, categoryFilter, t])
  const installDialog = installTarget ? (
    <PluginInstallDialog
      plugin={installTarget}
      installing={installingId === installTarget.id}
      onClose={() => setInstallTarget(null)}
      onInstall={async () => {
        try {
          setInstallingId(installTarget.id)
          const keepOpenForAppSetup = (installTarget.apps ?? []).length > 0
          await installPlugin(installTarget.id)
          await fetchSkills()
          await fetchPlugins()
          await selectPlugin(installTarget.id)
          setSavedPluginId(installTarget.id)
          setInstallTarget(keepOpenForAppSetup
            ? { ...installTarget, installed: true, enabled: true, installable: false }
            : null)
          addToast(t('plugins.installSuccess'), 'success')
        } catch {
          addToast(t('plugins.installFailed'), 'error')
        } finally {
          setInstallingId(null)
        }
      }}
    />
  ) : null

  if (surface === 'skills' && mode !== 'manage') {
    return (
      <div style={page}>
        <SurfaceTabs value={surface} onChange={setSurface} />
        <div style={{ flex: 1, minHeight: 0 }}>
          <SkillsView onManage={() => setMode('manage')} />
        </div>
      </div>
    )
  }

  if (selectedPlugin) {
    return (
      <>
        <PluginDetailView
          plugin={selectedPlugin}
          loading={detailLoading}
          saved={savedPluginId === selectedPlugin.id}
          onBack={() => clearSelection()}
          onSurfaceChange={(next) => {
            clearSelection()
            setSurface(next)
          }}
          onInstall={() => setInstallTarget(selectedPlugin)}
          onRemove={async () => {
            const pluginName = pluginTitle(selectedPlugin)
            const ok = await confirm({
              title: t('plugins.removeConfirm.title', { name: pluginName }),
              message: t('plugins.removeConfirm.message', {
                name: pluginName,
                path: selectedPlugin.rootPath || `.craft/plugins/${selectedPlugin.id}`
              }),
              confirmLabel: t('plugins.removeFromDotCraft'),
              cancelLabel: t('common.cancel'),
              danger: true
            })
            if (!ok) return

            try {
              await removePlugin(selectedPlugin.id)
              await fetchPlugins()
              await fetchSkills()
              setSavedPluginId(selectedPlugin.id)
              addToast(t('plugins.removeSuccess'), 'success')
            } catch {
              addToast(t('plugins.removeFailed'), 'error')
            }
          }}
          onToggle={async (enabled) => {
            try {
              await togglePluginEnabled(selectedPlugin.id, enabled)
              await selectPlugin(selectedPlugin.id)
              await fetchSkills()
              setSavedPluginId(selectedPlugin.id)
            } catch {
              addToast(t('plugins.updateFailed'), 'error')
            }
          }}
          enablingLsp={enablingLspId === selectedPlugin.id}
          onEnableLsp={async () => {
            try {
              setEnablingLspId(selectedPlugin.id)
              await window.api.appServer.sendRequest('workspace/config/update', { toolsLspEnabled: true })
              await fetchPlugins()
              await selectPlugin(selectedPlugin.id)
              setSavedPluginId(selectedPlugin.id)
              addToast(t('plugins.lsp.enableSuccess'), 'success')
            } catch {
              addToast(t('plugins.lsp.enableFailed'), 'error')
            } finally {
              setEnablingLspId(null)
            }
          }}
          onTryInChat={() => tryPluginInChat(selectedPlugin)}
        />
        {installDialog}
      </>
    )
  }

  if (mode === 'manage') {
    return (
      <>
        <div style={page}>
          <header style={manageHeader}>
            <div style={breadcrumb}>
              <button type="button" onClick={() => setMode('browse')} style={breadcrumbButton}>
                <ChevronLeft size={14} aria-hidden />
                {surface === 'plugins' ? t('plugins.pageTitle') : t('skills.pageTitle')}
              </button>
              <span style={breadcrumbSep}>›</span>
              <span style={breadcrumbCurrent}>{t('plugins.manage')}</span>
            </div>
            {surface === 'plugins' ? (
              <PluginsManageToolbar
                surface={surface}
                plugins={plugins}
                skillsCount={skills.length}
                query={query}
                savedPluginId={savedPluginId}
                onSurfaceChange={setSurface}
                onQueryChange={setQuery}
              />
            ) : (
              <ManageSkillsToolbar
                surface={surface}
                pluginsCount={plugins.length}
                skillsCount={skills.length}
                query={skillManageQuery}
                savedSkillName={savedSkillName}
                onSurfaceChange={setSurface}
                onQueryChange={setSkillManageQuery}
              />
            )}
          </header>
          {surface === 'plugins' ? (
            <PluginsManageList
              pluginManagement={pluginManagement}
              loading={loading}
              error={error}
              diagnostics={visibleDiagnostics}
              plugins={managePlugins}
              onOpen={(plugin) => void selectPlugin(plugin.id)}
              onInstall={setInstallTarget}
              onToggle={async (plugin, enabled) => {
                try {
                  await togglePluginEnabled(plugin.id, enabled)
                  await fetchSkills()
                  setSavedPluginId(plugin.id)
                } catch {
                  addToast(t('plugins.updateFailed'), 'error')
                }
              }}
            />
          ) : (
            <SkillsManageList
              skills={manageSkills}
              loading={skillsLoading}
              error={skillsError}
              onToggleEnabled={async (skill, enabled) => {
                try {
                  await toggleSkillEnabled(skill.name, enabled)
                  setSavedSkillName(skill.name)
                } catch {
                  addToast(t('skills.updateFailed'), 'error')
                }
              }}
            />
          )}
        </div>
        {installDialog}
      </>
    )
  }

  return (
    <div style={page}>
      <SurfaceTabs value={surface} onChange={setSurface} />
      <header style={browseHeader}>
        <div style={topActions}>
          <button type="button" onClick={() => setMode('manage')} style={manageButton}>
            <Settings size={14} aria-hidden />
            <span style={manageButtonLabel}>{t('plugins.manage')}</span>
          </button>
          <ActionTooltip label={t('plugins.moreActions')} placement="bottom">
            <button
              type="button"
              aria-label={t('plugins.moreActions')}
              onClick={(event) => setMenuPosition({ x: event.clientX, y: event.clientY })}
              style={iconButton}
            >
              <Ellipsis size={16} aria-hidden />
            </button>
          </ActionTooltip>
        </div>
        <h1 style={heroTitle}>{t('plugins.heroTitle')}</h1>
        <div style={searchRow}>
          <CatalogSearchBox value={query} placeholder={t('plugins.searchPlaceholder')} onChange={setQuery} />
          <CatalogFilterMenu
            value={publisherFilter}
            ariaLabel={t('plugins.filter.publisher.label')}
            onChange={setPublisherFilter}
            options={[
              { value: 'dotcraft', label: t('plugins.filter.publisher.dotcraft') },
              { value: 'all', label: t('plugins.filter.publisher.all') }
            ]}
          />
          <CatalogFilterMenu
            value={categoryFilter}
            ariaLabel={t('plugins.filter.category.label')}
            onChange={setCategoryFilter}
            options={categoryOptions}
          />
        </div>
      </header>
      <main style={browseMain}>
        {!pluginManagement && <p style={emptyText}>{t('plugins.unavailable')}</p>}
        {loading && <p style={emptyText}>{t('plugins.loading')}</p>}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        <PluginDiagnosticsBanner diagnostics={visibleDiagnostics} />
        {sections.map((section) => (
          <section key={section.key} style={{ marginBottom: '34px' }}>
            <h2 style={sectionTitle}>{section.title}</h2>
            <div style={compactGrid}>
              {section.plugins.map((plugin) => (
                <PluginCatalogItem
                  key={plugin.id}
                  plugin={plugin}
                  tryLabel={t('plugins.tryInChat')}
                  installLabel={t('plugins.install')}
                  onOpen={() => void selectPlugin(plugin.id)}
                  onTryInChat={() => tryPluginInChat(plugin)}
                  onInstall={() => setInstallTarget(plugin)}
                />
              ))}
            </div>
          </section>
        ))}
        {!loading && !error && browsePlugins.length === 0 && <p style={emptyText}>{t('plugins.empty')}</p>}
      </main>
      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('plugins.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: () => void fetchPlugins()
            }
          ]}
        />
      )}
      {installDialog}
    </div>
  )
}

function PluginsManageToolbar({
  surface,
  plugins,
  skillsCount,
  query,
  savedPluginId,
  onSurfaceChange,
  onQueryChange
}: {
  surface: Surface
  plugins: PluginEntry[]
  skillsCount: number
  query: string
  savedPluginId: string | null
  onSurfaceChange: (surface: Surface) => void
  onQueryChange: (query: string) => void
}): JSX.Element {
  const t = useT()

  return (
    <div style={manageToolbar}>
      <ManageSurfaceTabs
        value={surface}
        pluginsCount={plugins.length}
        skillsCount={skillsCount}
        onChange={onSurfaceChange}
      />
      <div style={{ flex: 1 }} />
      {savedPluginId && <span style={savedHint}>{t('settings.savedToast')}</span>}
      <CatalogSearchBox
        value={query}
        placeholder={t('plugins.manage.searchPlaceholder')}
        onChange={onQueryChange}
        style={{ maxWidth: '280px', flex: '0 1 280px' }}
      />
    </div>
  )
}

function ManageSkillsToolbar({
  surface,
  pluginsCount,
  skillsCount,
  query,
  savedSkillName,
  onSurfaceChange,
  onQueryChange
}: {
  surface: Surface
  pluginsCount: number
  skillsCount: number
  query: string
  savedSkillName: string | null
  onSurfaceChange: (surface: Surface) => void
  onQueryChange: (query: string) => void
}): JSX.Element {
  const t = useT()

  return (
    <div style={manageToolbar}>
      <ManageSurfaceTabs
        value={surface}
        pluginsCount={pluginsCount}
        skillsCount={skillsCount}
        onChange={onSurfaceChange}
      />
      <div style={{ flex: 1 }} />
      {savedSkillName && <span style={savedHint}>{t('settings.savedToast')}</span>}
      <CatalogSearchBox
        value={query}
        placeholder={t('skills.manage.searchPlaceholder')}
        onChange={onQueryChange}
        style={{ maxWidth: '280px', flex: '0 1 280px' }}
      />
    </div>
  )
}

function ManageSurfaceTabs({
  value,
  pluginsCount,
  skillsCount,
  onChange
}: {
  value: Surface
  pluginsCount: number
  skillsCount: number
  onChange: (surface: Surface) => void
}): JSX.Element {
  const t = useT()
  const items: Array<{ value: Surface; label: string }> = [
    { value: 'plugins', label: t('plugins.manage.count.plugins', { count: String(pluginsCount) }) },
    { value: 'skills', label: t('plugins.manage.count.skills', { count: String(skillsCount) }) }
  ]

  return (
    <div style={manageSurfaceTabs}>
      {items.map((item) => (
        <button
          key={item.value}
          type="button"
          onClick={() => onChange(item.value)}
          style={value === item.value ? manageSurfaceTabActive : manageSurfaceTab}
        >
          {item.label}
        </button>
      ))}
    </div>
  )
}

function PluginsManageList({
  pluginManagement,
  loading,
  error,
  diagnostics,
  plugins,
  onOpen,
  onInstall,
  onToggle
}: {
  pluginManagement: boolean
  loading: boolean
  error: string | null
  diagnostics: PluginDiagnosticEntry[]
  plugins: PluginEntry[]
  onOpen: (plugin: PluginEntry) => void
  onInstall: (plugin: PluginEntry) => void
  onToggle: (plugin: PluginEntry, enabled: boolean) => void
}): JSX.Element {
  const t = useT()

  return (
    <main style={manageMain}>
      {!pluginManagement && <p style={emptyText}>{t('plugins.unavailable')}</p>}
      {loading && <p style={emptyText}>{t('plugins.loading')}</p>}
      {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
      <PluginDiagnosticsBanner diagnostics={diagnostics} />
      {plugins.map((plugin) => (
        <PluginManageItem
          key={plugin.id}
          plugin={plugin}
          onOpen={() => onOpen(plugin)}
          onInstall={() => onInstall(plugin)}
          onToggle={(enabled) => onToggle(plugin, enabled)}
        />
      ))}
    </main>
  )
}

function SurfaceTabs({ value, onChange }: { value: Surface; onChange: (value: Surface) => void }): JSX.Element {
  const t = useT()
  return (
    <CatalogTabs
      value={value}
      onChange={onChange}
      items={[
        { value: 'plugins', label: t('plugins.tab.plugins') },
        { value: 'skills', label: t('plugins.tab.skills') }
      ]}
    />
  )
}

function PluginManageItem({
  plugin,
  onOpen,
  onInstall,
  onToggle
}: {
  plugin: PluginEntry
  onOpen: () => void
  onInstall: () => void
  onToggle: (enabled: boolean) => void
}): JSX.Element {
  const t = useT()
  return (
    <div style={manageRow}>
      <button type="button" onClick={onOpen} style={manageItemMain}>
        <PluginIcon plugin={plugin} size={38} />
        <span style={pluginText}>
          <strong style={rowTitle}>{pluginTitle(plugin)}</strong>
          <span style={rowDesc}>{pluginSubtitle(plugin)}</span>
        </span>
      </button>
      <span style={manageSource}>{pluginSourceLabel(plugin)}</span>
      {plugin.installed ? (
        <PillSwitch checked={plugin.enabled} onChange={onToggle} size="sm" aria-label={`${pluginTitle(plugin)} enabled`} />
      ) : (
        <button type="button" onClick={onInstall} style={installMiniButton}>{t('plugins.install')}</button>
      )}
    </div>
  )
}

function PluginDetailView({
  plugin,
  loading,
  saved,
  onBack,
  onSurfaceChange,
  onInstall,
  onRemove,
  onToggle,
  enablingLsp,
  onEnableLsp,
  onTryInChat
}: {
  plugin: PluginEntry
  loading: boolean
  saved: boolean
  onBack: () => void
  onSurfaceChange: (surface: Surface) => void
  onInstall: () => void
  onRemove: () => void
  onToggle: (enabled: boolean) => void
  enablingLsp: boolean
  onEnableLsp: () => void
  onTryInChat: () => void
}): JSX.Element {
  const t = useT()
  const info = plugin.interface
  const shouldOfferLspEnable = plugin.installed
    && plugin.enabled
    && (plugin.lspServers ?? []).some((server) => server.enabled && !server.active && !server.shadowedBy)
  const contents = [
    ...getPluginDesktopExtensionContents(plugin, t).map((extension) => ({
      key: extension.key,
      type: 'desktopExtension' as const,
      kind: extension.kind,
      title: extension.title,
      description: extension.description
    })),
    ...(plugin.apps ?? []).map((app) => ({
      key: `app:${app.appId}`,
      type: 'app' as const,
      kind: t('plugins.content.app'),
      title: app.displayName,
      description: app.description
    })),
    ...plugin.skills.map((skill) => ({
      key: `skill:${skill.name}`,
      type: 'skill' as const,
      kind: t('plugins.content.skill'),
      title: skill.displayName || skill.name,
      description: skill.shortDescription || skill.description
    })),
    ...plugin.functions.map((fn) => ({
      key: `function:${fn.name}`,
      type: 'tool' as const,
      kind: t('plugins.content.tool'),
      title: fn.name,
      description: fn.description
    })),
    ...(plugin.mcpServers ?? []).map((server) => ({
      key: `mcp:${server.runtimeName}`,
      type: 'mcp' as const,
      kind: t('plugins.content.mcpServer'),
      title: server.runtimeName,
      description: describePluginMcpServer(server, t)
    })),
    ...(plugin.lspServers ?? []).map((server) => ({
      key: `lsp:${server.runtimeName}`,
      type: 'lsp' as const,
      kind: t('plugins.content.lspServer'),
      title: server.runtimeName,
      description: describePluginLspServer(server, t)
    }))
  ]
  return (
    <div style={page}>
      <SurfaceTabs value="plugins" onChange={onSurfaceChange} />
      <header style={detailHeader}>
        <div style={detailTopRow}>
          <button type="button" onClick={onBack} style={breadcrumbButton}>
            <ChevronLeft size={14} aria-hidden />
            {t('plugins.pageTitle')}
          </button>
          <div style={{ flex: 1 }} />
          {saved && <span style={savedHint}>{t('settings.savedToast')}</span>}
          <a
            href={resolvePluginExternalUrl(info?.websiteUrl) ?? DOTCRAFT_PLUGIN_FALLBACK_URL}
            style={detailIconButton}
            aria-label={t('plugins.detail.website')}
            title={t('plugins.detail.website')}
            onClick={(event) => handlePluginExternalLinkClick(event, info?.websiteUrl)}
          >
            <Link size={15} aria-hidden />
          </a>
          {plugin.installed && plugin.removable && (
            <button type="button" style={secondaryDetailButton} onClick={onRemove}>
              <Trash2 size={14} aria-hidden />
              {t('plugins.removeFromDotCraft')}
            </button>
          )}
          {plugin.installed ? (
            <button type="button" style={tryButton} disabled={!plugin.enabled} onClick={onTryInChat}>
              <MessageCircle size={14} aria-hidden />
              {t('plugins.tryInChat')}
            </button>
          ) : (
            <button type="button" style={tryButton} onClick={onInstall}>
              {t('plugins.install')}
            </button>
          )}
        </div>
        <PluginIcon plugin={plugin} size={48} />
        <h1 style={detailTitle}>{pluginTitle(plugin)}</h1>
        <p style={detailSubtitle}>{pluginSubtitle(plugin)}</p>
      </header>
      <main style={detailMain}>
        <div style={detailContent}>
          {loading && <p style={emptyText}>{t('plugins.loading')}</p>}
          <div style={promptPreview}>
            <span style={promptBubble}>
              <PluginIcon plugin={plugin} size={18} />
              <strong>{pluginTitle(plugin)}</strong>
              {info?.defaultPrompt || t('plugins.defaultPromptFallback')}
            </span>
          </div>
          <p style={longDescription}>{info?.longDescription || plugin.description}</p>
          <AppBindingPanel plugin={plugin} />
          <section style={detailSection}>
            <h2 style={detailSectionTitle}>{t('plugins.detail.contents')}</h2>
            <div style={contentList}>
              {contents.map((item) => (
                <div key={item.key} style={contentItem}>
                  <span style={contentIcon}>
                    {item.type === 'app' ? (
                      <Link size={16} aria-hidden />
                    ) : item.type === 'desktopExtension' ? (
                      <Settings size={16} aria-hidden />
                    ) : item.type === 'skill' ? (
                      <Box size={16} aria-hidden />
                    ) : item.type === 'mcp' ? (
                      <Server size={16} aria-hidden />
                    ) : item.type === 'lsp' ? (
                      <Code2 size={16} aria-hidden />
                    ) : (
                      <Wrench size={16} aria-hidden />
                    )}
                  </span>
                  <span style={pluginText}>
                    <span style={contentTitleLine}>
                      <strong style={rowTitle}>{item.title}</strong>
                      <span style={contentKind}>{item.kind}</span>
                    </span>
                    <span style={rowDesc}>{item.description}</span>
                  </span>
                </div>
              ))}
            </div>
          </section>
          {shouldOfferLspEnable && (
            <div style={lspEnablePanel} role="status">
              <span style={rowDesc}>{t('plugins.lsp.enablePrompt')}</span>
              <button type="button" style={secondaryDetailButton} disabled={enablingLsp} onClick={onEnableLsp}>
                <Code2 size={14} aria-hidden />
                {enablingLsp ? t('plugins.lsp.enabling') : t('plugins.lsp.enable')}
              </button>
            </div>
          )}
          <section style={detailSection}>
            <h2 style={detailSectionTitle}>{t('plugins.detail.info')}</h2>
            <div style={infoTable}>
              <InfoRow label={t('plugins.detail.category')} value={[displayCategory(info?.category, t), info?.developerName].filter(Boolean).join(', ')} />
              <InfoRow label={t('plugins.detail.capabilities')} value={(info?.capabilities ?? []).join(', ')} />
              <InfoRow label={t('plugins.detail.developer')} value={info?.developerName || 'DotHarness'} />
              <InfoLinkRow label={t('plugins.detail.website')} href={info?.websiteUrl} />
              <InfoLinkRow label={t('plugins.detail.privacy')} href={info?.privacyPolicyUrl} />
              <InfoLinkRow label={t('plugins.detail.terms')} href={info?.termsOfServiceUrl} />
            </div>
          </section>
          {plugin.installed && (
            <div style={detailToggleRow}>
              <span>{plugin.enabled ? t('plugins.enabled') : t('plugins.disabled')}</span>
              <PillSwitch checked={plugin.enabled} onChange={onToggle} aria-label={`${pluginTitle(plugin)} enabled`} />
            </div>
          )}
        </div>
      </main>
    </div>
  )
}

function InfoRow({ label, value }: { label: string; value?: string | null }): JSX.Element {
  return (
    <div style={infoRow}>
      <span style={infoLabel}>{label}</span>
      <span style={infoValue}>{value || '-'}</span>
    </div>
  )
}

function InfoLinkRow({ label, href }: { label: string; href?: string | null }): JSX.Element {
  const resolvedHref = resolvePluginExternalUrl(href) ?? DOTCRAFT_PLUGIN_FALLBACK_URL
  return (
    <div style={infoRow}>
      <span style={infoLabel}>{label}</span>
      <span style={infoValue}>
        <a
          href={resolvedHref}
          style={plainLink}
          aria-label={label}
          title={label}
          onClick={(event) => handlePluginExternalLinkClick(event, href)}
        >
          <ExternalLink size={14} aria-hidden />
        </a>
      </span>
    </div>
  )
}

function handlePluginExternalLinkClick(event: MouseEvent<HTMLAnchorElement>, href?: string | null): void {
  event.preventDefault()
  const resolvedHref = resolvePluginExternalUrl(href) ?? DOTCRAFT_PLUGIN_FALLBACK_URL
  void window.api.shell.openExternal(resolvedHref).catch(() => undefined)
}

function resolvePluginExternalUrl(href?: string | null): string | null {
  const value = href?.trim()
  if (!value) return null
  try {
    const parsed = new URL(value)
    if (parsed.protocol === 'http:' || parsed.protocol === 'https:' || parsed.protocol === 'mailto:' || parsed.protocol === 'tel:') {
      return parsed.href
    }
  } catch {
    return null
  }
  return null
}

function filterPlugins(
  plugins: PluginEntry[],
  query: string,
  publisherFilter: PublisherFilter,
  categoryFilter: CategoryFilter
): PluginEntry[] {
  const q = query.trim().toLowerCase()
  return plugins.filter((plugin) => {
    if (publisherFilter === 'dotcraft' && !isDotHarnessPlugin(plugin)) return false
    if (categoryFilter === 'featured' && !isFeaturedPlugin(plugin)) return false
    if (categoryFilter !== 'all' && categoryFilter !== 'featured' && pluginCategoryKey(plugin) !== categoryFilter) return false
    if (!q) return true
    return (
      plugin.id.toLowerCase().includes(q) ||
      pluginTitle(plugin).toLowerCase().includes(q) ||
      pluginSubtitle(plugin).toLowerCase().includes(q)
    )
  })
}

function buildCategoryOptions(plugins: PluginEntry[], t: ReturnType<typeof useT>): Array<{ value: CategoryFilter; label: string }> {
  const categories = new Set<string>(FIXED_PLUGIN_CATEGORIES)
  for (const plugin of plugins) {
    const key = pluginCategoryKey(plugin)
    if (key) categories.add(key)
  }

  const orderedCategories = [
    ...FIXED_PLUGIN_CATEGORIES,
    ...[...categories]
      .filter((category) => !FIXED_PLUGIN_CATEGORIES.includes(category))
      .sort((a, b) => categoryLabel(a, t).localeCompare(categoryLabel(b, t)))
  ]

  return [
    { value: 'all', label: t('plugins.filter.category.all') },
    { value: 'featured', label: t('plugins.filter.category.featured') },
    ...orderedCategories.map((category) => ({ value: category, label: categoryLabel(category, t) }))
  ]
}

function buildSections(
  plugins: PluginEntry[],
  categoryFilter: CategoryFilter,
  t: ReturnType<typeof useT>
): Array<{ key: string; title: string; plugins: PluginEntry[] }> {
  if (categoryFilter === 'featured') {
    return plugins.length > 0 ? [{ key: 'featured', title: t('plugins.section.featured'), plugins }] : []
  }

  if (categoryFilter !== 'all') {
    return plugins.length > 0 ? [{ key: categoryFilter, title: categoryLabel(categoryFilter, t), plugins }] : []
  }

  const local = plugins.filter(isLocalInstalledPlugin)
  const seen = new Set(local.map((plugin) => plugin.id))
  const sections: Array<{ key: string; title: string; plugins: PluginEntry[] }> = []
  if (local.length > 0) {
    sections.push({ key: 'local', title: t('plugins.section.local'), plugins: local })
  }

  const featured = plugins.filter((plugin) => isFeaturedPlugin(plugin) && !seen.has(plugin.id))
  if (featured.length > 0) {
    sections.push({ key: 'featured', title: t('plugins.section.featured'), plugins: featured })
    for (const plugin of featured) seen.add(plugin.id)
  }

  const byCategory = new Map<string, PluginEntry[]>()
  for (const plugin of plugins) {
    if (seen.has(plugin.id)) continue
    const key = pluginCategoryKey(plugin) || 'uncategorized'
    const group = byCategory.get(key) ?? []
    group.push(plugin)
    byCategory.set(key, group)
  }

  const orderedKeys = [
    ...FIXED_PLUGIN_CATEGORIES,
    ...[...byCategory.keys()]
      .filter((key) => !FIXED_PLUGIN_CATEGORIES.includes(key))
      .sort((a, b) => categoryLabel(a, t).localeCompare(categoryLabel(b, t)))
  ]
  for (const key of orderedKeys) {
    const group = byCategory.get(key)
    if (group == null || group.length === 0) continue
    sections.push({ key, title: categoryLabel(key, t), plugins: group })
  }

  return sections
}

function isFeaturedPlugin(plugin: PluginEntry): boolean {
  return plugin.id === 'browser' || plugin.id === 'chrome'
}

function isLocalInstalledPlugin(plugin: PluginEntry): boolean {
  return plugin.installed && plugin.source.toLowerCase() !== 'builtin'
}

function isDotHarnessPlugin(plugin: PluginEntry): boolean {
  const developer = plugin.interface?.developerName?.trim().toLowerCase()
  return plugin.id === 'browser' || developer === 'dotharness' || plugin.source.toLowerCase().includes('builtin')
}

function pluginCategoryKey(plugin: PluginEntry): string {
  return normalizeCategory(plugin.interface?.category)
}

function normalizeCategory(category?: string | null): string {
  const normalized = (category || '').trim().toLowerCase()
  if (!normalized) return 'uncategorized'
  return normalized.replace(/\s+/g, '-')
}

function categoryLabel(category: string, t: ReturnType<typeof useT>): string {
  if (category === 'coding') return t('plugins.filter.category.coding')
  if (category === 'design') return t('plugins.filter.category.design')
  if (category === 'engineering') return t('plugins.filter.category.engineering')
  if (category === 'lifestyle') return t('plugins.filter.category.lifestyle')
  if (category === 'productivity') return t('plugins.filter.category.productivity')
  if (category === 'research') return t('plugins.filter.category.research')
  if (category === 'uncategorized') return t('plugins.filter.category.uncategorized')
  return category
    .split('-')
    .filter(Boolean)
    .map((part) => part.slice(0, 1).toUpperCase() + part.slice(1))
    .join(' ')
}

function displayCategory(category: string | null | undefined, t: ReturnType<typeof useT>): string {
  return categoryLabel(normalizeCategory(category), t)
}

function tryPluginInChat(plugin: PluginEntry): void {
  const prompt = plugin.interface?.defaultPrompt || ''
  const skillName = plugin.skills.find((skill) => skill.enabled)?.name ?? plugin.skills[0]?.name ?? null
  const text = skillName ? `$${skillName}${prompt ? ` ${prompt}` : ''}` : (prompt || pluginTitle(plugin))
  const ui = useUIStore.getState()
  const existing = ui.welcomeDraft
  ui.setWelcomeDraft({
    text,
    segments: skillName ? [{ type: 'skill', skillName }] : [],
    selectionStart: text.length,
    selectionEnd: text.length,
    images: [],
    files: [],
    mode: existing?.mode ?? 'agent',
    model: existing?.model || 'Default',
    approvalPolicy: existing?.approvalPolicy ?? 'default'
  })
  ui.goToNewChat()
}

function describePluginMcpServer(
  server: NonNullable<PluginEntry['mcpServers']>[number],
  t: (key: MessageKey, vars?: Record<string, string>) => string
): string {
  const transport =
    server.transport === 'stdio' ? t('settings.mcp.transport.stdio') : t('settings.mcp.transport.http')
  let state = server.active ? t('plugins.content.mcp.active') : t('plugins.content.mcp.inactive')
  if (!server.enabled) state = t('plugins.content.mcp.disabled')
  if (server.shadowedBy === 'workspace') state = t('plugins.content.mcp.shadowedWorkspace')
  if (server.shadowedBy === 'plugin') state = t('plugins.content.mcp.shadowedPlugin')
  return `${transport} · ${state}`
}

function describePluginLspServer(
  server: NonNullable<PluginEntry['lspServers']>[number],
  t: (key: MessageKey, vars?: Record<string, string>) => string
): string {
  let state = server.active ? t('plugins.content.lsp.active') : t('plugins.content.lsp.inactive')
  if (!server.enabled) state = t('plugins.content.lsp.disabled')
  if (server.shadowedBy === 'workspace') state = t('plugins.content.lsp.shadowedWorkspace')
  if (server.shadowedBy === 'plugin') state = t('plugins.content.lsp.shadowedPlugin')
  const extensions = server.extensions.length > 0 ? ` · ${server.extensions.join(', ')}` : ''
  return `${server.transport.toUpperCase()} · ${state}${extensions}`
}

function filterVisibleDiagnostics(diagnostics: PluginDiagnosticEntry[]): PluginDiagnosticEntry[] {
  return diagnostics.filter((diagnostic) => {
    const severity = diagnostic.severity.toLowerCase()
    return severity === 'warning' || severity === 'error'
  })
}

function PluginDiagnosticsBanner({ diagnostics }: { diagnostics: PluginDiagnosticEntry[] }): JSX.Element | null {
  const t = useT()
  if (diagnostics.length === 0) return null
  return (
    <div style={diagnosticsPanel} role="status">
      <strong style={diagnosticsTitle}>{t('plugins.diagnostics.title')}</strong>
      <div style={diagnosticsList}>
        {diagnostics.slice(0, 5).map((diagnostic, index) => (
          <div key={`${diagnostic.code}-${diagnostic.path ?? index}`} style={diagnosticItem}>
            <span style={diagnosticCode}>{diagnostic.code}</span>
            <span style={diagnosticMessage}>{diagnostic.message}</span>
            {diagnostic.path && <span style={diagnosticPath}>{diagnostic.path}</span>}
          </div>
        ))}
        {diagnostics.length > 5 && (
          <div style={diagnosticMore}>{t('plugins.diagnostics.more', { count: String(diagnostics.length - 5) })}</div>
        )}
      </div>
    </div>
  )
}

const page: CSSProperties = catalogStyles.page
const browseHeader: CSSProperties = catalogStyles.browseHeader
const topActions: CSSProperties = catalogStyles.topActions
const heroTitle: CSSProperties = catalogStyles.heroTitle
const searchRow: CSSProperties = catalogStyles.searchRow
const browseMain: CSSProperties = catalogStyles.browseMain
const sectionTitle: CSSProperties = catalogStyles.sectionTitle
const compactGrid: CSSProperties = catalogStyles.compactGrid
const compactItem: CSSProperties = catalogStyles.compactItem
const rowTitle: CSSProperties = catalogStyles.rowTitle
const rowDesc: CSSProperties = catalogStyles.rowDesc
const manageButton: CSSProperties = catalogStyles.manageButton
const manageButtonLabel: CSSProperties = {
  lineHeight: 1,
  transform: 'translateY(-1px)'
}
const iconButton: CSSProperties = catalogStyles.iconButton
const manageHeader: CSSProperties = catalogStyles.manageHeader
const breadcrumb: CSSProperties = catalogStyles.breadcrumb
const breadcrumbButton: CSSProperties = catalogStyles.breadcrumbButton
const breadcrumbSep: CSSProperties = catalogStyles.breadcrumbSep
const breadcrumbCurrent: CSSProperties = catalogStyles.breadcrumbCurrent
const manageToolbar: CSSProperties = catalogStyles.manageToolbar
const manageSurfaceTabs: CSSProperties = { display: 'flex', alignItems: 'center', gap: '8px' }
const manageSurfaceTab: CSSProperties = {
  ...catalogStyles.chip,
  border: 'none',
  cursor: 'pointer'
}
const manageSurfaceTabActive: CSSProperties = {
  ...catalogStyles.chipActive,
  border: 'none',
  cursor: 'pointer'
}
const savedHint: CSSProperties = catalogStyles.savedHint
const manageMain: CSSProperties = catalogStyles.manageMain
const manageRow: CSSProperties = catalogStyles.manageRow
const emptyText: CSSProperties = catalogStyles.emptyText
const manageItemMain: CSSProperties = { ...compactItem, flex: 1, padding: 0, height: 'auto' }
const manageSource: CSSProperties = { width: '86px', color: 'var(--text-secondary)', fontSize: '13px', textAlign: 'left' }
const installMiniButton: CSSProperties = { border: 'none', borderRadius: 999, background: 'var(--bg-tertiary)', color: 'var(--text-primary)', padding: '6px 11px', fontSize: 12, cursor: 'pointer' }
const pluginText: CSSProperties = { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }
const detailMain: CSSProperties = { flex: 1, minHeight: 0, overflow: 'auto', width: '100%' }
const detailContent: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '0 auto', padding: '0 0 48px' }
const detailHeader: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '22px auto 28px' }
const detailTopRow: CSSProperties = { display: 'flex', alignItems: 'center', gap: 10, marginBottom: 28 }
const detailTitle: CSSProperties = { margin: '22px 0 6px', fontSize: 22, fontWeight: 600 }
const detailSubtitle: CSSProperties = { margin: 0, color: 'var(--text-secondary)', fontSize: 15 }
const detailIconButton: CSSProperties = { width: 32, height: 32, borderRadius: 8, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)', textDecoration: 'none' }
const secondaryDetailButton: CSSProperties = { border: 'none', borderRadius: 8, background: 'var(--bg-tertiary)', color: 'var(--text-primary)', padding: '8px 12px', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }
const tryButton: CSSProperties = { border: 'none', borderRadius: 8, background: '#050505', color: '#fff', padding: '8px 12px', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 6 }
const promptPreview: CSSProperties = { height: 132, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'linear-gradient(120deg, #b6cdf5, #d9cef7 58%, #f3f0fb)' }
const promptBubble: CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 7, maxWidth: '80%', border: '1px solid rgba(0,0,0,0.12)', borderRadius: 13, background: 'rgba(255,255,255,0.82)', color: '#111', padding: '8px 12px', fontSize: 13, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }
const longDescription: CSSProperties = { margin: '54px 8px 40px', lineHeight: 1.55, fontSize: 14, color: 'var(--text-primary)' }
const detailSection: CSSProperties = { marginTop: 28 }
const detailSectionTitle: CSSProperties = { margin: '0 0 12px', fontSize: 15, fontWeight: 600 }
const contentList: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 8, padding: 10 }
const contentItem: CSSProperties = { display: 'flex', alignItems: 'center', gap: 12, padding: '8px 0' }
const contentIcon: CSSProperties = { width: 38, height: 38, borderRadius: 19, border: '1px solid var(--border-default)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)' }
const contentTitleLine: CSSProperties = { display: 'inline-flex', alignItems: 'baseline', gap: 5, minWidth: 0 }
const contentKind: CSSProperties = { fontWeight: 400, color: 'var(--text-secondary)' }
const lspEnablePanel: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', marginTop: 16 }
const infoTable: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 8, overflow: 'hidden' }
const infoRow: CSSProperties = { display: 'grid', gridTemplateColumns: '180px 1fr', minHeight: 54, borderBottom: '1px solid var(--border-default)' }
const infoLabel: CSSProperties = { color: 'var(--text-secondary)', fontSize: 13, padding: '18px 16px' }
const infoValue: CSSProperties = { fontSize: 13, padding: '18px 16px' }
const plainLink: CSSProperties = { color: 'var(--accent)', display: 'inline-flex' }
const detailToggleRow: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 24, padding: '12px 4px', fontSize: 13 }
const iconToolbarButton: CSSProperties = { width: 32, height: 32, border: 'none', borderRadius: 8, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', background: 'var(--bg-tertiary)', color: 'var(--text-secondary)', cursor: 'pointer' }
const diagnosticsPanel: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', margin: '0 0 24px' }
const diagnosticsTitle: CSSProperties = { display: 'block', fontSize: 13, marginBottom: 8, color: 'var(--text-primary)' }
const diagnosticsList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 7 }
const diagnosticItem: CSSProperties = { display: 'grid', gridTemplateColumns: 'minmax(120px, max-content) minmax(0, 1fr)', columnGap: 10, rowGap: 3, alignItems: 'baseline', fontSize: 12 }
const diagnosticCode: CSSProperties = { color: 'var(--warning, #A16207)', fontFamily: 'var(--font-mono)' }
const diagnosticMessage: CSSProperties = { color: 'var(--text-secondary)', minWidth: 0 }
const diagnosticPath: CSSProperties = { gridColumn: '1 / -1', color: 'var(--text-tertiary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const diagnosticMore: CSSProperties = { color: 'var(--text-tertiary)', fontSize: 12 }
