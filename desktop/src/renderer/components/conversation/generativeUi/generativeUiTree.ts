import {
  GENERATIVE_UI_LIMITS,
  generativeUiJsonBytes,
  isGenerativeUiJson,
  type GenerativeUiElementNode,
  type GenerativeUiFailureStage,
  type GenerativeUiJson,
  type GenerativeUiNode
} from './generativeUiProtocol'

export type GenerativeUiTreeResult =
  | { ok: true; tree: GenerativeUiNode[] }
  | { ok: false; stage: GenerativeUiFailureStage; message: string }

const COMPONENT_TYPE = /^[A-Z][A-Za-z0-9]{0,63}$/
const NODE_KEYS = new Set(['type', 'key', 'props', 'children'])

class TreeViolation extends Error {
  constructor(readonly stage: GenerativeUiFailureStage, message: string) {
    super(message)
  }
}

export function validateGenerativeUiTree(value: unknown): GenerativeUiTreeResult {
  if (generativeUiJsonBytes(value) > GENERATIVE_UI_LIMITS.maxSnapshotBytes) {
    return { ok: false, stage: 'payload_too_large', message: `The snapshot is larger than ${GENERATIVE_UI_LIMITS.maxSnapshotBytes / 1024} KB.` }
  }
  if (!Array.isArray(value)) return { ok: false, stage: 'invalid_payload', message: 'The snapshot tree must be a list of nodes.' }
  const counter = { nodes: 0 }
  try {
    return { ok: true, tree: value.map(node => readNode(node, 1, counter)) }
  } catch (error) {
    const violation = error instanceof TreeViolation ? error : new TreeViolation('invalid_payload', String(error))
    return { ok: false, stage: violation.stage, message: violation.message }
  }
}

function readNode(value: unknown, depth: number, counter: { nodes: number }): GenerativeUiNode {
  if (typeof value === 'string') return value
  if (!isPlainObject(value)) throw new TreeViolation('invalid_payload', 'A node must be text or a component.')
  if (Object.keys(value).some(key => !NODE_KEYS.has(key))) throw new TreeViolation('invalid_payload', 'A node has unexpected fields.')
  if (depth > GENERATIVE_UI_LIMITS.maxDepth) {
    throw new TreeViolation('payload_too_large', `The tree is deeper than ${GENERATIVE_UI_LIMITS.maxDepth} levels.`)
  }
  if (++counter.nodes > GENERATIVE_UI_LIMITS.maxNodes) {
    throw new TreeViolation('payload_too_large', `The tree has more than ${GENERATIVE_UI_LIMITS.maxNodes} components.`)
  }
  const { type, key, props, children } = value
  if (typeof type !== 'string' || !COMPONENT_TYPE.test(type)) throw new TreeViolation('invalid_payload', 'A component name is invalid.')
  if (key !== undefined && (typeof key !== 'string' || key.length > 256)) throw new TreeViolation('invalid_payload', 'A component key is invalid.')
  if (!isPlainObject(props) || !Object.values(props).every(value => isGenerativeUiJson(value))) throw new TreeViolation('invalid_payload', `${type} has props that are not JSON values.`)
  if (!Array.isArray(children)) throw new TreeViolation('invalid_payload', `${type} has invalid children.`)
  const node: GenerativeUiElementNode = {
    type,
    props: props as Record<string, GenerativeUiJson>,
    children: children.map(child => readNode(child, depth + 1, counter))
  }
  if (key !== undefined) node.key = key
  return node
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null) return false
  const prototype = Object.getPrototypeOf(value)
  return prototype === Object.prototype || prototype === null
}
