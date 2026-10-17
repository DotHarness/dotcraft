export interface BrowserFramePoint {
  x: number
  y: number
}

export interface BrowserFrameTarget {
  sessionId?: string
  frameId?: string
  offset: BrowserFramePoint
  sessionOffset: BrowserFramePoint
}

export type BrowserFrameSend = (method: string, params: Record<string, unknown>, sessionId?: string) => Promise<unknown>

export const TOP_FRAME_TARGET: BrowserFrameTarget = { offset: { x: 0, y: 0 }, sessionOffset: { x: 0, y: 0 } }

export const FRAME_AUTO_ATTACH = {
  autoAttach: true,
  flatten: true,
  waitForDebuggerOnStart: false,
  filter: [{ type: 'iframe', exclude: false }]
}

const WORLD_NAME = 'dotcraft-browser-use'

export function offsetPoint<T extends BrowserFramePoint>(point: T, offset: BrowserFramePoint): T {
  return { ...point, x: point.x + offset.x, y: point.y + offset.y }
}

export class BrowserFrameSessions {
  private readonly sessionsByFrame = new Map<string, string>()
  private readonly worlds = new Map<string, number>()
  private refTargets = new Map<string, BrowserFrameTarget>()

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
    this.refTargets.clear()
  }

  async childTarget(send: BrowserFrameSend, parent: BrowserFrameTarget, frameObjectId: string): Promise<BrowserFrameTarget> {
    const [described, box] = await Promise.all([
      send('DOM.describeNode', { objectId: frameObjectId }, parent.sessionId),
      send('DOM.getBoxModel', { objectId: frameObjectId }, parent.sessionId)
    ]) as [{ node?: { frameId?: string } }, { model?: { content?: number[] } }]
    const frameId = described.node?.frameId
    const content = box.model?.content
    if (!frameId || !content) throw new Error('Frame element has no rendered frame.')
    const offset = { x: parent.sessionOffset.x + content[0], y: parent.sessionOffset.y + content[1] }
    const sessionId = this.sessionsByFrame.get(frameId)
    if (sessionId) return { sessionId, offset, sessionOffset: offset }
    return { sessionId: parent.sessionId, frameId, offset, sessionOffset: parent.sessionOffset }
  }

  async evaluate(send: BrowserFrameSend, target: BrowserFrameTarget, params: Record<string, unknown>): Promise<unknown> {
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

  setRefTargets(targets: Map<string, BrowserFrameTarget>): void {
    this.refTargets = targets
  }

  targetForRef(ref: string): BrowserFrameTarget {
    const prefix = /^((?:f\d+)*)s\d+e\d+$/.exec(ref)?.[1] ?? ''
    return this.refTargets.get(prefix) ?? TOP_FRAME_TARGET
  }
}
