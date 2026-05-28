export type ReloadBehavior = 'hot' | 'subsystemRestart' | 'processRestart'

export interface FieldDescriptor {
  sectionPath?: string[]
  rootKey?: string
  key: string
  reload: ReloadBehavior | string
  subsystemKey?: string
}

export interface AffordanceInput {
  field: FieldDescriptor
}

export type Affordance =
  | { kind: 'live' }
  | { kind: 'subsystemRestart'; subsystemKey: string }
  | { kind: 'processRestart' }

export function getConfigReloadAffordance(input: AffordanceInput): Affordance {
  const { field } = input

  if (field.reload === 'hot') {
    return { kind: 'live' }
  }

  if (field.reload === 'subsystemRestart') {
    const subsystemKey = field.subsystemKey?.trim()
    if (subsystemKey) {
      return { kind: 'subsystemRestart', subsystemKey }
    }
    return { kind: 'processRestart' }
  }

  return { kind: 'processRestart' }
}
