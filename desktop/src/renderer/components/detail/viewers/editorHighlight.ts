import { StateEffect, StateField, type Extension, type Text } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate
} from '@codemirror/view'
import { syntaxTree } from '@codemirror/language'
import {
  fileCacheKey,
  languageFromPath,
  type FileHighlightResult,
  type HighlighterPool
} from '../../../highlight'

const highlighted = StateEffect.define<{ doc: Text; decorations: DecorationSet }>()

export const editorTokenField = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(value, transaction) {
    let next = value.map(transaction.changes)
    for (const effect of transaction.effects) {
      if (effect.is(highlighted) && effect.value.doc === transaction.state.doc)
        next = effect.value.decorations
    }
    return next
  },
  provide: (field) => EditorView.decorations.from(field)
})

export function editorHighlight(
  pool: HighlighterPool | undefined,
  path: string,
  preview: boolean
): Extension {
  if (!pool) return []
  return [
    editorTokenField,
    ViewPlugin.fromClass(
      class {
        private subscribers: object[] = []
        private frame = 0
        private revision = 0
        private destroyed = false
        constructor(private view: EditorView) {
          this.schedule()
        }
        update(update: ViewUpdate) {
          if (
            update.docChanged ||
            (preview && syntaxTree(update.startState) !== syntaxTree(update.state))
          )
            this.schedule()
        }
        schedule() {
          this.revision++
          this.subscribers.forEach((subscriber) => pool.release(subscriber))
          this.subscribers = []
          cancelAnimationFrame(this.frame)
          this.frame = requestAnimationFrame(() => this.request())
        }
        request() {
          const doc = this.view.state.doc
          const revision = this.revision
          const regions: Array<{ from: number; contents: string; lang?: string }> = []
          if (preview) {
            syntaxTree(this.view.state).iterate({
              enter: ({ node }) => {
                if (node.name !== 'FencedCode') return
                const code = node.getChild('CodeText')
                const info = node.getChild('CodeInfo')
                if (code)
                  regions.push({
                    from: code.from,
                    contents: doc.sliceString(code.from, code.to),
                    lang: info ? doc.sliceString(info.from, info.to).split(/\s/)[0] : 'text'
                  })
                return false
              }
            })
          } else regions.push({ from: 0, contents: doc.toString(), lang: languageFromPath(path) })
          if (regions.length === 0) {
            this.view.dispatch({ effects: highlighted.of({ doc, decorations: Decoration.none }) })
            return
          }
          const results = new Map<number, FileHighlightResult>()
          const complete = () => {
            if (
              this.destroyed ||
              revision !== this.revision ||
              doc !== this.view.state.doc ||
              results.size !== regions.length
            )
              return
            if (this.view.composing) {
              this.frame = requestAnimationFrame(complete)
              return
            }
            const ranges: ReturnType<Decoration['range']>[] = []
            regions.forEach((region, index) => {
              let offset = region.from
              for (const line of results.get(index)!.lines) {
                for (const span of line) {
                  if (span.text.length && span.style) {
                    const style = Object.entries(span.style)
                      .map(([key, value]) => `${key}:${value}`)
                      .join(';')
                    ranges.push(
                      Decoration.mark({ class: 'dc-code-token', attributes: { style } }).range(
                        offset,
                        offset + span.text.length
                      )
                    )
                  }
                  offset += span.text.length
                }
                offset++
              }
            })
            this.view.dispatch({
              effects: highlighted.of({ doc, decorations: Decoration.set(ranges, true) })
            })
          }
          regions.forEach((region, index) => {
            const subscriber = {}
            this.subscribers.push(subscriber)
            const request = {
              ...region,
              name: path,
              cacheKey: fileCacheKey(path, region.lang, region.contents)
            }
            const cached = pool.requestFile(subscriber, request, (result) => {
              results.set(index, result)
              complete()
            })
            if (cached) results.set(index, cached)
          })
          complete()
        }
        destroy() {
          this.destroyed = true
          cancelAnimationFrame(this.frame)
          this.subscribers.forEach((subscriber) => pool.release(subscriber))
        }
      }
    )
  ]
}
