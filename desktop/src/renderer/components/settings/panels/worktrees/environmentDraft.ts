import {
  PROJECT_ACTION_PLATFORMS,
  type EnvironmentDocument,
  type EnvironmentScript,
  type ProjectActionDraft,
  type ProjectActionIcon,
  type ProjectActionPlatform
} from '../../../../../shared/projectActions'

export const SCRIPT_TABS = ['default', 'win32', 'darwin', 'linux'] as const
export type ScriptTab = (typeof SCRIPT_TABS)[number]
export type ScriptDraft = Record<ScriptTab, string>

export interface ActionDraft {
  key: string
  name: string
  icon: ProjectActionIcon
  command: string
  platform: ProjectActionPlatform | null
}

export interface EnvironmentDraft {
  name: string
  setup: ScriptDraft
  cleanup: ScriptDraft
  actions: ActionDraft[]
}

export type SaveBlockReason = 'saving' | 'unreadable' | 'name' | 'action' | 'unchanged'

let nextActionKey = 0

function actionKey(): string {
  nextActionKey += 1
  return `action-${nextActionKey}`
}

export function newActionDraft(): ActionDraft {
  return { key: actionKey(), name: '', icon: 'run', command: '', platform: null }
}

function scriptDraft(script: EnvironmentScript | undefined): ScriptDraft {
  return {
    default: script?.default ?? '',
    win32: script?.win32 ?? '',
    darwin: script?.darwin ?? '',
    linux: script?.linux ?? ''
  }
}

export function draftFromDocument(document: EnvironmentDocument | null, fallbackName: string): EnvironmentDraft {
  return {
    name: document?.name.trim() || fallbackName,
    setup: scriptDraft(document?.setup),
    cleanup: scriptDraft(document?.cleanup),
    actions: (document?.actions ?? []).map((action) => ({
      key: actionKey(),
      name: action.name,
      icon: action.icon,
      command: action.command,
      platform: action.platform ?? null
    }))
  }
}

function scriptDocument(script: ScriptDraft): EnvironmentScript {
  const result: EnvironmentScript = { default: script.default.trim() }
  for (const platform of PROJECT_ACTION_PLATFORMS) {
    const value = script[platform].trim()
    if (value) result[platform] = value
  }
  return result
}

export function documentFromDraft(draft: EnvironmentDraft): EnvironmentDocument {
  return {
    name: draft.name.trim(),
    setup: scriptDocument(draft.setup),
    cleanup: scriptDocument(draft.cleanup),
    actions: draft.actions
      .filter((action) => action.name.trim() !== '' || action.command.trim() !== '')
      .map((action): ProjectActionDraft => ({
        name: action.name.trim(),
        icon: action.icon,
        command: action.command.trim(),
        ...(action.platform ? { platform: action.platform } : {})
      }))
  }
}

export function missingActionFields(action: ActionDraft): { name: boolean; command: boolean } {
  const hasName = action.name.trim() !== ''
  const hasCommand = action.command.trim() !== ''
  return { name: !hasName && hasCommand, command: hasName && !hasCommand }
}

export function isDraftDirty(draft: EnvironmentDraft, baseline: EnvironmentDraft, alwaysChanged: boolean): boolean {
  if (alwaysChanged) return true
  return JSON.stringify(documentFromDraft(draft)) !== JSON.stringify(documentFromDraft(baseline))
}

export function saveBlockReason(state: {
  saving: boolean
  readable: boolean
  draft: EnvironmentDraft
  dirty: boolean
}): SaveBlockReason | null {
  if (state.saving) return 'saving'
  if (!state.readable) return 'unreadable'
  if (state.draft.name.trim() === '') return 'name'
  if (state.draft.actions.some((action) => {
    const missing = missingActionFields(action)
    return missing.name || missing.command
  })) return 'action'
  if (!state.dirty) return 'unchanged'
  return null
}

export function scriptOverrides(script: EnvironmentScript): ProjectActionPlatform[] {
  return PROJECT_ACTION_PLATFORMS.filter((platform) => (script[platform] ?? '').trim() !== '')
}
