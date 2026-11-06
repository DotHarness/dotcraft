import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'
import type { PanelCheckpoint, PanelScope } from '../shared/viewer/panelState'
import { panelScopeKey } from '../shared/viewer/panelState'
import { normalizePanelCheckpoint } from '../shared/viewer/panelCheckpoint'

interface PanelStateFile {
  version: 1
  panels: Record<string, PanelCheckpoint>
}

export class ThreadPanelStateStore {
  private readonly panels = new Map<string, PanelCheckpoint>()
  private readonly deletedScopes = new Set<string>()

  constructor(private readonly filePath: string) {
    try {
      const data: unknown = JSON.parse(readFileSync(filePath, 'utf8'))
      if (!data || typeof data !== 'object' || !('version' in data) || data.version !== 1 ||
          !('panels' in data) || !data.panels || typeof data.panels !== 'object' || Array.isArray(data.panels)) return
      for (const [key, value] of Object.entries(data.panels)) {
        const panel = normalizePanelCheckpoint(value)
        if (panel) this.panels.set(key, panel)
      }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'ENOENT') console.warn('[desktop] panel checkpoints could not be read')
    }
  }

  read(scope: PanelScope): PanelCheckpoint | null {
    const key = panelScopeKey(scope)
    if (this.deletedScopes.has(key)) return null
    const value = this.panels.get(key)
    return value ? structuredClone(value) : null
  }

  removeThread(scope: PanelScope): void {
    this.deletedScopes.add(panelScopeKey(scope))
    this.update(scope, null)
  }

  update(scope: PanelScope, value: unknown): void {
    const key = panelScopeKey(scope)
    if (value !== null && this.deletedScopes.has(key)) throw new Error('Panel thread was deleted.')
    const previous = this.panels.get(key)
    if (value === null) {
      if (!this.panels.has(key)) return
      this.panels.delete(key)
    } else {
      const checkpoint = normalizePanelCheckpoint(value)
      if (!checkpoint) throw new Error('Invalid panel checkpoint.')
      this.panels.set(key, checkpoint)
    }
    try {
      this.persist()
    } catch (error) {
      if (previous) this.panels.set(key, previous)
      else this.panels.delete(key)
      throw error
    }
  }

  private persist(): void {
    const data: PanelStateFile = { version: 1, panels: Object.fromEntries(this.panels) }
    mkdirSync(dirname(this.filePath), { recursive: true })
    const temporary = `${this.filePath}.tmp`
    writeFileSync(temporary, JSON.stringify(data), 'utf8')
    renameSync(temporary, this.filePath)
  }
}
