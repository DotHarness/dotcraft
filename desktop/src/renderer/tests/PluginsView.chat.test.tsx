import { fireEvent, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import type { PluginEntry } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'
import {
  appServerSendRequest,
  localPlugin,
  mcpOnlyPlugin,
  renderPluginsView,
  setupPluginsViewTest
} from './pluginsViewTestFixtures'

describe('PluginsView Try in chat', () => {
  beforeEach(setupPluginsViewTest)

  it('does not generate a skill mention for MCP-only plugin try in chat', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [mcpOnlyPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: mcpOnlyPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools MCP'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('Review this change.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })

  it('prefers an enabled plugin-id skill for try in chat', async () => {
    const reviewPlugin: PluginEntry = {
      ...localPlugin,
      id: 'review-tools',
      displayName: 'Review Tools',
      interface: {
        ...localPlugin.interface,
        displayName: 'Review Tools',
        defaultPrompt: 'Please review this change.'
      },
      skills: [
        { name: 'review-tools', description: 'Route reviews.', enabled: true },
        { name: 'review-code', description: 'Review code.', enabled: true }
      ]
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [reviewPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: reviewPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('$review-tools Please review this change.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'skill', skillName: 'review-tools' }
    ])
  })

  it('uses the only enabled skill for try in chat when no entry skill exists', async () => {
    const singleSkillPlugin: PluginEntry = {
      ...localPlugin,
      id: 'review-tools',
      displayName: 'Review Tools',
      interface: {
        ...localPlugin.interface,
        displayName: 'Review Tools',
        defaultPrompt: 'Please review this change.'
      },
      skills: [{ name: 'review-change', description: 'Review changes.', enabled: true }]
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [singleSkillPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: singleSkillPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('$review-change Please review this change.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'skill', skillName: 'review-change' }
    ])
  })

  it('opens the consolidated DotCraft plugin without choosing an arbitrary skill', async () => {
    const dotcraftPlugin: PluginEntry = {
      ...localPlugin,
      id: 'dotcraft',
      displayName: 'DotCraft',
      interface: {
        ...localPlugin.interface,
        displayName: 'DotCraft',
        defaultPrompt: 'Help me develop or troubleshoot DotCraft using the appropriate workflow.'
      },
      skills: [
        { name: 'dotcraft-dev-guide', description: 'Develop DotCraft.', enabled: true },
        { name: 'dotcraft-doctor', description: 'Route troubleshooting.', enabled: true },
        { name: 'dotcraft-error-diagnosis', description: 'Diagnose failures.', enabled: true }
      ]
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [dotcraftPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: dotcraftPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('DotCraft'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('Help me develop or troubleshoot DotCraft using the appropriate workflow.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })
})
