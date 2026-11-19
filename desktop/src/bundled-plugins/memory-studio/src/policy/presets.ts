import type { ConfigEdit } from '@dotcraft/sdk/contracts'
import { POLICY_SETTINGS, policyEquals, settingEdit, settingEquals, type PolicyLayer, type PolicySetting, type PolicyValues } from './policy'

export type PresetId = 'dotcraft' | 'profile-and-notes' | 'atomic-facts'

/** A preset's own settings. The extraction interval always stays at the default. */
type PresetSettings = Omit<PolicyValues, 'extractionInterval'>

export interface Preset {
  id: PresetId
  /** `null` means Core's defaults: applying the preset removes the values. */
  settings: PresetSettings | null
}

const PROFILE_AND_NOTES: PresetSettings = {
  types: [
    { name: 'user', description: 'who the user is: role, expertise, working style, and stable preferences.' },
    { name: 'note', description: 'a short working note for later conversations: a decision, constraint, or pointer the repository does not record.' }
  ],
  saveGuidance: `Save sparingly. Memory is a small, curated profile of the user and a handful of short working notes, not a log of what happened.

- Save a user memory when you learn something durable about who the user is or how they like to work: their role, expertise, tools, or a preference they state or confirm.
- Save a note only for a decision, constraint, or pointer that will matter in later conversations and that the repository or AGENTS.md does not already record. Do not note what matters only to the current task, what you can find again by reading the workspace, or a summary of the conversation.
- Keep each memory to one or two sentences. If it needs more, it is probably not a memory.
- Update an existing memory rather than adding a new one, and delete a memory as soon as it proves wrong or stale. When unsure whether something lasts, do not save it.`,
  extractionGuidance: `Save only what clearly belongs in the user profile or in a working note and was not saved while responding: a preference the user stated or confirmed, a fact about their role or expertise, or a decision later conversations will depend on. Fold it into an existing memory when one covers the topic, and keep each memory to one or two sentences. Most reviews should save nothing.`,
  consolidationGuidance: `Compact the store toward a few dozen entries, each worth reading at the start of a conversation.

- Merge memories that cover the same topic into one, keeping each to one or two sentences.
- Fold user memories that describe the same aspect of the user into one entry per aspect.
- Delete notes whose decision is done, superseded, or now recorded in the repository or AGENTS.md, and notes that only mattered to one task.
- Convert relative dates to absolute dates, and delete facts that current evidence contradicts.

AGENTS.md is the maintained source. When a memory contradicts it on the same topic, delete the memory or rewrite it to agree. Keep it only when it is clearly newer and explicitly corrects AGENTS.md, and then add "contradicts AGENTS.md — verify which is current" and list it in your summary.`
}

const ATOMIC_FACTS: PresetSettings = {
  types: [
    { name: 'preference', description: 'how the user wants something done: a choice, habit, or correction stated as a standing rule.' },
    { name: 'fact', description: 'a durable fact about the user, their work, or their environment that the repository does not record.' },
    { name: 'event', description: 'something that happened or is scheduled, with its absolute date.' }
  ],
  saveGuidance: `Keep memory as atomic facts: one self-contained fact per file, readable without the conversation that produced it or any other memory.

- Write the fact as one plain statement that names its subject, such as "The user squash-merges feature branches." Avoid pronouns that need context, and never bundle several facts into one file. The description repeats the fact, shortened if needed.
- Save a preference when the user states, corrects, or confirms how they want something done. Save a fact when you learn something durable about the user, their work, or their environment that the repository or AGENTS.md does not record. Save an event when something happened or is scheduled that later conversations may need, and always give it an absolute date (YYYY-MM-DD).
- Before saving, look for a memory that states the same fact. Update that file when the new information refines or corrects it. Otherwise add a new file.
- When a fact proves wrong, delete it rather than adding one that contradicts it. Do not save what matters only to the current task, and when unsure whether a fact lasts, do not save it.`,
  extractionGuidance: `Turn each preference, durable fact, or event that the reviewed turns stated or confirmed but that was not saved while responding into its own file. Add a new fact as a new file. Update an existing file only when it states the same fact and the turns refine or correct it, and never fold a different fact into it. Give every event an absolute date.`,
  consolidationGuidance: `Keep the store a set of atomic facts, one per file.

- Resolve duplicates: when several files state the same fact, keep the clearest and delete the rest.
- Resolve contradictions: when two facts conflict, keep the one newer evidence supports and delete the other. When the evidence cannot decide, keep both and name the conflict in your summary.
- Split a file that holds several facts into one file per fact, and rewrite a fact that depends on context so it reads on its own.
- Give every event an absolute date, converting relative dates from the evidence, and delete events that no longer matter to future conversations.
- Delete facts that current evidence contradicts.

AGENTS.md is the maintained source. When a preference or fact contradicts it on the same topic, delete the memory or rewrite it to agree. Keep it only when it is clearly newer and explicitly corrects AGENTS.md, and then add "contradicts AGENTS.md — verify which is current" and list it in your summary.`
}

export const PRESETS: readonly Preset[] = [
  { id: 'dotcraft', settings: null },
  { id: 'profile-and-notes', settings: PROFILE_AND_NOTES },
  { id: 'atomic-facts', settings: ATOMIC_FACTS }
]

export function presetValues(preset: Preset, defaults: PolicyValues): PolicyValues {
  return preset.settings ? { ...preset.settings, extractionInterval: defaults.extractionInterval } : defaults
}

/** The preset the effective policy is, or `null` when it is customized. */
export function matchingPreset(effective: PolicyValues, defaults: PolicyValues): PresetId | null {
  return PRESETS.find((preset) => policyEquals(effective, presetValues(preset, defaults)))?.id ?? null
}

/** One configuration write for the chosen layer: every setting, with values a default would equal removed. */
export function presetEdits(preset: Preset): ConfigEdit[] {
  return POLICY_SETTINGS.map((setting) => {
    if (!preset.settings || setting === 'extractionInterval') return settingEdit(setting, null)
    return settingEdit(setting, preset.settings[setting])
  })
}

/** Whether applying the preset would replace values the layer already holds. */
export function replacesLayerValues(preset: Preset, layer: PolicyLayer | undefined, defaults: PolicyValues): boolean {
  if (!layer) return false
  const target = presetValues(preset, defaults)
  return POLICY_SETTINGS.some((setting) => {
    const current = layer.values[setting]
    if (current === undefined) return false
    if (!preset.settings || setting === 'extractionInterval') return true
    return !settingEquals<PolicySetting>(setting, current, target[setting])
  })
}
