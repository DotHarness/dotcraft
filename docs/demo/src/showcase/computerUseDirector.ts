import { computerUseCopy, type Action, type ComputerUseScript } from './content/computerUse'
import type { Lang } from './content/types'
import { actionsOfStep, findWindowCode, stepCode } from './computerUseCode'
import { Timeline, type Cue } from './playback'

export type StepState = 'running' | 'done' | 'stopped'

export interface Step {
  id: number
  text: string
  code: string
  state: StepState
  startedAt: string
  completedAt?: string
}

export type Entry =
  | { kind: 'user'; id: number; text: string }
  | { kind: 'assistant'; id: number; thinking: boolean; steps: Step[]; text: string | null; startedAt: string }

type AssistantEntry = Extract<Entry, { kind: 'assistant' }>
type AssistantPatch = Partial<Omit<AssistantEntry, 'kind' | 'id'>>

export interface Scene {
  entries: Entry[]
  /** Computer use is running: the status pill and screen glow are showing. */
  active: boolean
  gate: 'approval' | 'confirm' | null
  values: Record<string, string>
  focus: string | null
  cursor: string | null
  clicks: number
  done: boolean[]
}

const TYPE_CHUNK = 2

/** While held, the script continues only once the reader answers in the frame; `rebuild` lets an epilogue replay the whole example. */
class ScriptTimeline extends Timeline {
  private held = false
  private holdTimer: ReturnType<typeof setTimeout> | undefined

  constructor(cues: Cue[], reset: () => void, private readonly rebuild?: () => void) {
    super(cues, reset)
  }

  override play(): void {
    if (!this.held) super.play()
  }

  override restart(): void {
    this.held = false
    if (!this.rebuild) {
      super.restart()
      return
    }
    this.dispose()
    this.rebuild()
  }

  /** Called from a cue: the pause waits until the timeline has scheduled the next cue. */
  hold(): void {
    this.held = true
    this.holdTimer = setTimeout(() => this.pause())
  }

  release(): void {
    clearTimeout(this.holdTimer)
    this.held = false
    this.play()
  }

  override dispose(): void {
    clearTimeout(this.holdTimer)
    super.dispose()
  }
}

function initialScene(script: ComputerUseScript): Scene {
  return {
    entries: [],
    active: false,
    gate: null,
    values: { ...script.values },
    focus: null,
    cursor: null,
    clicks: 0,
    done: script.tasks.map(() => false)
  }
}

export class ComputerUseDirector {
  private scene: Scene
  private timeline: ScriptTimeline | null = null
  private readonly listeners = new Set<() => void>()
  private sequence = 0

  constructor(private readonly script: ComputerUseScript, private readonly lang: Lang) {
    this.scene = initialScene(script)
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  readonly getScene = (): Scene => this.scene

  current(): Timeline | null {
    return this.timeline
  }

  start(play: boolean): void {
    this.timeline?.dispose()
    this.timeline = new ScriptTimeline(this.mainCues(), () => this.reset())
    if (play) this.timeline.play()
  }

  dispose(): void {
    this.timeline?.dispose()
    this.timeline = null
  }

  /** `approval` and `confirm` wait at that question, `operate` allows the app and plays on, `stopped` stops after the first task. */
  openAt(chapter: string): void {
    this.timeline?.dispose()
    const timeline = new ScriptTimeline(this.mainCues(), () => this.reset())
    this.timeline = timeline
    const atGate = (gate: Scene['gate']) => () => this.scene.gate === gate
    if (chapter === 'confirm') {
      timeline.seek(atGate('confirm'))
      return
    }
    timeline.seek(atGate('approval'))
    if (chapter === 'operate') {
      this.decideApproval(true)
    } else if (chapter === 'stopped') {
      this.update({ gate: null })
      timeline.seek(() => this.scene.done.some(Boolean))
      this.stop()
    }
  }

  decideApproval(allowed: boolean): void {
    if (this.scene.gate !== 'approval') return
    this.update({ gate: null })
    if (allowed) {
      this.timeline?.release()
      return
    }
    this.epilogue([
      { delay: 300, run: () => this.settleStep('stopped') },
      { delay: 700, run: () => this.setReply(this.script.declined) },
      { delay: 500, run: () => this.update({ active: false, cursor: null }) }
    ])
  }

  answerConfirm(send: boolean): void {
    const confirm = this.script.confirm
    if (this.scene.gate !== 'confirm' || !confirm) return
    this.update({ gate: null })
    if (send) {
      this.timeline?.release()
      return
    }
    this.epilogue([
      { delay: 200, run: () => this.pushUser(confirm.no.message) },
      { delay: 600, run: () => this.pushAssistant(true) },
      { delay: 900, run: () => this.patchAssistant({ thinking: false, text: confirm.no.reply }) }
    ])
  }

  stop(): void {
    if (!this.scene.active || this.scene.gate) return
    this.settleStep('stopped')
    this.update({ active: false, cursor: null, focus: null })
    const copy = computerUseCopy(this.lang)
    const done = this.script.tasks.filter((_, index) => this.scene.done[index])
    const remaining = this.script.tasks.filter((_, index) => !this.scene.done[index])
    this.epilogue([{ delay: 800, run: () => this.setReply(copy.stopped(this.script.app.name, done, remaining)) }])
  }

  private epilogue(cues: Cue[]): void {
    this.timeline?.dispose()
    this.timeline = new ScriptTimeline(cues, () => {}, () => this.start(true))
    this.timeline.play()
  }

  private stamp(): string {
    return new Date(Date.now() + (this.timeline?.skipped ?? 0)).toISOString()
  }

  private reset(): void {
    this.scene = initialScene(this.script)
    this.emit()
  }

  private update(patch: Partial<Scene>): void {
    this.scene = { ...this.scene, ...patch }
    this.emit()
  }

  private emit(): void {
    for (const listener of this.listeners) listener()
  }

  private pushUser(text: string): void {
    this.update({ entries: [...this.scene.entries, { kind: 'user', id: ++this.sequence, text }] })
  }

  private pushAssistant(thinking: boolean): void {
    this.update({ entries: [...this.scene.entries, { kind: 'assistant', id: ++this.sequence, thinking, steps: [], text: null, startedAt: this.stamp() }] })
  }

  private patchAssistant(patch: AssistantPatch | ((entry: AssistantEntry) => AssistantPatch)): void {
    const entries = [...this.scene.entries]
    const index = entries.map((entry) => entry.kind).lastIndexOf('assistant')
    const entry = entries[index]
    if (!entry || entry.kind !== 'assistant') return
    entries[index] = { ...entry, ...(typeof patch === 'function' ? patch(entry) : patch) }
    this.update({ entries })
  }

  private settleStep(state: StepState): void {
    this.patchAssistant((entry) => ({
      steps: entry.steps.map((step) => (step.state === 'running' ? { ...step, state, completedAt: this.stamp() } : step))
    }))
  }

  private startStep(text: string, code: string): void {
    this.settleStep('done')
    this.patchAssistant((entry) => ({ steps: [...entry.steps, { id: ++this.sequence, text, code, state: 'running', startedAt: this.stamp() }] }))
  }

  private setReply(text: string): void {
    this.patchAssistant({ text })
  }

  private actionCues(actions: Action[]): Cue[] {
    const cues: Cue[] = []
    for (const [index, action] of actions.entries()) {
      if ('step' in action) {
        const code = stepCode(actionsOfStep(actions, index))
        cues.push({ delay: 500, run: () => this.startStep(action.step, code) })
      }
      else if ('move' in action) cues.push({ delay: 700, run: () => this.update({ cursor: action.move }) })
      else if ('click' in action) {
        cues.push({ delay: 650, run: () => this.update({ cursor: action.click }) })
        cues.push({ delay: 500, run: () => this.update({ clicks: this.scene.clicks + 1, focus: action.click }) })
      } else if ('type' in action) {
        for (let index = 0; index < action.text.length; index += TYPE_CHUNK) {
          const typed = action.text.slice(0, index + TYPE_CHUNK)
          cues.push({ delay: index === 0 ? 300 : 60, run: () => this.update({ values: { ...this.scene.values, [action.type]: typed } }) })
        }
      } else if ('set' in action) cues.push({ delay: 350, run: () => this.update({ values: { ...this.scene.values, ...action.set } }) })
      else if ('done' in action) cues.push({ delay: 0, run: () => this.update({ done: this.scene.done.map((value, index) => value || index === action.done) }) })
      else cues.push({ delay: action.pause, run: () => {} })
    }
    return cues
  }

  private finish(text: string): Cue[] {
    return [
      { delay: 500, run: () => { this.settleStep('done'); this.update({ cursor: null, focus: null }) } },
      { delay: 400, run: () => this.setReply(text) },
      { delay: 500, run: () => this.update({ active: false }) }
    ]
  }

  private mainCues(): Cue[] {
    const { app, confirm } = this.script
    const cues: Cue[] = [
      { delay: 400, run: () => this.pushUser(this.script.prompt) },
      { delay: 500, run: () => this.pushAssistant(true) },
      { delay: 1100, run: () => { this.patchAssistant({ thinking: false }); this.startStep(this.script.findStep, findWindowCode(app.window)); this.update({ active: true }) } }
    ]
    if (app.approval) {
      cues.push({ delay: 800, run: () => { this.update({ gate: 'approval' }); this.timeline?.hold() } })
    }
    cues.push(...this.actionCues(this.script.actions))
    if (!confirm) return [...cues, ...this.finish(this.script.reply)]
    cues.push(
      { delay: 500, run: () => { this.settleStep('done'); this.update({ cursor: null }) } },
      { delay: 400, run: () => { this.setReply(confirm.question); this.update({ active: false }) } },
      { delay: 600, run: () => { this.update({ gate: 'confirm' }); this.timeline?.hold() } },
      { delay: 200, run: () => this.pushUser(confirm.yes.message) },
      { delay: 600, run: () => this.pushAssistant(true) },
      { delay: 900, run: () => { this.patchAssistant({ thinking: false }); this.update({ active: true }) } },
      ...this.actionCues(confirm.actions),
      ...this.finish(confirm.reply)
    )
    return cues
  }
}
