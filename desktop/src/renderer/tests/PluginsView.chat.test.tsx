import { fireEvent, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import type { PluginEntry } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'
import {
  appServerSendRequest,
  localPlugin,
  renderPluginsView,
  setupPluginsViewTest
} from './pluginsViewTestFixtures'

const pluginId = 'sample-plugin'
const prompt = 'Handle this request.'
const skill = (name: string, enabled = true) => ({ name, description: name, enabled })

describe('PluginsView Try in chat', () => {
  beforeEach(setupPluginsViewTest)

  it.each([
    { caseName: 'no skills', skills: [], expectedSkill: null },
    {
      caseName: 'a matching entry skill among multiple skills',
      skills: [skill(pluginId), skill('other-skill')],
      expectedSkill: pluginId
    },
    {
      caseName: 'one enabled skill and a disabled entry skill',
      skills: [skill(pluginId, false), skill('specific-skill')],
      expectedSkill: 'specific-skill'
    },
    {
      caseName: 'multiple skills without an entry skill',
      skills: [skill('first-skill'), skill('second-skill')],
      expectedSkill: null
    }
  ])('stages a draft for $caseName', async ({ skills, expectedSkill }) => {
    const plugin: PluginEntry = {
      ...localPlugin,
      id: pluginId,
      displayName: 'Sample Plugin',
      interface: { ...localPlugin.interface, displayName: 'Sample Plugin', defaultPrompt: prompt },
      skills
    }
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'plugin/list') return { plugins: [plugin], diagnostics: [], snapshotRevision: 1 }
      if (method === 'plugin/view') return { plugin, snapshotRevision: 1 }
      return {}
    })

    renderPluginsView()

    fireEvent.click(await screen.findByText(plugin.displayName))
    fireEvent.click(await screen.findByRole('button', { name: 'Try in chat' }))

    const draft = useUIStore.getState().welcomeDraft
    expect(draft?.text).toContain(prompt)
    expect(draft?.segments).toEqual(expectedSkill
      ? [{ type: 'skill', skillName: expectedSkill }]
      : [])
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })
})
