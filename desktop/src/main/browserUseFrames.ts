export interface BrowserFramePoint {
  x: number
  y: number
}

export interface BrowserFrameBox extends BrowserFramePoint {
  width: number
  height: number
}

export interface BrowserFrameContext {
  sessionId?: string
  frameId?: string
}

interface BrowserFrameHop {
  frameId: string
  parent: BrowserFrameContext
  child: BrowserFrameContext
}

export interface BrowserFrameTarget extends BrowserFrameContext {
  hops: BrowserFrameHop[]
}

export interface BrowserFrameMapping {
  point<T extends BrowserFramePoint>(point: T): T
  box<T extends BrowserFrameBox>(box: T): T
}

interface BrowserFrameNode {
  target: BrowserFrameTarget
  documentKey: string
  localRef: string
}

export type BrowserFrameSend = (method: string, params: Record<string, unknown>, sessionId?: string) => Promise<unknown>

export const TOP_FRAME_TARGET: BrowserFrameTarget = { hops: [] }

export const FRAME_AUTO_ATTACH = {
  autoAttach: true,
  flatten: true,
  waitForDebuggerOnStart: false,
  filter: [{ type: 'iframe', exclude: false }]
}

const WORLD_NAME = 'dotcraft-browser-use'
const MAX_REMEMBERED_NODE_IDS = 5000

const IDENTITY_MAPPING: BrowserFrameMapping = { point: point => point, box: box => box }

function mapThroughQuad(quad: number[], size: { width: number; height: number }, point: BrowserFramePoint): BrowserFramePoint {
  const width = size.width > 0 ? size.width : quad[2] - quad[0]
  const height = size.height > 0 ? size.height : quad[7] - quad[1]
  const s = width ? point.x / width : 0
  const a = height ? point.y / height : 0
  return {
    x: quad[0] + s * (quad[2] - quad[0]) + a * (quad[6] - quad[0]),
    y: quad[1] + s * (quad[3] - quad[1]) + a * (quad[7] - quad[1])
  }
}

export class BrowserFrameSessions {
  private readonly sessionsByFrame = new Map<string, string>()
  private readonly worlds = new Map<string, number>()
  private mainDocumentKey = ''
  private nextNodeId = 1
  private readonly nodeIds = new Map<string, string>()
  private readonly nodes = new Map<string, BrowserFrameNode>()

  handleEvent(send: BrowserFrameSend, method: string, params: Record<string, unknown>): void {
    if (method === 'Target.attachedToTarget') {
      const info = params.targetInfo as { type?: unknown; targetId?: unknown } | undefined
      const sessionId = params.sessionId
      if (info?.type !== 'iframe' || typeof info.targetId !== 'string' || typeof sessionId !== 'string') return
      if (this.sessionsByFrame.has(info.targetId)) return
      this.sessionsByFrame.set(info.targetId, sessionId)
      send('Target.setAutoAttach', FRAME_AUTO_ATTACH, sessionId).catch(() => {})
    } else if (method === 'Target.detachedFromTarget') {
      for (const [frameId, sessionId] of this.sessionsByFrame) {
        if (sessionId === params.sessionId) this.sessionsByFrame.delete(frameId)
      }
      for (const key of this.worlds.keys()) {
        if (key.startsWith(`${params.sessionId}:`)) this.worlds.delete(key)
      }
    }
  }

  clear(): void {
    this.sessionsByFrame.clear()
    this.worlds.clear()
    this.nodes.clear()
  }

  async childTarget(send: BrowserFrameSend, parent: BrowserFrameTarget, frameObjectId: string): Promise<BrowserFrameTarget> {
    const described = await send('DOM.describeNode', { objectId: frameObjectId }, parent.sessionId) as { node?: { frameId?: string } }
    const frameId = described.node?.frameId
    if (!frameId) throw new Error('Frame element has no frame.')
    const frameSessionId = this.sessionsByFrame.get(frameId)
    const child: BrowserFrameContext = frameSessionId ? { sessionId: frameSessionId } : { sessionId: parent.sessionId, frameId }
    const hop: BrowserFrameHop = { frameId, parent: { sessionId: parent.sessionId, frameId: parent.frameId }, child }
    const last = parent.hops.at(-1)
    const hops = last && last.parent.sessionId === parent.sessionId ? [...parent.hops.slice(0, -1), hop] : [...parent.hops, hop]
    return { ...child, hops }
  }

  async mapping(send: BrowserFrameSend, target: BrowserFrameTarget): Promise<BrowserFrameMapping> {
    if (!target.hops.length) return IDENTITY_MAPPING
    const layers = await Promise.all(target.hops.map(async hop => {
      const owner = await send('DOM.getFrameOwner', { frameId: hop.frameId }, hop.parent.sessionId) as { backendNodeId: number }
      const [box, size] = await Promise.all([
        send('DOM.getBoxModel', { backendNodeId: owner.backendNodeId }, hop.parent.sessionId) as Promise<{ model: { content: number[] } }>,
        this.evaluate(send, hop.child, { expression: '({ width: innerWidth, height: innerHeight })', returnByValue: true }) as
          Promise<{ result?: { value?: { width: number; height: number } } }>
      ])
      return { quad: box.model.content, size: size.result?.value ?? { width: 0, height: 0 } }
    }))
    const point = <T extends BrowserFramePoint>(input: T): T => {
      let mapped: BrowserFramePoint = input
      for (let index = layers.length - 1; index >= 0; index--) mapped = mapThroughQuad(layers[index].quad, layers[index].size, mapped)
      return { ...input, x: mapped.x, y: mapped.y }
    }
    return {
      point,
      box: <T extends BrowserFrameBox>(input: T): T => {
        const corners = [
          point({ x: input.x, y: input.y }), point({ x: input.x + input.width, y: input.y }),
          point({ x: input.x, y: input.y + input.height }), point({ x: input.x + input.width, y: input.y + input.height })
        ]
        const xs = corners.map(corner => corner.x)
        const ys = corners.map(corner => corner.y)
        const x = Math.min(...xs)
        const y = Math.min(...ys)
        return { ...input, x, y, width: Math.max(...xs) - x, height: Math.max(...ys) - y }
      }
    }
  }

  async documentKey(send: BrowserFrameSend, context: BrowserFrameContext): Promise<string> {
    type FrameTree = { frame?: { id?: string; loaderId?: string }; childFrames?: FrameTree[] }
    const { frameTree } = await send('Page.getFrameTree', {}, context.sessionId) as { frameTree?: FrameTree }
    const find = (tree: FrameTree | undefined): FrameTree['frame'] => {
      if (!tree?.frame) return undefined
      if (!context.frameId || tree.frame.id === context.frameId) return tree.frame
      for (const child of tree.childFrames ?? []) {
        const found = find(child)
        if (found) return found
      }
      return undefined
    }
    const frame = find(frameTree)
    return `${frame?.id ?? context.frameId ?? ''}:${frame?.loaderId ?? ''}`
  }

  beginSnapshot(mainDocumentKey: string): void {
    if (mainDocumentKey !== this.mainDocumentKey || this.nodeIds.size > MAX_REMEMBERED_NODE_IDS) this.nodeIds.clear()
    this.mainDocumentKey = mainDocumentKey
    this.nodes.clear()
  }

  nodeId(target: BrowserFrameTarget, documentKey: string, localRef: string): string {
    const key = `${documentKey}:${localRef}`
    let id = this.nodeIds.get(key)
    if (!id) {
      id = String(this.nextNodeId++)
      this.nodeIds.set(key, id)
    }
    this.nodes.set(id, { target, documentKey, localRef })
    return id
  }

  node(id: string): BrowserFrameNode | undefined {
    return this.nodes.get(id)
  }

  async evaluate(send: BrowserFrameSend, target: BrowserFrameContext, params: Record<string, unknown>): Promise<unknown> {
    if (!target.frameId) return await send('Runtime.evaluate', params, target.sessionId)
    const key = `${target.sessionId ?? ''}:${target.frameId}`
    for (let attempt = 0; ; attempt++) {
      let contextId = this.worlds.get(key)
      if (contextId == null) {
        const world = await send('Page.createIsolatedWorld', {
          frameId: target.frameId,
          worldName: WORLD_NAME,
          grantUniveralAccess: false
        }, target.sessionId) as { executionContextId: number }
        contextId = world.executionContextId
        this.worlds.set(key, contextId)
      }
      try {
        return await send('Runtime.evaluate', { ...params, contextId }, target.sessionId)
      } catch (error) {
        this.worlds.delete(key)
        if (attempt > 0 || !/context/i.test(error instanceof Error ? error.message : String(error))) throw error
      }
    }
  }
}
