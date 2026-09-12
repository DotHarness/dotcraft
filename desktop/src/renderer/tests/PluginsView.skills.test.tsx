import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useSkillsStore, type SkillEntry } from '../stores/skillsStore'
import { useToastStore } from '../stores/toastStore'
import {
  appServerSendRequest, localPlugin, renderPluginsView, setupPluginsViewTest
} from './pluginsViewTestFixtures'

const icon = 'data:image/svg+xml;base64,PHN2ZyAvPg=='
const plugin: PluginEntry = {
  ...localPlugin,
  skills: [
    { name: 'review', displayName: 'Review', description: 'Review a change.', enabled: true, iconSmallDataUrl: icon },
    { name: 'summarize', displayName: 'Summarize', description: 'Summarize a change.', enabled: false }
  ]
}
const runtimeSkills: SkillEntry[] = plugin.skills.map((skill) => ({
  ...skill, source: 'plugin', pluginId: plugin.id, available: true, path: `/plugins/review/skills/${skill.name}/SKILL.md`
}))

function respondWith(currentPlugin = plugin, skills = runtimeSkills): void {
  appServerSendRequest.mockImplementation(async (method: string) => {
    if (method === 'plugin/list') return { plugins: [currentPlugin], snapshotRevision: 1 }
    if (method === 'plugin/view') return { plugin: currentPlugin, snapshotRevision: 1 }
    if (method === 'skills/list') return { skills }
    if (method === 'plugin/skill/read') return { content: '---\nname: review\n---\n# Package source' }
    if (method === 'skills/view') return { content: '# Effective skill' }
    return {}
  })
}

async function openDetails(): Promise<void> {
  renderPluginsView()
  fireEvent.click(await screen.findByText('External Process Echo'))
  await screen.findByRole('heading', { name: 'Skills 2' })
}

describe('Plugin detail Skills', () => {
  beforeEach(setupPluginsViewTest)

  it('shows declared icons in a separate section and previews the effective skill', async () => {
    respondWith()
    await openDetails()
    const section = screen.getByRole('region', { name: 'Skills 2' })
    expect(section.querySelector('img')).toHaveAttribute('src', icon)
    expect(within(section).queryByText('Skill')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Included content' })).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Toggle Review skill' })).toBeEnabled())
    fireEvent.click(within(section).getByRole('button', { name: /Review a change/ }))
    expect(await screen.findByRole('heading', { name: 'Effective skill' })).toBeInTheDocument()
    expect(within(screen.getByRole('dialog')).queryByRole('switch')).not.toBeInTheDocument()
  })

  it('locks only the saving skill, uses the returned state, and shares changes with management', async () => {
    respondWith()
    await openDetails()
    const toggle = screen.getByRole('switch', { name: 'Toggle Review skill' })
    await waitFor(() => expect(toggle).toBeEnabled())
    let finish!: (result: unknown) => void
    appServerSendRequest.mockImplementationOnce(() => new Promise((resolve) => { finish = resolve }))
    fireEvent.click(toggle)
    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('switch', { name: 'Toggle Summarize skill' })).toBeEnabled()
    fireEvent.click(toggle)
    expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'skills/setEnabled')).toHaveLength(1)
    expect(appServerSendRequest).toHaveBeenCalledWith('skills/setEnabled', { name: 'review', enabled: false })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await act(async () => { finish({ skill: { ...runtimeSkills[0], enabled: false } }) })
    expect(toggle).toBeEnabled()
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    expect(useSkillsStore.getState().skills[0].enabled).toBe(false)
    await act(async () => {
      appServerSendRequest.mockResolvedValueOnce({ skill: { ...runtimeSkills[0], enabled: true } })
      await useSkillsStore.getState().toggleSkillEnabled('review', true)
    })
    expect(toggle).toHaveAttribute('aria-checked', 'true')
  })

  it('retains the previous state and reports a failed save', async () => {
    respondWith()
    await openDetails()
    const toggle = screen.getByRole('switch', { name: 'Toggle Review skill' })
    await waitFor(() => expect(toggle).toBeEnabled())
    appServerSendRequest.mockRejectedValueOnce(new Error('Save failed'))
    fireEvent.click(toggle)
    await waitFor(() => expect(toggle).toBeEnabled())
    expect(toggle).toHaveAttribute('aria-checked', 'true')
    expect(useToastStore.getState().toasts).toHaveLength(1)
  })

  it('disables child switches with the parent and restores saved choices', async () => {
    respondWith()
    await openDetails()
    const toggle = screen.getByRole('switch', { name: 'Toggle Review skill' })
    await waitFor(() => expect(toggle).toBeEnabled())
    act(() => usePluginStore.setState({ selectedPlugin: { ...plugin, enabled: false } }))
    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    act(() => usePluginStore.setState({ selectedPlugin: plugin }))
    await waitFor(() => expect(toggle).toBeEnabled())
    expect(toggle).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('switch', { name: 'Toggle Summarize skill' })).toHaveAttribute('aria-checked', 'false')
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'skills/setEnabled')).toBe(false)
  })

  it.each(['workspace', 'another-plugin'])('does not control a same-named skill from %s', async (source) => {
    respondWith(plugin, [{ ...runtimeSkills[0], source: source === 'workspace' ? 'workspace' : 'plugin', pluginId: source }])
    await openDetails()
    const toggle = screen.getByRole('switch', { name: 'Toggle Review skill' })
    act(() => useSkillsStore.setState({ pendingSkillNames: ['review'] }))
    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('aria-busy', 'false')
    fireEvent.click(screen.getByRole('button', { name: /Review a change/ }))
    expect(await screen.findByRole('heading', { name: 'Package source' })).toBeInTheDocument()
    expect(appServerSendRequest).toHaveBeenCalledWith('plugin/skill/read', { id: plugin.id, name: 'review' })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'skills/view' || method === 'skills/setEnabled')).toBe(false)
  })

  it('shows icons and read-only previews without switches before installation', async () => {
    respondWith({ ...plugin, installed: false, enabled: false })
    await openDetails()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Review a change/ }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog.querySelector('img')).toHaveAttribute('src', icon)
    expect(await within(dialog).findByRole('heading', { name: 'Package source' })).toBeInTheDocument()
    expect(within(dialog).queryByText('name: review')).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Try in chat' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
    expect(appServerSendRequest).toHaveBeenCalledWith('plugin/skill/read', { id: plugin.id, name: 'review' })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'skills/list' || method === 'skills/view')).toBe(false)
  })
})
