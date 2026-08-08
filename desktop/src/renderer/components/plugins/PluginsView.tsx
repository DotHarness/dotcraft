import { useEffect, useMemo, useState } from 'react'
import type { CSSProperties, MouseEvent } from 'react'
import { Anchor, AtSign, Box, Code2, Ellipsis, ExternalLink, FolderInput, Link, MessageCircle, Plus, Server, Settings, Store, Trash2, Wrench } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { usePluginStore, type MarketplaceEntry, type PluginDiagnosticEntry, type PluginEntry } from '../../stores/pluginStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore, type PluginCatalogSurface } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { stringifyComposerDraftSegments } from '../conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../../types/composerDraft'
import { PillSwitch } from '../ui/PillSwitch'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SkillsManageList, SkillsView, filterLocalSkills, stripYamlFrontmatter } from '../skills/SkillsView'
import { SkillDetailDialog } from '../skills/SkillDetailDialog'
import { stageSkillTryInChat } from '../skills/skillDraft'
import {
  CatalogHoverButton,
  CatalogFilterMenu,
  CatalogBreadcrumb,
  CatalogSearchBox,
  CatalogTabs,
  CatalogToolbarIconButton,
  CatalogTopBar,
  styles as catalogStyles
} from '../catalog/CatalogSurface'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { RefreshIcon } from '../ui/AppIcons'
import { PluginCatalogItem, PluginIcon, pluginSourceLabel, pluginSubtitle, pluginTitle } from './PluginCatalogItem'
import { PluginInstallDialog } from './PluginInstallDialog'
import { AppBindingPanel } from './AppBindingPanel'
import { getPluginContentSummaries, type PluginContentType } from '../../utils/pluginContentSummaries'
import { SkeletonCatalogGrid, SkeletonList } from '../ui/Skeleton'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { SplitButton, type SplitButtonItem } from '../ui/SplitButton'
import { PluginInstallButton } from './PluginInstallButton'
import { AddMarketplaceDialog } from './AddMarketplaceDialog'

type Surface = PluginCatalogSurface
type PluginMode = 'browse' | 'manage'
/**
 * The browse listing's grouping selector. `marketplaces` is not a publisher but a
 * delivery route: it narrows to marketplace-sourced entries and groups them by
 * their source, which is where refresh and remove live. See the Plugin creation
 * and marketplace sources section in specs/clients/desktop-client.md.
 */
type PublisherFilter = 'dotcraft' | 'all' | 'marketplaces'
type CategoryFilter = string
const DOTCRAFT_PLUGIN_FALLBACK_URL = 'https://github.com/DotHarness/dotcraft'
const PLUGIN_CREATOR_SKILL = 'plugin-creator'
const FIXED_PLUGIN_CATEGORIES = [
  'coding',
  'design',
  'engineering',
  'security',
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
  const pluginMarketplaces = capabilities?.pluginMarketplaces === true
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const {
    plugins,
    marketplaces,
    diagnostics,
    loading,
    error,
    fetchPlugins,
    selectedPlugin,
    detailLoading,
    selectPlugin,
    clearSelection,
    installPlugin,
    installLocalPlugin,
    removePlugin,
    togglePluginEnabled,
    removeMarketplace,
    refreshMarketplace
  } = usePluginStore()
  const {
    skills,
    loading: skillsLoading,
    error: skillsError,
    selectedSkillName,
    skillContent,
    contentLoading: skillContentLoading,
    selectSkill,
    clearSelection: clearSkillSelection,
    fetchSkills,
    toggleSkillEnabled
  } = useSkillsStore()
  const surface = useUIStore((s) => s.pluginCatalogSurface)
  const setSurface = useUIStore((s) => s.setPluginCatalogSurface)
  const [mode, setMode] = useState<PluginMode>('browse')
  const [browseQuery, setBrowseQuery] = useState('')
  const [managePluginQuery, setManagePluginQuery] = useState('')
  const [skillManageQuery, setSkillManageQuery] = useState('')
  const [publisherFilter, setPublisherFilter] = useState<PublisherFilter>('all')
  const [categoryFilter, setCategoryFilter] = useState<CategoryFilter>('all')
  const [installTarget, setInstallTarget] = useState<PluginEntry | null>(null)
  const [installingId, setInstallingId] = useState<string | null>(null)
  const [enablingLspId, setEnablingLspId] = useState<string | null>(null)
  const [addMarketplaceOpen, setAddMarketplaceOpen] = useState(false)

  useEffect(() => {
    if (pluginManagement) void fetchPlugins()
  }, [fetchPlugins, pluginManagement])

  useEffect(() => {
    if (mode === 'manage') void fetchSkills()
  }, [fetchSkills, mode])

  // Removing the last marketplace leaves the marketplace mode with nothing to show,
  // so the listing returns to the unfiltered view rather than looking empty.
  useEffect(() => {
    if (publisherFilter === 'marketplaces' && marketplaces.length === 0) setPublisherFilter('all')
  }, [marketplaces.length, publisherFilter])

  const browsePlugins = useMemo(
    () => filterPlugins(plugins, browseQuery, publisherFilter, categoryFilter),
    [plugins, browseQuery, publisherFilter, categoryFilter]
  )
  const managePlugins = useMemo(
    () => filterPlugins(plugins, managePluginQuery, 'all', 'all'),
    [managePluginQuery, plugins]
  )
  const manageSkills = useMemo(
    () => filterLocalSkills(skills, skillManageQuery, 'all'),
    [skills, skillManageQuery]
  )
  const visibleDiagnostics = useMemo(() => filterVisibleDiagnostics(diagnostics), [diagnostics])
  const selectedSkill = selectedSkillName
    ? skills.find((skill) => skill.name === selectedSkillName) ?? null
    : null
  const selectedSkillBody = skillContent != null ? stripYamlFrontmatter(skillContent) : ''
  const categoryOptions = useMemo(() => buildCategoryOptions(plugins, t), [plugins, t])
  const sections = useMemo(
    () => buildSections(browsePlugins, categoryFilter, publisherFilter, t, marketplaces),
    [browsePlugins, categoryFilter, marketplaces, publisherFilter, t]
  )
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

  // Stage a plugin authoring conversation. The skill mention is only staged when the skill
  // actually resolves, so the composer never shows a chip that cannot be resolved. The skill
  // list is only fetched when it has not been loaded yet, keeping browse free of an eager call.
  async function handleCreatePlugin(): Promise<void> {
    let available = skills
    if (available.length === 0) {
      try {
        await fetchSkills()
        available = useSkillsStore.getState().skills
      } catch {
        available = []
      }
    }

    const hasCreatorSkill = available.some((skill) => skill.name === PLUGIN_CREATOR_SKILL)
    const prompt = t('plugins.create.prompt')
    // The segment list is the composer's content model and the plain text is only its
    // serialization, so the prompt has to be a segment of its own — text passed beside a
    // lone skill segment is dropped. Deriving the text from the segments keeps the two
    // from drifting apart.
    const segments: ComposerDraftSegment[] = hasCreatorSkill
      ? [{ type: 'skill', skillName: PLUGIN_CREATOR_SKILL }, { type: 'text', value: ` ${prompt}` }]
      : [{ type: 'text', value: prompt }]
    const text = stringifyComposerDraftSegments(segments)
    const ui = useUIStore.getState()
    const existing = ui.welcomeDraft
    ui.setWelcomeDraft({
      text,
      segments,
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

  // Browse never loads the skill list, so it is fetched on demand the first time a
  // plugin's contents list is used to open one.
  async function handleOpenSkill(name: string): Promise<void> {
    if (useSkillsStore.getState().skills.length === 0) {
      try {
        await fetchSkills()
      } catch {
        addToast(t('skills.updateFailed'), 'error')
        return
      }
    }
    await selectSkill(name)
  }

  async function handleRefreshMarketplace(marketplace: MarketplaceEntry): Promise<void> {
    try {
      const errors = await refreshMarketplace(marketplace.name)
      const failure = errors.find((entry) => entry.name === marketplace.name)
      if (failure) {
        addToast(failure.message, 'error')
        return
      }
      addToast(t('plugins.marketplace.refreshSuccess'), 'success')
    } catch (err) {
      addToast(err instanceof Error ? err.message : t('plugins.marketplace.refreshFailed'), 'error')
    }
  }

  async function handleRemoveMarketplace(marketplace: MarketplaceEntry): Promise<void> {
    const name = marketplaceTitle(marketplace)
    const ok = await confirm({
      title: t('plugins.marketplace.removeConfirm.title', { name }),
      message: t('plugins.marketplace.removeConfirm.message', { name }),
      confirmLabel: t('plugins.marketplace.remove'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!ok) return

    try {
      await removeMarketplace(marketplace.name)
      addToast(t('plugins.marketplace.removeSuccess'), 'success')
    } catch (err) {
      addToast(err instanceof Error ? err.message : t('plugins.marketplace.removeFailed'), 'error')
    }
  }

  // Install a plugin from a local folder the user points at. The backend validates the
  // folder (a valid `.craft-plugin/plugin.json`) before copying anything; on failure it
  // returns the reason, which we surface in the toast. This makes it possible to add
  // plugins to workspaces that are not browsed as projects, such as the default Chat one.
  async function handleInstallFromDisk(): Promise<void> {
    let path: string | null
    try {
      path = await window.api.workspace.pickFolder({ title: t('plugins.installLocal.pickTitle') })
    } catch {
      return
    }
    if (!path) return
    try {
      const installed = await installLocalPlugin(path)
      await fetchSkills()
      await fetchPlugins()
      if (installed) await selectPlugin(installed.id)
      addToast(t('plugins.installLocal.success'), 'success')
    } catch (err) {
      const detail = err instanceof Error ? extractInstallErrorDetail(err.message) : ''
      addToast(detail || t('plugins.installLocal.failed'), 'error')
    }
  }

  // Every way of getting a plugin into the workspace lives in one menu. The first entry
  // is also the principal action, so a single available entry degrades to a plain button.
  const createActions: SplitButtonItem[] = [
    {
      key: 'create-plugin',
      label: t('plugins.create.plugin'),
      icon: <AtSign size={14} aria-hidden />,
      onClick: () => void handleCreatePlugin()
    },
    ...(pluginMarketplaces
      ? [{
          key: 'add-marketplace',
          label: t('plugins.marketplace.add.menu'),
          icon: <Store size={14} aria-hidden />,
          onClick: () => setAddMarketplaceOpen(true)
        }]
      : []),
    ...(!remoteWorkspaceActive
      ? [{
          key: 'install-local',
          label: t('plugins.installLocal.menu'),
          icon: <FolderInput size={14} aria-hidden />,
          onClick: () => void handleInstallFromDisk()
        }]
      : [])
  ]

  if (surface === 'skills' && mode !== 'manage') {
    return (
      <SkillsView
        topNavigation={<SurfaceTabs value={surface} onChange={setSurface} />}
        onManage={() => setMode('manage')}
      />
    )
  }

  if (selectedPlugin) {
    return (
      <>
        <PluginDetailView
          plugin={selectedPlugin}
          loading={detailLoading}
          onBack={() => clearSelection()}
          onManage={() => {
            setManagePluginQuery(pluginTitle(selectedPlugin))
            setMode('manage')
            clearSelection()
          }}
          onInstall={() => setInstallTarget(selectedPlugin)}
          onRemove={async () => {
            const pluginName = pluginTitle(selectedPlugin)
            const ok = await confirm({
              title: t('plugins.uninstallConfirm.title', { name: pluginName }),
              message: t('plugins.uninstallConfirm.message', {
                name: pluginName,
                path: selectedPlugin.rootPath || `.craft/plugins/${selectedPlugin.id}`
              }),
              confirmLabel: t('plugins.uninstall'),
              cancelLabel: t('common.cancel'),
              danger: true
            })
            if (!ok) return

            try {
              await removePlugin(selectedPlugin.id)
              await fetchPlugins()
              await fetchSkills()
              addToast(t('plugins.uninstallSuccess'), 'success')
            } catch {
              addToast(t('plugins.uninstallFailed'), 'error')
            }
          }}
          enablingLsp={enablingLspId === selectedPlugin.id}
          onEnableLsp={async () => {
            try {
              setEnablingLspId(selectedPlugin.id)
              await window.api.appServer.sendRequest('workspace/config/update', { toolsLspEnabled: true })
              await fetchPlugins()
              await selectPlugin(selectedPlugin.id)
              addToast(t('plugins.lsp.enableSuccess'), 'success')
            } catch {
              addToast(t('plugins.lsp.enableFailed'), 'error')
            } finally {
              setEnablingLspId(null)
            }
          }}
          onTryInChat={() => tryPluginInChat(selectedPlugin)}
          onOpenSkill={(name) => void handleOpenSkill(name)}
        />
        {selectedSkill && (
          <SkillDetailDialog
            skill={selectedSkill}
            markdownBody={selectedSkillBody}
            loading={skillContentLoading}
            onClose={() => clearSkillSelection()}
            onTryInChat={() => {
              clearSkillSelection()
              stageSkillTryInChat(selectedSkill)
            }}
          />
        )}
        {installDialog}
      </>
    )
  }

  if (mode === 'manage') {
    return (
      <>
        <div style={page}>
          <CatalogTopBar
            navigation={(
              <CatalogBreadcrumb
                parentLabel={surface === 'plugins' ? t('plugins.pageTitle') : t('skills.pageTitle')}
                currentLabel={t('plugins.manage')}
                onBack={() => setMode('browse')}
              />
            )}
          />
          <header style={manageHeader}>
            {surface === 'plugins' ? (
              <PluginsManageToolbar
                surface={surface}
                plugins={plugins}
                skillsCount={skills.length}
                query={managePluginQuery}
                onSurfaceChange={setSurface}
                onQueryChange={setManagePluginQuery}
              />
            ) : (
              <ManageSkillsToolbar
                surface={surface}
                pluginsCount={plugins.length}
                skillsCount={skills.length}
                query={skillManageQuery}
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
      <CatalogTopBar
        navigation={<SurfaceTabs value={surface} onChange={setSurface} />}
        actions={(
          <>
            <CatalogToolbarIconButton
              label={t('plugins.refresh')}
              onClick={() => void fetchPlugins()}
              icon={<RefreshIcon size={15} />}
            />
            <CatalogToolbarIconButton
              label={t('plugins.manage')}
              onClick={() => { setManagePluginQuery(''); setMode('manage') }}
              icon={<Settings size={15} aria-hidden />}
            />
            {createActions.length > 1 ? (
              <SplitButton
                label={t('plugins.create.button')}
                menuLabel={t('plugins.create.menuLabel')}
                onClick={createActions[0].onClick}
                items={createActions}
              />
            ) : (
              <Button variant="primary" size="toolbar" onClick={createActions[0].onClick}>
                {t('plugins.create.button')}
              </Button>
            )}
          </>
        )}
      />
      <header style={browseHeader}>
        <h1 style={heroTitle}>{t('plugins.heroTitle')}</h1>
        <div style={searchRow}>
          <CatalogSearchBox value={browseQuery} placeholder={t('plugins.searchPlaceholder')} onChange={setBrowseQuery} />
          <CatalogFilterMenu
            value={publisherFilter}
            ariaLabel={t('plugins.filter.publisher.label')}
            onChange={setPublisherFilter}
            options={[
              { value: 'dotcraft', label: t('plugins.filter.publisher.dotcraft') },
              { value: 'all', label: t('plugins.filter.publisher.all') },
              ...(pluginMarketplaces && marketplaces.length > 0
                ? [{ value: 'marketplaces' as const, label: t('plugins.filter.publisher.marketplaces') }]
                : [])
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
        {loading && <SkeletonCatalogGrid ariaLabel={t('plugins.loading')} />}
        {error && <p style={{ ...emptyText, color: 'var(--error)' }} role="alert">{error}</p>}
        <PluginDiagnosticsBanner diagnostics={visibleDiagnostics} />
        {sections.map((section) => (
          <section key={section.key} style={{ marginBottom: '34px' }}>
            {section.marketplace ? (
              <MarketplaceSectionHeader
                marketplace={section.marketplace}
                onRefresh={() => void handleRefreshMarketplace(section.marketplace!)}
                onRemove={() => void handleRemoveMarketplace(section.marketplace!)}
              />
            ) : (
              <h2 style={sectionTitle}>{section.title}</h2>
            )}
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
      {addMarketplaceOpen && (
        <AddMarketplaceDialog
          allowLocalFolder={!remoteWorkspaceActive}
          onClose={() => setAddMarketplaceOpen(false)}
          onAdded={(marketplace, alreadyAdded) => {
            setAddMarketplaceOpen(false)
            addToast(
              alreadyAdded
                ? t('plugins.marketplace.add.alreadyAdded', { name: marketplaceTitle(marketplace) })
                : t('plugins.marketplace.add.success', { name: marketplaceTitle(marketplace) }),
              'success'
            )
          }}
        />
      )}
      {installDialog}
    </div>
  )
}

function MarketplaceSectionHeader({
  marketplace,
  onRefresh,
  onRemove
}: {
  marketplace: MarketplaceEntry
  onRefresh: () => void
  onRemove: () => void
}): JSX.Element {
  const t = useT()
  const [position, setPosition] = useState<ContextMenuPosition | null>(null)
  const [hovered, setHovered] = useState(false)
  const [actionFocused, setActionFocused] = useState(false)

  // Revealed by opacity rather than by mounting, so the row does not shift as the
  // pointer crosses it, and on keyboard focus too, or the actions are pointer-only.
  // See Selection Rows in specs/architecture/DESIGN.md.
  const actionsVisible = hovered || actionFocused || position != null

  return (
    <div
      style={marketplaceHeaderRow}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      {/* The source identifies the group without earning a place in the layout. */}
      <ActionTooltip label={marketplace.source} placement="bottom" multiline>
        <h2 style={marketplaceHeaderTitle}>{marketplaceTitle(marketplace)}</h2>
      </ActionTooltip>
      <div style={{ flex: 1 }} />
      {marketplace.removable && (
        <span style={revealedActionStyle(actionsVisible)}>
          <IconButton
            label={t('plugins.marketplace.actions')}
            tooltipLabel={t('plugins.marketplace.actions')}
            tooltipPlacement="bottom"
            aria-haspopup="menu"
            aria-expanded={position != null}
            size={28}
            onFocus={() => setActionFocused(true)}
            onBlur={() => setActionFocused(false)}
            onClick={(event) => {
              const rect = event.currentTarget.getBoundingClientRect()
              setPosition({ x: rect.right - 200, y: rect.bottom + 4 })
            }}
            icon={<Ellipsis size={15} aria-hidden />}
          />
        </span>
      )}
      {position && (
        <ContextMenu
          position={position}
          onClose={() => setPosition(null)}
          items={[
            {
              label: t('plugins.marketplace.refresh'),
              icon: <RefreshIcon size={14} />,
              onClick: onRefresh
            },
            {
              label: t('plugins.marketplace.remove'),
              icon: <Trash2 size={14} />,
              danger: true,
              onClick: onRemove
            }
          ]}
        />
      )}
    </div>
  )
}

export function marketplaceTitle(marketplace: MarketplaceEntry): string {
  return marketplace.displayName?.trim() || marketplace.name
}

function PluginsManageToolbar({
  surface,
  plugins,
  skillsCount,
  query,
  onSurfaceChange,
  onQueryChange
}: {
  surface: Surface
  plugins: PluginEntry[]
  skillsCount: number
  query: string
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
  onSurfaceChange,
  onQueryChange
}: {
  surface: Surface
  pluginsCount: number
  skillsCount: number
  query: string
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
        <ManageSurfaceTab
          key={item.value}
          label={item.label}
          active={value === item.value}
          onClick={() => onChange(item.value)}
        />
      ))}
    </div>
  )
}

function ManageSurfaceTab({
  label,
  active,
  onClick
}: {
  label: string
  active: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <CatalogHoverButton
      type="button"
      onClick={onClick}
      baseStyle={active ? manageSurfaceTabActive : manageSurfaceTab}
      hoverStyle={{ borderColor: 'transparent' }}
    >
      {label}
    </CatalogHoverButton>
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
      {loading && (
        <SkeletonList
          count={5}
          ariaLabel={t('plugins.loading')}
          rowProps={{ media: 38, mediaRadius: 8, lines: ['46%', '30%'] }}
          rowStyle={{ maxWidth: '730px', margin: '0 auto', minHeight: '74px' }}
        />
      )}
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
      inTopBar
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
  const [active, setActive] = useState(false)
  return (
    <div
      style={interactiveManageRow(active)}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
    >
      <button type="button" onClick={onOpen} style={manageItemMain}>
        <PluginIcon plugin={plugin} size={38} />
        <span style={pluginText}>
          <strong style={rowTitle}>{pluginTitle(plugin)}</strong>
          <span style={rowDesc}>{pluginSubtitle(plugin)}</span>
        </span>
      </button>
      <span style={manageSource} title={plugin.marketplaceName ?? undefined}>
        {plugin.marketplaceName || pluginSourceLabel(plugin)}
      </span>
      <span style={manageActionSlot}>
        {plugin.installed ? (
          <PillSwitch checked={plugin.enabled} onChange={onToggle} size="sm" aria-label={`${pluginTitle(plugin)} enabled`} />
        ) : (
          <PluginInstallButton onClick={onInstall}>{t('plugins.install')}</PluginInstallButton>
        )}
      </span>
    </div>
  )
}

function PluginDetailView({
  plugin,
  loading,
  onBack,
  onManage,
  onInstall,
  onRemove,
  enablingLsp,
  onEnableLsp,
  onTryInChat,
  onOpenSkill
}: {
  plugin: PluginEntry
  loading: boolean
  onBack: () => void
  onManage: () => void
  onInstall: () => void
  onRemove: () => void
  enablingLsp: boolean
  onEnableLsp: () => void
  onTryInChat: () => void
  onOpenSkill: (skillName: string) => void
}): JSX.Element {
  const t = useT()
  const [detailMenuPosition, setDetailMenuPosition] = useState<ContextMenuPosition | null>(null)
  const info = plugin.interface
  const shouldOfferLspEnable = plugin.installed
    && plugin.enabled
    && (plugin.lspServers ?? []).some((server) => server.enabled && !server.active && !server.shadowedBy)
  const contents = getPluginContentSummaries(plugin, t)
  return (
    <div style={page}>
      <CatalogTopBar
        navigation={(
          <CatalogBreadcrumb
            parentLabel={t('plugins.pageTitle')}
            currentLabel={pluginTitle(plugin)}
            onBack={onBack}
          />
        )}
      />
      <header style={detailHeader}>
        <div style={detailIconRow}>
          <PluginIcon plugin={plugin} size={64} />
          <div style={{ flex: 1 }} />
          <ActionTooltip label={t('plugins.detail.website')}>
            <a
              href={resolvePluginExternalUrl(info?.websiteUrl) ?? DOTCRAFT_PLUGIN_FALLBACK_URL}
              style={detailIconButton}
              aria-label={t('plugins.detail.website')}
              onClick={(event) => handlePluginExternalLinkClick(event, info?.websiteUrl)}
            >
              <Link size={15} aria-hidden />
            </a>
          </ActionTooltip>
          {plugin.installed && (
            <IconButton
              icon={<Ellipsis size={16} aria-hidden />}
              label={t('plugins.moreActions')}
              onClick={(event) => {
                event.stopPropagation()
                const rect = event.currentTarget.getBoundingClientRect()
                setDetailMenuPosition({ x: rect.right - 200, y: rect.bottom + 4 })
              }}
            />
          )}
          {plugin.installed ? (
            <PluginInstallButton variant="primary" disabled={!plugin.enabled} onClick={onTryInChat} iconLeft={<MessageCircle size={14} />}>
              {t('plugins.tryInChat')}
            </PluginInstallButton>
          ) : (
            <PluginInstallButton variant="primary" onClick={onInstall} iconLeft={<Plus size={14} />}>
              {t('plugins.install')}
            </PluginInstallButton>
          )}
        </div>
        <h1 style={detailTitle}>{pluginTitle(plugin)}</h1>
        <p style={detailSubtitle}>{pluginSubtitle(plugin)}</p>
      </header>
      <main style={detailMain}>
        <div style={detailContent}>
          {loading && <p style={emptyText}>{t('plugins.loading')}</p>}
          <div style={promptPreview}>
            <span style={promptBubble}>
              <span style={promptBubblePrefix}>
                <PluginIcon plugin={plugin} size={18} />
                <strong style={promptBubbleTitle}>{pluginTitle(plugin)}</strong>
              </span>
              <span style={promptBubbleText}>{info?.defaultPrompt || t('plugins.defaultPromptFallback')}</span>
            </span>
          </div>
          <p style={longDescription}>{info?.longDescription || plugin.description}</p>
          <AppBindingPanel plugin={plugin} />
          <section style={detailSection}>
            <h2 style={detailSectionTitle}>{t('plugins.detail.contents')}</h2>
            {contents.length > 0 ? (
              <div style={contentList}>
                {contents.map((item) => {
                  const body = (
                    <>
                      <span style={contentIcon}>
                        <PluginContentIcon type={item.type} size={16} />
                      </span>
                      <span style={pluginText}>
                        <span style={contentTitleLine}>
                          <strong style={rowTitle}>{item.title}</strong>
                          <span style={contentKind}>{item.kind}</span>
                        </span>
                        <span style={rowDesc}>{item.description}</span>
                      </span>
                    </>
                  )
                  // Only a skill has a document to preview; the other kinds are
                  // descriptions of runtime wiring with nothing to open.
                  if (item.skillName == null) {
                    return <div key={item.key} style={contentItem}>{body}</div>
                  }
                  return (
                    <CatalogHoverButton
                      key={item.key}
                      type="button"
                      baseStyle={contentItemButton}
                      onClick={() => onOpenSkill(item.skillName!)}
                    >
                      {body}
                    </CatalogHoverButton>
                  )
                })}
              </div>
            ) : (
              <p style={emptyText}>{t('plugins.detail.noContents')}</p>
            )}
          </section>
          {shouldOfferLspEnable && (
            <div style={lspEnablePanel} role="status">
              <span style={rowDesc}>{t('plugins.lsp.enablePrompt')}</span>
              <Button disabled={enablingLsp} onClick={onEnableLsp} iconLeft={<Code2 size={14} />}>
                {enablingLsp ? t('plugins.lsp.enabling') : t('plugins.lsp.enable')}
              </Button>
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
        </div>
      </main>
      {detailMenuPosition && (
        <ContextMenu
          position={detailMenuPosition}
          onClose={() => setDetailMenuPosition(null)}
          items={[
            {
              label: t('plugins.manage'),
              icon: <Settings size={14} />,
              onClick: onManage
            },
            ...(plugin.removable
              ? [{
                  label: t('plugins.uninstall'),
                  icon: <Trash2 size={14} />,
                  danger: true,
                  onClick: onRemove
                }]
              : [])
          ]}
        />
      )}
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
        <ActionTooltip label={label}>
          <a
            href={resolvedHref}
            style={plainLink}
            aria-label={label}
            onClick={(event) => handlePluginExternalLinkClick(event, href)}
          >
            <ExternalLink size={14} aria-hidden />
          </a>
        </ActionTooltip>
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

// AppServer surfaces validation failures as `Invalid params: <reason>`. Strip the
// generic JSON-RPC prefix so the toast shows just the actionable reason; fall back to
// the full message when the prefix is absent.
function extractInstallErrorDetail(message: string): string {
  const trimmed = message.trim()
  const prefix = 'Invalid params: '
  return (trimmed.startsWith(prefix) ? trimmed.slice(prefix.length) : trimmed).trim()
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
    if (publisherFilter === 'marketplaces' && !plugin.marketplaceName) return false
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

interface PluginSection {
  key: string
  title: string
  plugins: PluginEntry[]
  /** Present when the section groups a marketplace, which owns refresh and remove. */
  marketplace?: MarketplaceEntry
}

function buildSections(
  plugins: PluginEntry[],
  categoryFilter: CategoryFilter,
  publisherFilter: PublisherFilter,
  t: ReturnType<typeof useT>,
  marketplaces: MarketplaceEntry[]
): PluginSection[] {
  // Grouping by marketplace is the one mode that asks "where did this come from",
  // so it answers only that: no installed-state group, and the category filter
  // still narrows what each group contains.
  if (publisherFilter === 'marketplaces') {
    const sections: PluginSection[] = []
    for (const marketplace of marketplaces) {
      const owned = plugins.filter((plugin) => plugin.marketplaceName === marketplace.name)
      if (owned.length === 0) continue
      sections.push({
        key: `marketplace:${marketplace.name}`,
        title: marketplaceTitle(marketplace),
        plugins: owned,
        marketplace
      })
    }
    return sections
  }

  if (categoryFilter === 'featured') {
    return plugins.length > 0 ? [{ key: 'featured', title: t('plugins.section.featured'), plugins }] : []
  }

  if (categoryFilter !== 'all') {
    return plugins.length > 0 ? [{ key: categoryFilter, title: categoryLabel(categoryFilter, t), plugins }] : []
  }

  const local = plugins.filter(isLocalInstalledPlugin)
  const seen = new Set(local.map((plugin) => plugin.id))
  const sections: PluginSection[] = []
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
  if (category === 'security') return t('plugins.filter.category.security')
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

function PluginContentIcon({ type, size }: { type: PluginContentType; size: number }): JSX.Element {
  if (type === 'app') return <Link size={size} aria-hidden />
  if (type === 'desktopExtension') return <Settings size={size} aria-hidden />
  if (type === 'hooks') return <Anchor size={size} aria-hidden />
  if (type === 'skill') return <Box size={size} aria-hidden />
  if (type === 'mcp') return <Server size={size} aria-hidden />
  if (type === 'lsp') return <Code2 size={size} aria-hidden />
  return <Wrench size={size} aria-hidden />
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
const heroTitle: CSSProperties = catalogStyles.heroTitle
const searchRow: CSSProperties = catalogStyles.searchRow
const browseMain: CSSProperties = catalogStyles.browseMain
const sectionTitle: CSSProperties = catalogStyles.sectionTitle
const compactGrid: CSSProperties = catalogStyles.compactGrid
const compactItem: CSSProperties = catalogStyles.compactItem
const rowTitle: CSSProperties = catalogStyles.rowTitle
const rowDesc: CSSProperties = catalogStyles.rowDesc
const manageHeader: CSSProperties = catalogStyles.manageHeader
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
const manageMain: CSSProperties = catalogStyles.manageMain
const manageRow: CSSProperties = catalogStyles.manageRow
const emptyText: CSSProperties = catalogStyles.emptyText

function interactiveManageRow(active: boolean): CSSProperties {
  return {
    ...manageRow,
    borderRadius: '8px',
    padding: '0 8px',
    boxSizing: 'border-box',
    backgroundColor: active ? 'var(--bg-tertiary)' : 'transparent',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

const manageItemMain: CSSProperties = { ...compactItem, flex: 1, padding: 0, height: 'auto' }
const manageSource: CSSProperties = {
  width: '86px',
  flexShrink: 0,
  color: 'var(--text-secondary)',
  fontSize: '13px',
  textAlign: 'left',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
// A section title carries the section's column geometry, not just its type: the
// 760px width and `0 auto` centering are what line a heading up with the grid
// beneath it. Splitting the two lets the header row take the column while the
// heading keeps the type, instead of the row spanning the full main padding.
const { maxWidth: sectionColumnWidth, margin: sectionColumnMargin,
  ...sectionTitleType } = sectionTitle

const marketplaceHeaderRow: CSSProperties = {
  maxWidth: sectionColumnWidth,
  margin: sectionColumnMargin,
  display: 'flex',
  alignItems: 'center',
  gap: '10px'
}
// `minWidth: 0` lets a long marketplace name truncate instead of pushing the row
// wider than its column; a flex item defaults to its content's minimum width.
const marketplaceHeaderTitle: CSSProperties = {
  ...sectionTitleType,
  margin: 0,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
function revealedActionStyle(visible: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    opacity: visible ? 1 : 0,
    pointerEvents: visible ? 'auto' : 'none',
    transition: 'opacity 120ms ease'
  }
}
// Fixed-width, centered slot for the trailing control so the Install button and the
// PillSwitch share one column: this keeps the developer column at a constant x across
// installed/uninstalled rows and centers both controls on the same vertical line.
const manageActionSlot: CSSProperties = {
  width: '84px',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center'
}
const pluginText: CSSProperties = { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 }
const detailMain: CSSProperties = { flex: 1, minHeight: 0, overflow: 'auto', width: '100%' }
const detailContent: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '0 auto', padding: '0 0 48px' }
const detailHeader: CSSProperties = { width: 'min(760px, calc(100% - 48px))', margin: '22px auto 28px' }
const detailIconRow: CSSProperties = { display: 'flex', alignItems: 'flex-start', gap: 12 }
const detailTitle: CSSProperties = { margin: '20px 0 6px', fontSize: 28, fontWeight: 600 }
const detailSubtitle: CSSProperties = { margin: 0, color: 'var(--text-secondary)', fontSize: 15 }
const detailIconButton: CSSProperties = { width: 32, height: 32, borderRadius: 8, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)', textDecoration: 'none' }
const promptPreview: CSSProperties = { minHeight: 132, borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'linear-gradient(120deg, #b6cdf5, #d9cef7 58%, #f3f0fb)', padding: '18px 24px', boxSizing: 'border-box' }
const promptBubble: CSSProperties = { display: 'inline-flex', alignItems: 'center', flexWrap: 'wrap', columnGap: 7, rowGap: 4, maxWidth: '80%', border: '1px solid rgba(0,0,0,0.12)', borderRadius: 13, background: 'rgba(255,255,255,0.82)', color: '#111', padding: '8px 12px', fontSize: 13, lineHeight: 1.35 }
const promptBubblePrefix: CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: 7, flex: '0 1 auto', minWidth: 0, maxWidth: '100%', whiteSpace: 'nowrap' }
const promptBubbleTitle: CSSProperties = { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const promptBubbleText: CSSProperties = { flex: '1 1 180px', minWidth: 0, whiteSpace: 'normal', overflowWrap: 'anywhere' }
const longDescription: CSSProperties = { margin: '54px 8px 40px', lineHeight: 1.55, fontSize: 14, color: 'var(--text-primary)' }
// Detail sections are frameless: a section is marked by a rule under its heading,
// not by a box around its rows, so stacked groups read as one column instead of a
// stack of cards. See the Detail Sections part of DESIGN.md.
const detailSection: CSSProperties = { marginTop: 28 }
const detailSectionTitle: CSSProperties = {
  margin: '0 0 8px',
  paddingBottom: 8,
  borderBottom: '1px solid var(--border-subtle)',
  fontSize: 15,
  fontWeight: 600
}
const contentList: CSSProperties = { display: 'flex', flexDirection: 'column' }
const contentItem: CSSProperties = { display: 'flex', alignItems: 'center', gap: 12, padding: '8px 0' }
// An openable row keeps the same rhythm as a static one, so the list does not
// change shape; the hover fill is what marks it as reachable.
const contentItemButton: CSSProperties = {
  ...contentItem,
  width: 'calc(100% + 16px)',
  marginInline: -8,
  padding: '8px',
  border: 'none',
  borderRadius: 8,
  background: 'transparent',
  color: 'inherit',
  font: 'inherit',
  textAlign: 'left',
  cursor: 'pointer'
}
const contentIcon: CSSProperties = { width: 38, height: 38, borderRadius: 19, border: '1px solid var(--border-default)', display: 'inline-flex', alignItems: 'center', justifyContent: 'center', color: 'var(--text-secondary)' }
const contentTitleLine: CSSProperties = { display: 'inline-flex', alignItems: 'baseline', gap: 5, minWidth: 0 }
const contentKind: CSSProperties = { fontWeight: 400, color: 'var(--text-secondary)' }
const lspEnablePanel: CSSProperties = { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', marginTop: 16 }
const infoTable: CSSProperties = { display: 'flex', flexDirection: 'column' }
const infoRow: CSSProperties = { display: 'grid', gridTemplateColumns: '180px 1fr', alignItems: 'center', minHeight: 32 }
const infoLabel: CSSProperties = { color: 'var(--text-secondary)', fontSize: 13, padding: '6px 0' }
const infoValue: CSSProperties = { fontSize: 13, padding: '6px 0' }
const plainLink: CSSProperties = { color: 'var(--accent)', display: 'inline-flex' }
const diagnosticsPanel: CSSProperties = { border: '1px solid var(--border-default)', borderRadius: 8, background: 'var(--bg-secondary)', padding: '12px 14px', margin: '0 0 24px' }
const diagnosticsTitle: CSSProperties = { display: 'block', fontSize: 13, marginBottom: 8, color: 'var(--text-primary)' }
const diagnosticsList: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 7 }
const diagnosticItem: CSSProperties = { display: 'grid', gridTemplateColumns: 'minmax(120px, max-content) minmax(0, 1fr)', columnGap: 10, rowGap: 3, alignItems: 'baseline', fontSize: 12 }
const diagnosticCode: CSSProperties = { color: 'var(--warning, #A16207)', fontFamily: 'var(--font-mono)' }
const diagnosticMessage: CSSProperties = { color: 'var(--text-secondary)', minWidth: 0 }
const diagnosticPath: CSSProperties = { gridColumn: '1 / -1', color: 'var(--text-tertiary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }
const diagnosticMore: CSSProperties = { color: 'var(--text-tertiary)', fontSize: 12 }
