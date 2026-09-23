import { installDesktopPluginRuntime as installAuthoringRuntime } from '@dotcraft/plugin/runtime'
import * as React from 'react'
import { createPortal } from 'react-dom'
import * as JsxRuntime from 'react/jsx-runtime'

import { DesktopPluginInlineDiff } from '../components/desktopPlugins/DesktopPluginInlineDiff'
import { DesktopPluginSegmentedControl } from '../components/desktopPlugins/DesktopPluginSegmentedControl'
import { DesktopPluginSurface } from '../components/desktopPlugins/DesktopPluginSurface'
import { SettingsBreadcrumb } from '../components/settings/SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../components/settings/SettingsGroup'
import { SettingsPanelShell } from '../components/settings/SettingsPanelShell'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { Button } from '../components/ui/Button'
import { Checkbox } from '../components/ui/Checkbox'
import { Combobox } from '../components/ui/Combobox'
import { IconButton } from '../components/ui/IconButton'
import { Input, Textarea } from '../components/ui/Input'
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
      ModalHeader,
      PillSwitch,
      SettingsPanelShell,
      SettingsBreadcrumb,
      SettingsGroup,
      SettingsRow,
      InlineDiff: DesktopPluginInlineDiff,
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
