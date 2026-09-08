import type { AutomationDefinition, AutomationRun, AutomationInput, AutomationPreset } from '../types/automation'
import { create } from 'zustand'
export type { AutomationDefinition, AutomationRun, AutomationInput, AutomationPreset, AutomationSchedule } from '../types/automation'

interface AutomationsState {
  automations: AutomationDefinition[]
  runs: Record<string, AutomationRun[]>
  presets: AutomationPreset[]
  loading: boolean
  error: string | null
  selectedAutomationId: string | null
  fetchAutomations(): Promise<void>
  readAutomation(id: string): Promise<AutomationDefinition>
  fetchRuns(id: string): Promise<void>
  fetchPresets(locale: string): Promise<void>
  save(input: AutomationInput, existing?: AutomationDefinition): Promise<AutomationDefinition>
  remove(id: string): Promise<void>
  run(id: string): Promise<AutomationRun>
  selectAutomation(id: string | null): void
  upsertAutomation(automation: AutomationDefinition): void
  removeAutomation(id: string): void
  upsertRun(run: AutomationRun): void
}
const request = (method: string, params: unknown) => window.api.appServer.sendRequest(method as Parameters<typeof window.api.appServer.sendRequest>[0], params as never)
export const useAutomationsStore = create<AutomationsState>((set, get) => ({
  automations: [], runs: {}, presets: [], loading: false, error: null, selectedAutomationId: null,
  async fetchAutomations() {
    set({ loading: true, error: null })
    try {
      const result = await request('automation/list', {}) as { automations: AutomationDefinition[] }
      set({ automations: result.automations, loading: false })
    } catch (error) { set({ loading: false, error: String(error) }) }
  },
  async readAutomation(automationId) {
    try {
      const { automation } = await request('automation/read', { automationId }) as { automation: AutomationDefinition }
      get().upsertAutomation(automation)
      return automation
    } catch (error) {
      const failure = error as { code?: number; data?: { code?: string } }
      if (failure?.code === -32051 || failure?.data?.code === 'automation.notFound' || /automation\.notFound|automation not found/i.test(String(error))) get().removeAutomation(automationId)
      throw error
    }
  },
  async fetchRuns(automationId) {
    const { runs } = await request('automation/runs/list', { automationId }) as { runs: AutomationRun[] }
    set(state => ({ runs: { ...state.runs, [automationId]: runs } }))
  },
  async fetchPresets(locale) {
    const { presets } = await request('automation/presets/list', { locale }) as { presets: AutomationPreset[] }
    set({ presets })
  },
  async save(input, existing) {
    const automation = editableAutomation(input)
    const result = await request(existing ? 'automation/update' : 'automation/create', existing
      ? { automationId: existing.id, expectedVersion: existing.version, automation }
      : { automation }) as { automation: AutomationDefinition }
    get().upsertAutomation(result.automation)
    return result.automation
  },
  async remove(automationId) {
    await request('automation/delete', { automationId })
    get().removeAutomation(automationId)
  },
  async run(automationId) {
    const { run } = await request('automation/run', { automationId }) as { run: AutomationRun }
    get().upsertRun(run)
    return run
  },
  selectAutomation(selectedAutomationId) {
    set({ selectedAutomationId, error: null })
    if (selectedAutomationId) void get().readAutomation(selectedAutomationId).catch(error => set({ error: String(error) }))
  },
  upsertAutomation(automation) {
    set(state => ({ automations: [automation, ...state.automations.filter(a => a.id !== automation.id)] }))
  },
  removeAutomation(id) {
    set(state => ({ automations: state.automations.filter(a => a.id !== id) }))
  },
  upsertRun(run) {
    set(state => ({ runs: { ...state.runs, [run.automationId]: [run, ...(state.runs[run.automationId] ?? []).filter(r => r.id !== run.id)] } }))
  }
}))

export function editableAutomation(value: AutomationInput): AutomationInput {
  return { name: value.name, prompt: value.prompt, status: value.status, executionMode: value.executionMode,
    targetThreadId: value.executionMode === 'thread' ? value.targetThreadId : null,
    workspaceMode: value.workspaceMode, agentProfileId: value.executionMode === 'independent' ? value.agentProfileId : null,
    approvalPolicy: value.approvalPolicy, schedule: value.schedule, notificationPolicy: value.notificationPolicy }
}
