import { useEffect, useMemo, useState } from 'react'
import { AtSign, FolderInput, Store } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import {
  operationFailureMessage,
  usePluginStore,
  type MarketplaceEntry,
  type PluginEntry
} from '../../stores/pluginStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { SkillsView, filterLocalSkills, stripYamlFrontmatter } from '../skills/SkillsView'
import { SkillDetailDialog } from '../skills/SkillDetailDialog'
import { stageSkillTryInChat } from '../skills/skillDraft'
import type { SplitButtonItem } from '../ui/SplitButton'
import { pluginTitle } from './PluginCatalogItem'
import { PluginInstallDialog } from './PluginInstallDialog'
import { PluginBrowseSurface, PluginSurfaceTabs } from './PluginBrowseSurface'
import { PluginDetailView } from './PluginDetailView'
import { filterVisibleDiagnostics } from './PluginDiagnosticsBanner'
import { PluginManageSurface } from './PluginManageSurface'
import { PLUGIN_CREATOR_SKILL, stagePluginCreationInChat, stagePluginTryInChat } from './pluginDraft'
import {
  buildCategoryOptions,
  buildSections,
  filterPlugins,
  marketplaceTitle,
  type CategoryFilter,
  type PublisherFilter
} from './pluginCatalogModel'

export { marketplaceTitle } from './pluginCatalogModel'

type PluginMode = 'browse' | 'manage'
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
    setPluginTrusted,
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
  // The dialog owns installed/app state for its own session and takes the live trust state from
  // the store, so completing the trust step advances the dialog without reopening it.
  const installDialogTrust = installTarget
    ? plugins.find((entry) => entry.id === installTarget.id)?.dotnetRuntime
    : undefined
  const installDialogPlugin = installTarget
    ? (installDialogTrust ? { ...installTarget, dotnetRuntime: installDialogTrust } : installTarget)
    : null
  const installDialog = installDialogPlugin ? (
    <PluginInstallDialog
      plugin={installDialogPlugin}
      installing={installingId === installDialogPlugin.id}
      onClose={() => setInstallTarget(null)}
      onInstall={async () => {
        try {
          setInstallingId(installDialogPlugin.id)
          // Copying the bundle is not authority: an in-process plugin still owes the trust step.
          const keepOpenForSetup = (installDialogPlugin.apps ?? []).length > 0 || installDialogPlugin.dotnet != null
          await installPlugin(installDialogPlugin.id)
          await fetchSkills()
          await fetchPlugins()
          await selectPlugin(installDialogPlugin.id)
          setInstallTarget(keepOpenForSetup
            ? { ...installDialogPlugin, installed: true, enabled: true, installable: false }
            : null)
          addToast(t('plugins.installSuccess'), 'success')
        } catch {
          addToast(t('plugins.installFailed'), 'error')
        } finally {
          setInstallingId(null)
        }
      }}
      onTrust={async () => {
        await setPluginTrusted(installDialogPlugin.id, true)
        await fetchPlugins()
        if (!installDialogPlugin.enabled) await togglePluginEnabled(installDialogPlugin.id, true)
        if ((installDialogPlugin.apps ?? []).length === 0) setInstallTarget(null)
      }}
    />
  ) : null

  // The skill mention is only staged when the skill actually resolves, so the composer
  // never shows a chip that cannot be resolved.
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
    stagePluginCreationInChat(t('plugins.create.prompt'), hasCreatorSkill)
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

  // The backend validates the folder before copying anything and returns the reason on
  // failure, which the toast surfaces.
  async function handleInstallFromDisk(): Promise<void> {
    const proceed = await confirm({
      title: t('plugins.installLocal.confirm.title'),
      message: t('plugins.installLocal.confirm.message'),
      confirmLabel: t('plugins.installLocal.confirm.action'),
      cancelLabel: t('common.cancel')
    })
    if (!proceed) return

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
      // Only now is it known whether the folder carried in-process code, and the trust step
      // it owes is the same one a catalog install completes.
      if (installed?.dotnet) setInstallTarget(installed)
      addToast(t('plugins.installLocal.success'), 'success')
    } catch (err) {
      const detail = err instanceof Error ? extractInstallErrorDetail(err.message) : ''
      addToast(detail || t('plugins.installLocal.failed'), 'error')
    }
  }

  async function handleToggleEnabled(plugin: PluginEntry, enabled: boolean): Promise<void> {
    // Enabling an in-process plugin that has not been trusted asks for the authority first,
    // in the same setup dialog the install flow uses.
    if (enabled && plugin.dotnet && plugin.dotnetRuntime?.trustStatus !== 'trusted') {
      setInstallTarget(plugin)
      return
    }
    try {
      await togglePluginEnabled(plugin.id, enabled)
      await fetchSkills()
    } catch {
      addToast(t('plugins.updateFailed'), 'error')
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
        topNavigation={<PluginSurfaceTabs value={surface} onChange={setSurface} />}
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
              const result = await removePlugin(selectedPlugin.id)
              await fetchPlugins()
              await fetchSkills()
              if (result.outcome === 'notApplied') {
                addToast(operationFailureMessage(result) ?? t('plugins.uninstallFailed'), 'error')
              } else {
                addToast(t('plugins.uninstallSuccess'), 'success')
              }
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
          onTryInChat={() => stagePluginTryInChat(selectedPlugin)}
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
        <PluginManageSurface
          surface={surface}
          pluginManagement={pluginManagement}
          loading={loading}
          error={error}
          diagnostics={visibleDiagnostics}
          plugins={managePlugins}
          pluginCount={plugins.length}
          pluginQuery={managePluginQuery}
          skills={manageSkills}
          skillsLoading={skillsLoading}
          skillsError={skillsError}
          skillsCount={skills.length}
          skillQuery={skillManageQuery}
          onBack={() => setMode('browse')}
          onSurfaceChange={setSurface}
          onPluginQueryChange={setManagePluginQuery}
          onSkillQueryChange={setSkillManageQuery}
          onOpenPlugin={(plugin) => void selectPlugin(plugin.id)}
          onInstallPlugin={setInstallTarget}
          onTogglePlugin={(plugin, enabled) => void handleToggleEnabled(plugin, enabled)}
          onToggleSkill={async (skill, enabled) => {
            try {
              await toggleSkillEnabled(skill.name, enabled)
            } catch {
              addToast(t('skills.updateFailed'), 'error')
            }
          }}
        />
        {installDialog}
      </>
    )
  }

  return (
    <PluginBrowseSurface
      surface={surface}
      pluginManagement={pluginManagement}
      pluginMarketplaces={pluginMarketplaces}
      remoteWorkspaceActive={remoteWorkspaceActive}
      loading={loading}
      error={error}
      diagnostics={visibleDiagnostics}
      plugins={browsePlugins}
      query={browseQuery}
      publisherFilter={publisherFilter}
      categoryFilter={categoryFilter}
      categoryOptions={categoryOptions}
      marketplaces={marketplaces}
      sections={sections}
      createActions={createActions}
      addMarketplaceOpen={addMarketplaceOpen}
      installDialog={installDialog}
      onSurfaceChange={setSurface}
      onRefresh={() => void fetchPlugins()}
      onManage={() => { setManagePluginQuery(''); setMode('manage') }}
      onQueryChange={setBrowseQuery}
      onPublisherFilterChange={setPublisherFilter}
      onCategoryFilterChange={setCategoryFilter}
      onOpenPlugin={(plugin) => void selectPlugin(plugin.id)}
      onTryPlugin={stagePluginTryInChat}
      onInstallPlugin={setInstallTarget}
      onRefreshMarketplace={(marketplace) => void handleRefreshMarketplace(marketplace)}
      onRemoveMarketplace={(marketplace) => void handleRemoveMarketplace(marketplace)}
      onCloseAddMarketplace={() => setAddMarketplaceOpen(false)}
      onMarketplaceAdded={(marketplace, alreadyAdded) => {
        setAddMarketplaceOpen(false)
        addToast(
          alreadyAdded
            ? t('plugins.marketplace.add.alreadyAdded', { name: marketplaceTitle(marketplace) })
            : t('plugins.marketplace.add.success', { name: marketplaceTitle(marketplace) }),
          'success'
        )
      }}
    />
  )
}

// AppServer surfaces validation failures as `Invalid params: <reason>`. Strip the
// generic JSON-RPC prefix so the toast shows just the actionable reason; fall back to
// the full message when the prefix is absent.
function extractInstallErrorDetail(message: string): string {
  const trimmed = message.trim()
  const prefix = 'Invalid params: '
  return (trimmed.startsWith(prefix) ? trimmed.slice(prefix.length) : trimmed).trim()
}
