import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')
const srcRoot = resolve(rendererRoot, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8')
}

function readSrcFile(path: string): string {
  return readFileSync(resolve(srcRoot, path), 'utf8')
}

describe('settings panel shell', () => {
  it('centralizes settings page headers and group spacing', () => {
    const shellSource = readRendererFile('components/settings/SettingsPanelShell.tsx')

    expect(shellSource).toContain('SettingsPageHeader')
  })

  it('uses the shared shell for major settings tabs and archived threads', () => {
    const settingsSource = readRendererFile('components/settings/SettingsView.tsx')
    const archivedSource = readRendererFile('components/settings/ArchivedThreadsSettingsView.tsx')
    const subAgentsSource = readRendererFile('components/settings/panels/SubAgentsPanel.tsx')

    expect(settingsSource).toContain("import { SettingsPanelShell } from './SettingsPanelShell'")
    expect(settingsSource).not.toContain("import { SettingsPageHeader } from './SettingsPageHeader'")
    expect(settingsSource).toContain("description={t('settings.general.description')}")
    expect(settingsSource).toContain("description={t('settings.connection.description')}")
    expect(settingsSource).toContain("description={t('settings.usage.description')}")
    expect(settingsSource).toContain("title={t('settings.group.application')}")
    expect(settingsSource).toContain("title={t('settings.group.connectionMode')}")
    expect(settingsSource).toContain("title={t('settings.group.localAppServer')}")
    expect(settingsSource).toContain("title={t('settings.group.identity')}")
    expect(settingsSource).toContain("title={t('settings.group.command')}")
    expect(settingsSource).toContain("title={t('settings.group.environment')}")
    expect(settingsSource).toContain("title={t('settings.group.http')}")
    expect(settingsSource).toContain("title={t('settings.chrome.connectionStatus')}")

    expect(archivedSource).toContain('SettingsPanelShell')
    expect(archivedSource).toContain("title={t('settings.group.conversations')}")
    expect(subAgentsSource).toContain('SettingsPanelShell')
    expect(subAgentsSource).toContain("title={t('settings.group.workspaceSettings')}")
  })

  it('defines the unified English and Chinese copy keys', () => {
    const englishSource = readSrcFile('shared/locales/messages/en.ts')
    const chineseSource = readSrcFile('shared/locales/messages/zh-Hans.ts')

    expect(englishSource).toContain("'settings.general.description': 'Set language, theme, notifications, and workspace permissions.'")
    expect(chineseSource).toContain("'settings.general.description': '设置界面语言、主题、通知和工作区权限。'")
    expect(englishSource).toContain("'settings.connection.description': 'Choose how DotCraft connects to this workspace.'")
    expect(chineseSource).toContain("'settings.connection.description': '选择 DotCraft 如何连接当前工作区。'")
    expect(englishSource).toContain("'settings.usage.dashboardUnavailable': 'No hosted Dashboard URL is available for this workspace yet.'")
    expect(chineseSource).toContain("'settings.usage.dashboardUnavailable': '当前工作区暂未提供服务器托管的 Dashboard 地址。'")
    expect(englishSource).toContain("'settings.group.workspaceSettings': 'Workspace settings'")
    expect(chineseSource).toContain("'settings.group.workspaceSettings': '工作区设置'")
    expect(englishSource).toContain("'settings.group.profileDetails': 'Profile details'")
    expect(chineseSource).toContain("'settings.group.profileDetails': '助手详情'")
  })
})
