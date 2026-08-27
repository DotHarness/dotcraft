// Global test setup
// Extend expect with jest-dom matchers for DOM assertions
import '@testing-library/jest-dom'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import * as React from 'react'
import * as JsxRuntime from 'react/jsx-runtime'
import { createPortal } from 'react-dom'
import { installDesktopPluginRuntime } from '@dotcraft/plugin/runtime'
import { Button } from '../components/ui/Button'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { Checkbox } from '../components/ui/Checkbox'
import { Combobox } from '../components/ui/Combobox'
import { IconButton } from '../components/ui/IconButton'
import { Input, Textarea } from '../components/ui/Input'
import { ModalHeader } from '../components/ui/ModalHeader'
import { PillSwitch } from '../components/ui/PillSwitch'
import { RunningSpinner } from '../components/ui/RunningSpinner'
import { Select } from '../components/ui/Select'
import { Skeleton } from '../components/ui/Skeleton'
import { SettingsBreadcrumb } from '../components/settings/SettingsBreadcrumb'
import { SettingsGroup, SettingsRow } from '../components/settings/SettingsGroup'
import { SettingsPanelShell } from '../components/settings/SettingsPanelShell'
import { DesktopPluginInlineDiff } from '../components/desktopPlugins/DesktopPluginInlineDiff'

installDesktopPluginRuntime({
  react: React as Parameters<typeof installDesktopPluginRuntime>[0]['react'],
  jsxRuntime: JsxRuntime,
  reactDom: { createPortal },
  ui: {
    Button,
    Checkbox,
    IconButton,
    Input,
    Textarea,
    Spinner: RunningSpinner,
    Select,
    Skeleton,
    ActionTooltip,
    Combobox,
    ModalHeader,
    PillSwitch,
    SettingsPanelShell,
    SettingsBreadcrumb,
    SettingsGroup,
    SettingsRow,
    InlineDiff: DesktopPluginInlineDiff
  }
})

afterEach(() => {
  cleanup()
  vi.clearAllTimers()
  vi.useRealTimers()
})
