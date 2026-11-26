import { installDesktopPluginRuntime as installAuthoringRuntime } from '@dotcraft/plugin/runtime'
import { AvatarOutfitProvider } from '@dotcraft/avatar/react'
import * as React from 'react'
import { createPortal } from 'react-dom'
import * as JsxRuntime from 'react/jsx-runtime'

import { RobotAvatar } from '../components/agents/RobotAvatar'
import { CatalogSection, CatalogToolbarIconButton } from '../components/catalog/CatalogSurface'
import { ForegroundWorkspaceName } from '../components/projects/ProjectNameMenu'
import { DesktopPluginCatalogPage } from '../components/desktopPlugins/DesktopPluginCatalogPage'
import { DesktopPluginInlineDiff } from '../components/desktopPlugins/DesktopPluginInlineDiff'
import { DesktopPluginMarkdown } from '../components/desktopPlugins/DesktopPluginMarkdown'
import { DesktopPluginUserAvatar } from '../components/desktopPlugins/DesktopPluginUserAvatar'
import { DesktopPluginPersonalMascot } from '../components/desktopPlugins/DesktopPluginPersonalMascot'
import { DesktopPluginMenu } from '../components/desktopPlugins/DesktopPluginMenu'
import { DesktopPluginSegmentedControl } from '../components/desktopPlugins/DesktopPluginSegmentedControl'
import { DesktopPluginSplitButton } from '../components/desktopPlugins/DesktopPluginSplitButton'
import { DesktopPluginSummaryRow, DesktopPluginSummarySection } from '../components/desktopPlugins/DesktopPluginSummary'
import { DesktopPluginSidebarRow } from '../components/desktopPlugins/DesktopPluginSidebarRow'
import { DesktopPluginSurface } from '../components/desktopPlugins/DesktopPluginSurface'
import { SettingsBreadcrumb } from '../components/settings/SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../components/settings/SettingsGroup'
import { SettingsPanelShell } from '../components/settings/SettingsPanelShell'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { DesktopPluginToolDisclosure } from '../components/desktopPlugins/DesktopPluginToolDisclosure'
import { RunningShimmer } from '../components/ui/RunningShimmer'
import { Button } from '../components/ui/Button'
import { Checkbox } from '../components/ui/Checkbox'
import { Combobox } from '../components/ui/Combobox'
import { IconButton } from '../components/ui/IconButton'
import { Input, Textarea } from '../components/ui/Input'
import { LayerBoundary } from '../contexts/LayerContext'
import { ModalHeader } from '../components/ui/ModalHeader'
import { PillSwitch } from '../components/ui/PillSwitch'
import { Select } from '../components/ui/Select'
import { Skeleton } from '../components/ui/Skeleton'
import { Slider } from '../components/ui/Slider'
import { Spinner } from '../components/ui/Spinner'

import { DesktopPluginRuntime } from './desktopPluginLifecycle'
import { clearDesktopPluginError, observeDesktopPluginSource, reportDesktopPluginError } from './desktopPluginSource'
export { DesktopPluginRuntime } from './desktopPluginLifecycle'
export type { DesktopPluginRuntimeDependencies } from './desktopPluginLifecycle'

let runtime: DesktopPluginRuntime | null = null
let unsubscribeStore: (() => void) | null = null

export function startDesktopPluginRuntime(): () => void {
  if (runtime) return stopDesktopPluginRuntime
  installAuthoringRuntime({
    react: React as Parameters<typeof installAuthoringRuntime>[0]['react'],
    jsxRuntime: JsxRuntime,
    reactDom: { createPortal },
    ui: {
      ToolDisclosure: DesktopPluginToolDisclosure,
      RunningShimmer,
      Button,
      IconButton,
      Input,
      Textarea,
      Select,
      SegmentedControl: DesktopPluginSegmentedControl,
      Checkbox,
      Spinner,
      Skeleton,
      Slider,
      ActionTooltip,
      Combobox,
      LayerBoundary,
      ModalHeader,
      PillSwitch,
      SettingsPanelShell,
      SettingsBreadcrumb,
      SettingsGroup,
      SettingsRow,
      InlineDiff: DesktopPluginInlineDiff,
      Markdown: DesktopPluginMarkdown,
      UserAvatar: DesktopPluginUserAvatar,
      PersonalMascot: DesktopPluginPersonalMascot,
      AgentAvatar: RobotAvatar,
      Menu: DesktopPluginMenu,
      SplitButton: DesktopPluginSplitButton,
      CatalogPage: DesktopPluginCatalogPage,
      CatalogToolbarButton: CatalogToolbarIconButton,
      CatalogSection,
      SummarySection: DesktopPluginSummarySection,
      SummaryRow: DesktopPluginSummaryRow,
      SidebarRow: DesktopPluginSidebarRow,
      MascotOutfit: AvatarOutfitProvider,
      WorkspaceName: ForegroundWorkspaceName,
      PluginSurface: DesktopPluginSurface
    }
  })
  runtime = new DesktopPluginRuntime({
    registerModule: (params) => window.api.desktopPlugins.registerModule(params),
    removeModule: (params) => window.api.desktopPlugins.removeModule(params),
    onError: (pluginId, error) => reportDesktopPluginError(pluginId, error),
    onActivated: clearDesktopPluginError,
    importModule: (url) => import(/* @vite-ignore */ url)
  })
  unsubscribeStore = observeDesktopPluginSource(runtime)
  return stopDesktopPluginRuntime
}

export function stopDesktopPluginRuntime(): void {
  unsubscribeStore?.()
  unsubscribeStore = null
  const activeRuntime = runtime
  runtime = null
  if (activeRuntime) void activeRuntime.stop()
}
