import { existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { Server } from 'lucide-react'

import type { MessageKey } from '../../shared/locales'
import { McpIcon } from '../components/settings/McpIcon'
import { buildSettingsTabs } from '../components/settings/settingsTabs'

const t = (key: MessageKey): string => key

describe('settings tabs', () => {
  it('uses a local MCP icon distinct from Servers', () => {
    const tabs = buildSettingsTabs(t, {
      personalizationAvailable: false,
      mcpEnabled: true,
      subAgentEnabled: false
    })

    const serversTab = tabs.find((tab) => tab.id === 'servers')
    const mcpTab = tabs.find((tab) => tab.id === 'mcp')

    expect(serversTab?.icon).toBe(Server)
    expect(mcpTab?.icon).toBe(McpIcon)
    expect(mcpTab?.icon).not.toBe(serversTab?.icon)
  })

  it('vendors the MCP SVG asset locally', () => {
    expect(existsSync(resolve(__dirname, '../assets/icons/mcp.svg'))).toBe(true)
  })
})
