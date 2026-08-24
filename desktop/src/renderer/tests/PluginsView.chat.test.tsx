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
    const doctorPlugin: PluginEntry = {
      ...localPlugin,
      id: 'dotcraft-doctor',
      displayName: 'DotCraft Doctor',
      interface: {
        ...localPlugin.interface,
        displayName: 'DotCraft Doctor',
        defaultPrompt: 'Please diagnose this failure.'
      },
      skills: [
        { name: 'context-handoff', description: 'Export context.', enabled: true },
        { name: 'dotcraft-doctor', description: 'Route troubleshooting.', enabled: true },
        { name: 'error-diagnosis', description: 'Diagnose failures.', enabled: true }
      ]
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [doctorPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: doctorPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('DotCraft Doctor'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('$dotcraft-doctor Please diagnose this failure.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'skill', skillName: 'dotcraft-doctor' }
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

  it('does not choose an arbitrary skill for a multi-skill plugin without an entry skill', async () => {
    const multiSkillPlugin: PluginEntry = {
      ...localPlugin,
      id: 'review-tools',
      displayName: 'Review Tools',
      interface: {
        ...localPlugin.interface,
        displayName: 'Review Tools',
        defaultPrompt: 'Please help with this review.'
      },
      skills: [
        { name: 'review-code', description: 'Review code.', enabled: true },
        { name: 'review-docs', description: 'Review documentation.', enabled: true }
      ]
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [multiSkillPlugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin: multiSkillPlugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText('Review Tools'))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    expect(useUIStore.getState().welcomeDraft?.text).toBe('Please help with this review.')
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([])
  })
})
