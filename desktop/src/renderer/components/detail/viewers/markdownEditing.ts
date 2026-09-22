import { StateEffect, StateField, type EditorState, type Extension } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  ViewPlugin,
  WidgetType,
  type DecorationSet
} from '@codemirror/view'
import { syntaxTree } from '@codemirror/language'
import type { ThemeMode } from '../../../../shared/theme'
import { renderMermaidSvg } from '../../conversation/MermaidDiagram'

const focusChanged = StateEffect.define<boolean>()
const focused = StateField.define<boolean>({
  create: () => false,
  update: (value, tr) =>
    tr.effects.reduce(
      (current, effect) => (effect.is(focusChanged) ? effect.value : current),
      value
    )
})

export function markdownEditing(theme: ThemeMode, openLink: (href: string) => void): Extension {
  const decorations = StateField.define<DecorationSet>({
    create: (state) => decorateMarkdown(state, theme),
    update(value, tr) {
      return tr.docChanged ||
        tr.selection ||
        tr.effects.some((effect) => effect.is(focusChanged)) ||
        syntaxTree(tr.startState) !== syntaxTree(tr.state)
        ? decorateMarkdown(tr.state, theme)
        : value
    },
    provide: (field) => EditorView.decorations.from(field)
  })
  return [
    focused,
    decorations,
    ViewPlugin.fromClass(
      class {
        private destroyed = false
        constructor(view: EditorView) {
          queueMicrotask(() => {
            if (!this.destroyed) view.dispatch({ effects: focusChanged.of(view.hasFocus) })
          })
        }
        destroy() {
          this.destroyed = true
        }
      }
    ),
    EditorView.domEventHandlers({
      focus: (_event, view) => {
        view.dispatch({ effects: focusChanged.of(true) })
        return false
      },
      blur: (_event, view) => {
        view.dispatch({ effects: focusChanged.of(false) })
        return false
      },
      click: (event, view) => {
        const link = (event.target as Element).closest<HTMLAnchorElement>('a[data-editor-link]')
        if (!link) return false
        event.preventDefault()
        if (
          event.button !== 0 ||
          event.detail > 1 ||
          !view.state.selection.main.empty ||
          (event.shiftKey && !event.ctrlKey && !event.metaKey)
        )
          return false
        openLink(link.getAttribute('href') ?? '')
        return true
      }
    })
  ]
}

function decorateMarkdown(state: EditorState, theme: ThemeMode): DecorationSet {
  const ranges: ReturnType<Decoration['range']>[] = []
  const hasFocus = state.field(focused, false) ?? false
  const selected = (from: number, to: number) =>
    hasFocus && state.selection.ranges.some((range) => range.to >= from && range.from <= to)
  const line = (from: number, className: string, attributes?: Record<string, string>) =>
    ranges.push(
      Decoration.line({ class: className, attributes }).range(state.doc.lineAt(from).from)
    )
  const hide = (from: number, to: number) => {
    if (to > from) ranges.push(Decoration.replace({}).range(from, to))
  }
  const mark = (from: number, to: number, className: string) => {
    if (to > from) ranges.push(Decoration.mark({ class: className }).range(from, to))
  }
  syntaxTree(state).iterate({
    enter: ({ node }) => {
      const { name, from, to } = node
      const active = selected(from, to)
      if (name.startsWith('ATXHeading') || name.startsWith('SetextHeading')) {
        const level = Number(name.slice(-1))
        line(from, `dc-md-heading dc-md-heading-${level}`)
        for (const marker of node.getChildren('HeaderMark')) {
          if (!selected(marker.from, marker.to)) {
            const end = Math.min(state.doc.lineAt(marker.to).to, marker.to + 1)
            hide(marker.from, end)
          }
        }
      }
      const classes: Record<string, string> = {
        StrongEmphasis: 'dc-md-strong',
        Emphasis: 'dc-md-emphasis',
        Strikethrough: 'dc-md-strike',
        InlineCode: 'dc-md-inline-code'
      }
      if (classes[name]) {
        mark(from, to, classes[name]!)
        if (!active)
          for (let child = node.firstChild; child; child = child.nextSibling) {
            if (child.name.endsWith('Mark')) hide(child.from, child.to)
          }
      }
      if (name === 'Link' || name === 'Autolink') {
        const url = node.getChild('URL')
        if (url) {
          const href = state.doc.sliceString(url.from, url.to)
          if (!/^(javascript|data|vbscript):/i.test(href.trim())) {
            ranges.push(
              Decoration.mark({
                tagName: 'a',
                class: 'dc-md-link',
                attributes: { href, 'data-editor-link': '' }
              }).range(from, to)
            )
          }
          if (!active && name === 'Link') {
            const markers = node.getChildren('LinkMark')
            if (markers.length >= 4) {
              hide(markers[0]!.from, markers[0]!.to)
              hide(markers[1]!.from, to)
            }
          }
          if (!active && name === 'Autolink') {
            for (const marker of node.getChildren('LinkMark')) hide(marker.from, marker.to)
          }
        }
      }
      if (
        name === 'URL' &&
        node.parent?.name !== 'Link' &&
        node.parent?.name !== 'Autolink' &&
        node.parent?.name !== 'Image'
      ) {
        const value = state.doc.sliceString(from, to)
        const href = value.startsWith('www.')
          ? `https://${value}`
          : value.includes('@') && !value.includes(':')
            ? `mailto:${value}`
            : value
        ranges.push(
          Decoration.mark({
            tagName: 'a',
            class: 'dc-md-link',
            attributes: { href, 'data-editor-link': '' }
          }).range(from, to)
        )
      }
      if (name === 'Blockquote' && node.parent?.name !== 'Blockquote') {
        for (
          let number = state.doc.lineAt(from).number;
          number <= state.doc.lineAt(to).number;
          number++
        )
          line(state.doc.line(number).from, 'dc-md-quote')
      }
      if (name === 'QuoteMark') {
        let depth = 0
        for (let parent = node.parent; parent; parent = parent.parent)
          if (parent.name === 'Blockquote') depth++
        if (depth === 1) hide(from, Math.min(state.doc.lineAt(to).to, to + 1))
      }
      if (name === 'ListItem') line(from, 'dc-md-list-item')
      if (name === 'HorizontalRule') {
        line(from, 'dc-md-rule')
        hide(from, to)
      }
      if (name === 'FencedCode') {
        const code = node.getChild('CodeText')
        const info = node.getChild('CodeInfo')
        const language = info ? state.doc.sliceString(info.from, info.to) : ''
        if (language === 'mermaid' && code && !active) {
          ranges.push(
            Decoration.replace({
              widget: new MermaidWidget(state.doc.sliceString(code.from, code.to), theme),
              block: true
            }).range(from, to)
          )
          return false
        }
        const first = state.doc.lineAt(from).number
        const last = state.doc.lineAt(to).number
        for (let number = first; number <= last; number++) {
          line(
            state.doc.line(number).from,
            `dc-md-code-line${number === first ? ' dc-md-code-first' : ''}${number === last ? ' dc-md-code-last' : ''}`
          )
        }
        if (node.getChildren('CodeMark').length === 2) {
          for (const marker of node.getChildren('CodeMark')) hide(marker.from, marker.to)
          if (info) hide(info.from, info.to)
          line(from, 'dc-md-code-label', { 'data-language': language })
        }
        return false
      }
      if (name === 'Table') {
        for (let row = node.firstChild; row; row = row.nextSibling) {
          if (row.name === 'TableDelimiter') {
            line(row.from, 'dc-md-hidden-line')
            hide(row.from, row.to)
            continue
          }
          line(
            row.from,
            `dc-md-table-row${row.name === 'TableHeader' ? ' dc-md-table-header' : ''}`
          )
          for (let cell = row.firstChild; cell; cell = cell.nextSibling) {
            if (cell.name === 'TableDelimiter') hide(cell.from, cell.to)
            if (cell.name === 'TableCell') mark(cell.from, cell.to, 'dc-md-table-cell')
          }
        }
      }
    }
  })
  return Decoration.set(ranges, true)
}

let mermaidId = 0
class MermaidWidget extends WidgetType {
  private destroyed = false
  constructor(
    private source: string,
    private theme: ThemeMode
  ) {
    super()
  }
  eq(other: MermaidWidget) {
    return other.source === this.source && other.theme === this.theme
  }
  toDOM(view: EditorView) {
    const element = document.createElement('div')
    element.className = 'dc-file-editor__markdown-widget'
    element.setAttribute('role', 'img')
    element.setAttribute('aria-label', this.source)
    void renderMermaidSvg({
      id: `dc-file-editor-mermaid-${mermaidId++}`,
      source: this.source,
      themeMode: this.theme
    })
      .then((svg) => {
        if (!this.destroyed) {
          element.innerHTML = svg
          view.requestMeasure()
        }
      })
      .catch(() => {
        if (!this.destroyed) element.textContent = this.source
      })
    return element
  }
  destroy() {
    this.destroyed = true
  }
  ignoreEvent() {
    return false
  }
}
