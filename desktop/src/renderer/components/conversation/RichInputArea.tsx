import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
  type ForwardedRef,
  type KeyboardEvent as ReactKeyboardEvent
} from 'react'
import {
  COMMAND_REF_CLASS,
  FILE_REF_CLASS,
  RICH_REFS_CLIPBOARD_MIME,
  SKILL_REF_CLASS
} from './richInputConstants'
import {
  buildEditorFragmentFromSegments,
  collectComposerDraftSegments,
  createRefSpan,
  parseComposerTextWithCatalog,
  replaceEditorContentFromSegments,
  serializeEditor,
  stringifyComposerDraftSegments,
  truncateEditorDomToSerializedLength,
  type ComposerRefCatalog
} from './richInputSerialization'
import type { ComposerDraftSegment } from '../../types/composerDraft'

const MAX_ROWS = 8
const MAX_TEXT_LEN = 100_000
const PLACEHOLDER = 'Ask DotCraft anything…'
const EMPTY_REF_CATALOG: ComposerRefCatalog = {}

type RefType = 'file' | 'command' | 'skill'
type RichInputContent = string | { text?: string; segments?: ComposerDraftSegment[] }
type SelectionRange = { start: number; end: number }

export interface RichInputAreaHandle {
  getText: () => string
  getSegments: () => ComposerDraftSegment[]
  getSelectionRange: () => SelectionRange | null
  setSelectionRange: (range: SelectionRange) => void
  clear: () => void
  focus: () => void
  insertFileTag: (relativePath: string) => void
  insertCommandTag: (commandName: string) => void
  insertSkillTag: (skillName: string) => void
  /** Replace editor content from stored draft data. */
  setContent: (content: RichInputContent) => void
  /** Replace editor content with plain text (used for composer prefill). */
  setPlainText: (text: string) => void
}

interface RichInputAreaProps {
  disabled?: boolean
  placeholder?: string
  /**
   * `default` draws the bordered input surface, `minimal` strips the chrome for
   * composer surfaces with their own frame, `inline` additionally collapses the
   * vertical footprint so the editor fits a sidebar-height decision row.
   */
  chrome?: 'default' | 'minimal' | 'inline'
  suppressSubmit?: boolean
  onToggleModeShortcut?: () => void
  onHistoryNavigate?: (direction: 'previous' | 'next') => boolean
  historyNavigationActive?: boolean
  onSubmit: () => void
  /** Omitted on welcome screen (no @ popover). */
  onAtQuery?: (query: string | null) => void
  onSlashQuery?: (query: string | null) => void
  onSkillQuery?: (query: string | null) => void
  onContentChange?: () => void
  onSelectionChange?: (range: SelectionRange | null) => void
  onFocusChange?: (focused: boolean) => void
  onPasteImage?: (file: File) => void
  onPasteTextOversized?: () => void
  refCatalog?: ComposerRefCatalog
}

/** Same linearization as textBeforeCaretForTriggers (tags = one boundary char). */
function linearizeForTriggers(root: HTMLElement): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return
    }
    if (el.classList.contains(FILE_REF_CLASS) || el.classList.contains(COMMAND_REF_CLASS)) {
      out += ' '
      return
    }
    if (el.classList.contains(SKILL_REF_CLASS)) {
      out += ' '
      return
    }
    if (el.tagName === 'BR') {
      out += '\n'
      return
    }
    for (const c of Array.from(el.childNodes)) {
      walk(c)
    }
  }
  for (const c of Array.from(root.childNodes)) {
    walk(c)
  }
  return out
}

function textBeforeCaretForTriggers(root: HTMLElement): string {
  const sel = window.getSelection()
  if (!sel || sel.rangeCount === 0) return ''
  const range = sel.getRangeAt(0)
  if (!root.contains(range.startContainer)) return ''
  const endRange = range.cloneRange()
  endRange.selectNodeContents(root)
  endRange.setEnd(range.startContainer, range.startOffset)
  const frag = endRange.cloneContents()
  const div = document.createElement('div')
  div.appendChild(frag)
  return linearizeForTriggers(div)
}

function linearLengthOfNode(node: Node): number {
  if (node.nodeType === Node.TEXT_NODE) {
    return (node.textContent ?? '').length
  }
  if (node.nodeType !== Node.ELEMENT_NODE) return 0
  const el = node as HTMLElement
  if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') return 0
  if (
    el.classList.contains(FILE_REF_CLASS) ||
    el.classList.contains(COMMAND_REF_CLASS) ||
    el.classList.contains(SKILL_REF_CLASS) ||
    el.tagName === 'BR'
  ) {
    return 1
  }

  let total = 0
  for (const child of Array.from(node.childNodes)) {
    total += linearLengthOfNode(child)
  }
  return total
}

function linearOffsetFromPoint(root: HTMLElement, node: Node, offset: number): number | null {
  if (!root.contains(node) && root !== node) return null
  const range = document.createRange()
  try {
    range.selectNodeContents(root)
    range.setEnd(node, offset)
  } catch {
    return null
  }
  const frag = range.cloneContents()
  const div = document.createElement('div')
  div.appendChild(frag)
  return linearizeForTriggers(div).length
}

function locateLinearBoundary(root: HTMLElement, target: number): { node: Node; offset: number } {
  const totalLength = linearLengthOfNode(root)
  const clamped = Math.max(0, Math.min(target, totalLength))

  function walk(parent: Node, startPos: number): { found: { node: Node; offset: number } | null; nextPos: number } {
    let pos = startPos
    const children = Array.from(parent.childNodes)
    for (let index = 0; index < children.length; index += 1) {
      const child = children[index]!
      if (clamped === pos) {
        return { found: { node: parent, offset: index }, nextPos: pos }
      }

      if (child.nodeType === Node.TEXT_NODE) {
        const len = (child.textContent ?? '').length
        if (clamped <= pos + len) {
          return { found: { node: child, offset: clamped - pos }, nextPos: clamped }
        }
        pos += len
        continue
      }

      if (child.nodeType === Node.ELEMENT_NODE) {
        const el = child as HTMLElement
        if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
          continue
        }
        if (
          el.classList.contains(FILE_REF_CLASS) ||
          el.classList.contains(COMMAND_REF_CLASS) ||
          el.classList.contains(SKILL_REF_CLASS) ||
          el.tagName === 'BR'
        ) {
          pos += 1
          if (clamped === pos) {
            return { found: { node: parent, offset: index + 1 }, nextPos: pos }
          }
          continue
        }

        const nested = walk(child, pos)
        if (nested.found) return nested
        pos = nested.nextPos
      }
    }

    if (clamped === pos) {
      return { found: { node: parent, offset: children.length }, nextPos: pos }
    }
    return { found: null, nextPos: pos }
  }

  const located = walk(root, 0).found
  return located ?? { node: root, offset: root.childNodes.length }
}

function walkToLinearOffset(
  root: HTMLElement,
  target: number
): { node: Node; offset: number } | null {
  let pos = 0
  function walk(node: Node): { node: Node; offset: number } | null {
    if (node.nodeType === Node.TEXT_NODE) {
      const len = (node.textContent ?? '').length
      if (pos + len >= target) {
        return { node, offset: target - pos }
      }
      pos += len
      return null
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return null
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return null
    }
    if (
      el.classList.contains(FILE_REF_CLASS) ||
      el.classList.contains(COMMAND_REF_CLASS) ||
      el.classList.contains(SKILL_REF_CLASS)
    ) {
      if (pos + 1 >= target) {
        return { node: el, offset: 0 }
      }
      pos += 1
      return null
    }
    if (el.tagName === 'BR') {
      if (pos + 1 >= target) {
        return { node: el, offset: 0 }
      }
      pos += 1
      return null
    }
    for (const c of Array.from(node.childNodes)) {
      const r = walk(c)
      if (r) return r
    }
    return null
  }
  return walk(root)
}

function parseAtQuery(beforeCaret: string): { fullMatch: string; query: string } | null {
  const m = /(?:^|[\s\n])@([^\s@]*)$/.exec(beforeCaret)
  if (!m) return null
  return { fullMatch: m[0], query: m[1] }
}

function parseSlashQuery(beforeCaret: string): { fullMatch: string; query: string } | null {
  const m = /(?:^|[\s\n])\/([^\s/]*)$/.exec(beforeCaret)
  if (!m) return null
  return { fullMatch: m[0], query: m[1] }
}

function parseSkillQuery(beforeCaret: string): { fullMatch: string; query: string } | null {
  const m = /(?:^|[\s\n])\$([^\s$]*)$/.exec(beforeCaret)
  if (!m) return null
  return { fullMatch: m[0], query: m[1] }
}

function isInlineTagElement(node: Node | null): node is HTMLElement {
  return (
    node instanceof HTMLElement &&
    (node.classList.contains(FILE_REF_CLASS) ||
      node.classList.contains(COMMAND_REF_CLASS) ||
      node.classList.contains(SKILL_REF_CLASS))
  )
}

function isImeConfirmKey(
  e: ReactKeyboardEvent<HTMLDivElement>,
  compositionActive: boolean,
  compositionJustEnded: boolean
): boolean {
  const nativeEvent = e.nativeEvent as KeyboardEvent & { keyCode?: number }
  return (
    compositionActive ||
    compositionJustEnded ||
    nativeEvent.isComposing === true ||
    nativeEvent.keyCode === 229
  )
}

function parseSkillMarkersFromText(text: string, catalog: ComposerRefCatalog): ComposerDraftSegment[] {
  return parseComposerTextWithCatalog(text, catalog)
}

export const RichInputArea = forwardRef(function RichInputArea(
  {
    disabled,
    placeholder = PLACEHOLDER,
    chrome = 'default',
    suppressSubmit,
    onToggleModeShortcut,
    onHistoryNavigate,
    historyNavigationActive = false,
    onSubmit,
    onAtQuery,
    onSlashQuery,
    onSkillQuery,
    onContentChange,
    onSelectionChange,
    onFocusChange,
    onPasteImage,
    onPasteTextOversized,
    refCatalog = EMPTY_REF_CATALOG
  }: RichInputAreaProps,
  ref: ForwardedRef<RichInputAreaHandle>
) {
    const editorRef = useRef<HTMLDivElement>(null)
    const lastSelectionRangeRef = useRef<SelectionRange | null>(null)
    const compositionActiveRef = useRef(false)
    const compositionJustEndedRef = useRef(false)
    const compositionResetTimerRef = useRef<number | null>(null)
    const [showPh, setShowPh] = useState(true)

    const clearCompositionJustEndedTimer = useCallback((): void => {
      if (compositionResetTimerRef.current == null) return
      window.clearTimeout(compositionResetTimerRef.current)
      compositionResetTimerRef.current = null
    }, [])

    useEffect(() => {
      return () => {
        clearCompositionJustEndedTimer()
      }
    }, [clearCompositionJustEndedTimer])

    const syncEmpty = useCallback(() => {
      const el = editorRef.current
      if (!el) return
      const t = el.textContent?.replace(/\u00a0/g, ' ').trim() ?? ''
      const hasTags =
        el.querySelector(`.${FILE_REF_CLASS}`) !== null ||
        el.querySelector(`.${COMMAND_REF_CLASS}`) !== null ||
        el.querySelector(`.${SKILL_REF_CLASS}`) !== null
      setShowPh(!t && !hasTags)
    }, [])

    const adjustHeight = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      // Setting height to 'auto' to measure scrollHeight collapses the overflow
      // and snaps scrollTop to 0. Preserve it across the measurement so committing
      // an IME composition (input → adjustHeight) doesn't jump the editor to the top.
      const prevScrollTop = el.scrollTop
      el.style.height = 'auto'
      const lineHeight = parseInt(getComputedStyle(el).lineHeight) || 20
      const maxHeight = lineHeight * MAX_ROWS + 24
      el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
      el.scrollTop = prevScrollTop
    }, [])

    const getText = useCallback((): string => {
      const el = editorRef.current
      if (!el) return ''
      return serializeEditor(el).slice(0, MAX_TEXT_LEN)
    }, [])

    const getSegments = useCallback((): ComposerDraftSegment[] => {
      const el = editorRef.current
      if (!el) return []
      return collectComposerDraftSegments(el)
    }, [])

    const readSelectionRange = useCallback((): SelectionRange | null => {
      const el = editorRef.current
      const sel = window.getSelection()
      if (!el || !sel || sel.rangeCount === 0) return null
      const range = sel.getRangeAt(0)
      if (!el.contains(range.startContainer) || !el.contains(range.endContainer)) return null

      const start = linearOffsetFromPoint(el, range.startContainer, range.startOffset)
      const end = linearOffsetFromPoint(el, range.endContainer, range.endOffset)
      if (start == null || end == null) return null
      return { start, end }
    }, [])

    const getSelectionRange = useCallback((): SelectionRange | null => {
      const current = readSelectionRange()
      if (current) {
        lastSelectionRangeRef.current = current
        return current
      }
      return lastSelectionRangeRef.current
    }, [readSelectionRange])

    const captureSelectionRange = useCallback((): void => {
      const current = readSelectionRange()
      if (current) {
        lastSelectionRangeRef.current = current
      }
      onSelectionChange?.(current)
    }, [onSelectionChange, readSelectionRange])

    const setSelectionRange = useCallback((range: SelectionRange): void => {
      const el = editorRef.current
      if (!el) return

      const totalLength = linearLengthOfNode(el)
      const start = Math.max(0, Math.min(range.start, totalLength))
      const end = Math.max(start, Math.min(range.end, totalLength))
      const startLoc = locateLinearBoundary(el, start)
      const endLoc = locateLinearBoundary(el, end)
      const nextRange = document.createRange()

      try {
        nextRange.setStart(startLoc.node, startLoc.offset)
        nextRange.setEnd(endLoc.node, endLoc.offset)
      } catch {
        const fallback = locateLinearBoundary(el, totalLength)
        nextRange.setStart(fallback.node, fallback.offset)
        nextRange.setEnd(fallback.node, fallback.offset)
      }

      el.focus()
      const sel = window.getSelection()
      sel?.removeAllRanges()
      sel?.addRange(nextRange)
      lastSelectionRangeRef.current = { start, end }
      onSelectionChange?.({ start, end })
    }, [onSelectionChange])

    const clear = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      el.innerHTML = ''
      onAtQuery?.(null)
      onSlashQuery?.(null)
      onSkillQuery?.(null)
      setShowPh(true)
      adjustHeight()
      lastSelectionRangeRef.current = { start: 0, end: 0 }
      onSelectionChange?.({ start: 0, end: 0 })
      onContentChange?.()
    }, [adjustHeight, onAtQuery, onContentChange, onSelectionChange, onSkillQuery, onSlashQuery])

    const focusEditor = useCallback((): void => {
      editorRef.current?.focus()
    }, [])

    const replaceQueryRangeWithRef = useCallback(
      (kind: RefType, value: string, parsed: { fullMatch: string; query: string }, trigger: '@' | '/' | '$'): void => {
        const el = editorRef.current
        if (!el) return
        const before = textBeforeCaretForTriggers(el)
        const endLinear = before.length
        const leadingWs = parsed.fullMatch.length > 0 && parsed.fullMatch[0] !== trigger ? 1 : 0
        const startLinear = endLinear - parsed.fullMatch.length + leadingWs
        const startLoc = walkToLinearOffset(el, startLinear)
        const endLoc = walkToLinearOffset(el, endLinear)
        if (!startLoc || !endLoc) return
        const range = document.createRange()
        try {
          range.setStart(startLoc.node, startLoc.offset)
          range.setEnd(endLoc.node, endLoc.offset)
          range.deleteContents()
        } catch {
          return
        }
        const span = createRefSpan(kind, value)
        const space = document.createTextNode('\u00a0')
        range.insertNode(span)
        range.setStartAfter(span)
        range.insertNode(space)
        range.setStartAfter(space)
        range.collapse(true)
        const sel = window.getSelection()
        sel?.removeAllRanges()
        sel?.addRange(range)
        onAtQuery?.(null)
        onSlashQuery?.(null)
        onSkillQuery?.(null)
        syncEmpty()
        adjustHeight()
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSkillQuery, onSlashQuery, syncEmpty]
    )

    const insertFileTag = useCallback(
      (relativePath: string): void => {
        const el = editorRef.current
        if (!el) return
        const before = textBeforeCaretForTriggers(el)
        const parsed = parseAtQuery(before)
        if (!parsed) return
        replaceQueryRangeWithRef('file', relativePath, parsed, '@')
      },
      [replaceQueryRangeWithRef]
    )

    const insertCommandTag = useCallback(
      (commandName: string): void => {
        const el = editorRef.current
        if (!el) return
        const command = commandName.trim()
        if (!command.startsWith('/')) return
        const before = textBeforeCaretForTriggers(el)
        const parsed = parseSlashQuery(before)
        if (!parsed) return
        replaceQueryRangeWithRef('command', command, parsed, '/')
      },
      [replaceQueryRangeWithRef]
    )

    const insertSkillTag = useCallback(
      (skillName: string): void => {
        const el = editorRef.current
        if (!el) return
        const name = skillName.trim().replace(/^\/+/, '')
        if (!name) return
        const before = textBeforeCaretForTriggers(el)
        const parsed = parseSkillQuery(before) ?? parseSlashQuery(before)
        if (!parsed) return
        replaceQueryRangeWithRef('skill', name, parsed, before.endsWith(`$${parsed.query}`) ? '$' : '/')
      },
      [replaceQueryRangeWithRef]
    )

    const setPlainText = useCallback(
      (text: string): void => {
        const el = editorRef.current
        if (!el) return
        el.textContent = text
        onAtQuery?.(null)
        onSlashQuery?.(null)
        onSkillQuery?.(null)
        syncEmpty()
        adjustHeight()
        lastSelectionRangeRef.current = {
          start: text.length,
          end: text.length
        }
        onSelectionChange?.({
          start: text.length,
          end: text.length
        })
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSelectionChange, onSkillQuery, onSlashQuery, syncEmpty]
    )

    const setStructuredContent = useCallback(
      (segments: ComposerDraftSegment[]): void => {
        const el = editorRef.current
        if (!el) return
        replaceEditorContentFromSegments(el, segments)
        if (serializeEditor(el).length > MAX_TEXT_LEN) {
          truncateEditorDomToSerializedLength(el, MAX_TEXT_LEN)
        }
        onAtQuery?.(null)
        onSlashQuery?.(null)
        onSkillQuery?.(null)
        syncEmpty()
        adjustHeight()
        const contentLength = linearLengthOfNode(el)
        lastSelectionRangeRef.current = {
          start: contentLength,
          end: contentLength
        }
        onSelectionChange?.({
          start: contentLength,
          end: contentLength
        })
        onContentChange?.()
      },
      [adjustHeight, onAtQuery, onContentChange, onSelectionChange, onSkillQuery, onSlashQuery, syncEmpty]
    )

    const setContent = useCallback(
      (content: RichInputContent): void => {
        const normalized =
          typeof content === 'string'
            ? { text: content }
            : content
        const segments =
          Array.isArray(normalized.segments) && normalized.segments.length > 0
            ? normalized.segments
            : parseComposerTextWithCatalog(normalized.text ?? '', refCatalog)
        setStructuredContent(segments)
      },
      [refCatalog, setStructuredContent]
    )

    useImperativeHandle(
      ref,
      () => ({
        getText,
        getSegments,
        getSelectionRange,
        setSelectionRange,
        clear,
        focus: focusEditor,
        insertFileTag,
        insertCommandTag,
        insertSkillTag,
        setContent,
        setPlainText
      }),
      [
        getText,
        getSegments,
        getSelectionRange,
        setSelectionRange,
        clear,
        focusEditor,
        insertCommandTag,
        insertFileTag,
        insertSkillTag,
        setContent,
        setPlainText
      ]
    )

    useEffect(() => {
      syncEmpty()
    }, [syncEmpty])

    const emitTriggerQueries = useCallback((): void => {
      const el = editorRef.current
      if (!el) return
      const before = textBeforeCaretForTriggers(el)
      const atParsed = parseAtQuery(before)
      const slashParsed = parseSlashQuery(before)
      const skillParsed = parseSkillQuery(before)
      if (atParsed && slashParsed) {
        const atStart = before.length - atParsed.fullMatch.length
        const slashStart = before.length - slashParsed.fullMatch.length
        if (atStart > slashStart) {
          onAtQuery?.(atParsed.query)
          onSlashQuery?.(null)
          onSkillQuery?.(null)
        } else {
          onAtQuery?.(null)
          onSlashQuery?.(slashParsed.query)
          onSkillQuery?.(null)
        }
        return
      }
      if (skillParsed && slashParsed) {
        const skillStart = before.length - skillParsed.fullMatch.length
        const slashStart = before.length - slashParsed.fullMatch.length
        if (skillStart > slashStart) {
          onAtQuery?.(null)
          onSlashQuery?.(null)
          onSkillQuery?.(skillParsed.query)
        } else {
          onAtQuery?.(null)
          onSlashQuery?.(slashParsed.query)
          onSkillQuery?.(null)
        }
        return
      }
      if (atParsed && skillParsed) {
        const atStart = before.length - atParsed.fullMatch.length
        const skillStart = before.length - skillParsed.fullMatch.length
        if (atStart > skillStart) {
          onAtQuery?.(atParsed.query)
          onSlashQuery?.(null)
          onSkillQuery?.(null)
        } else {
          onAtQuery?.(null)
          onSlashQuery?.(null)
          onSkillQuery?.(skillParsed.query)
        }
        return
      }
      if (atParsed) {
        onAtQuery?.(atParsed.query)
        onSlashQuery?.(null)
        onSkillQuery?.(null)
        return
      }
      if (slashParsed) {
        onAtQuery?.(null)
        onSlashQuery?.(slashParsed.query)
        onSkillQuery?.(null)
        return
      }
      if (skillParsed) {
        onAtQuery?.(null)
        onSlashQuery?.(null)
        onSkillQuery?.(skillParsed.query)
        return
      }
      onAtQuery?.(null)
      onSlashQuery?.(null)
      onSkillQuery?.(null)
    }, [onAtQuery, onSkillQuery, onSlashQuery])

    const onInput = useCallback((): void => {
      syncEmpty()
      adjustHeight()
      emitTriggerQueries()
      captureSelectionRange()
      onContentChange?.()
      const el = editorRef.current
      if (el && serializeEditor(el).length > MAX_TEXT_LEN) {
        truncateEditorDomToSerializedLength(el, MAX_TEXT_LEN)
        syncEmpty()
        adjustHeight()
        emitTriggerQueries()
        captureSelectionRange()
        onContentChange?.()
      }
    }, [adjustHeight, captureSelectionRange, emitTriggerQueries, onContentChange, syncEmpty])

    const onKeyDown = useCallback(
      (e: ReactKeyboardEvent<HTMLDivElement>): void => {
        if (e.key === 'Tab' && e.shiftKey) {
          if (onToggleModeShortcut) {
            e.preventDefault()
            onToggleModeShortcut()
          }
          return
        }
        if ((e.key === 'ArrowUp' || e.key === 'ArrowDown') && onHistoryNavigate) {
          const hasModifier = e.altKey || e.ctrlKey || e.metaKey || e.shiftKey
          const el = editorRef.current
          const selection = readSelectionRange()
          if (!hasModifier && el && selection && selection.start === selection.end) {
            const content = serializeEditor(el)
            const linearLength = linearLengthOfNode(el)
            const isSingleLine = !content.includes('\n')
            const atStart = selection.start === 0
            const atEnd = selection.end === linearLength
            const shouldOfferHistory = historyNavigationActive ||
              isSingleLine ||
              (e.key === 'ArrowUp' ? atStart : atEnd)
            if (shouldOfferHistory) {
              const handled = onHistoryNavigate(e.key === 'ArrowUp' ? 'previous' : 'next')
              if (handled) {
                e.preventDefault()
                return
              }
            }
          }
        }
        if (e.key === 'Enter' && !e.shiftKey) {
          if (isImeConfirmKey(e, compositionActiveRef.current, compositionJustEndedRef.current)) {
            compositionJustEndedRef.current = false
            clearCompositionJustEndedTimer()
            return
          }
          if (suppressSubmit) {
            e.preventDefault()
            return
          }
          e.preventDefault()
          if (!disabled) onSubmit()
          return
        }
        if (e.key === 'Backspace') {
          const sel = window.getSelection()
          if (!sel || !sel.isCollapsed || !editorRef.current) return
          const range = sel.getRangeAt(0)
          const { startContainer, startOffset } = range
          if (startContainer.nodeType === Node.TEXT_NODE && startOffset === 0) {
            const prev = startContainer.previousSibling
            if (prev && prev.nodeType === Node.TEXT_NODE && (prev.textContent === '\u00a0' || prev.textContent === ' ')) {
              const beforeSpace = prev.previousSibling
              if (isInlineTagElement(beforeSpace)) {
                e.preventDefault()
                beforeSpace.remove()
                prev.remove()
                onInput()
                return
              }
            }
            if (isInlineTagElement(prev)) {
              e.preventDefault()
              prev.remove()
              onInput()
            }
          }
        }
      },
      [
        clearCompositionJustEndedTimer,
        disabled,
        historyNavigationActive,
        onHistoryNavigate,
        onInput,
        onSubmit,
        onToggleModeShortcut,
        readSelectionRange,
        suppressSubmit
      ]
    )

    const onCompositionStart = useCallback((): void => {
      clearCompositionJustEndedTimer()
      compositionActiveRef.current = true
      compositionJustEndedRef.current = false
    }, [clearCompositionJustEndedTimer])

    const onCompositionEnd = useCallback((): void => {
      compositionActiveRef.current = false
      compositionJustEndedRef.current = true
      clearCompositionJustEndedTimer()
      compositionResetTimerRef.current = window.setTimeout(() => {
        compositionJustEndedRef.current = false
        compositionResetTimerRef.current = null
      }, 0)
    }, [clearCompositionJustEndedTimer])

    const insertClipboardSegmentsAtCaret = useCallback(
      (segments: ComposerDraftSegment[]): void => {
        const editor = editorRef.current
        if (!editor) return
        const sel = window.getSelection()
        if (!sel || sel.rangeCount === 0) return
        const range = sel.getRangeAt(0)
        if (!editor.contains(range.startContainer)) return

        const frag = buildEditorFragmentFromSegments(segments, { addSpacers: true })
        const marker = document.createTextNode('')
        frag.appendChild(marker)

        range.deleteContents()
        range.insertNode(frag)
        range.setStartAfter(marker)
        range.collapse(true)
        marker.remove()
        sel.removeAllRanges()
        sel.addRange(range)
      },
      []
    )

    const onEditorClick = useCallback(
      (e: React.MouseEvent<HTMLDivElement>): void => {
        const target = e.target
        if (!(target instanceof HTMLElement)) return
        if (!target.classList.contains('dc-ref-icon-remove')) return
        const tag = target.closest('[data-ref-type]')
        if (!(tag instanceof HTMLElement)) return
        e.preventDefault()
        e.stopPropagation()
        const next = tag.nextSibling
        const prev = tag.previousSibling
        tag.remove()
        if (next?.nodeType === Node.TEXT_NODE && (next.textContent === '\u00a0' || next.textContent === ' ')) {
          next.remove()
        } else if (
          prev?.nodeType === Node.TEXT_NODE &&
          (prev.textContent === '\u00a0' || prev.textContent === ' ')
        ) {
          prev.remove()
        }
        onInput()
      },
      [onInput]
    )

    const onCopyOrCut = useCallback(
      (e: React.ClipboardEvent<HTMLDivElement>, cut: boolean): void => {
        const editor = editorRef.current
        const sel = window.getSelection()
        if (!editor || !sel || sel.rangeCount === 0 || sel.isCollapsed) return
        const range = sel.getRangeAt(0)
        if (!editor.contains(range.commonAncestorContainer)) return
        const container = document.createElement('div')
        container.appendChild(range.cloneContents())
        const segments = collectComposerDraftSegments(container)
        const plainText = stringifyComposerDraftSegments(segments)
        e.preventDefault()
        e.clipboardData.setData('text/plain', plainText)
        e.clipboardData.setData(RICH_REFS_CLIPBOARD_MIME, JSON.stringify({ version: 1, segments }))
        if (!cut) return
        range.deleteContents()
        onInput()
      },
      [onInput]
    )

    const onPaste = useCallback(
      (e: React.ClipboardEvent<HTMLDivElement>): void => {
        const items = Array.from(e.clipboardData.items)
        const imageItem = items.find((it) => it.type.startsWith('image/'))
        if (imageItem && onPasteImage) {
          e.preventDefault()
          const file = imageItem.getAsFile()
          if (file) onPasteImage(file)
          return
        }
        const richPayload = e.clipboardData.getData(RICH_REFS_CLIPBOARD_MIME)
        if (richPayload.trim().length > 0) {
          try {
            const parsed = JSON.parse(richPayload) as { segments?: ComposerDraftSegment[] }
            const segments = Array.isArray(parsed.segments) ? parsed.segments : []
            if (segments.length > 0) {
              e.preventDefault()
              insertClipboardSegmentsAtCaret(segments)
              onInput()
              return
            }
          } catch {
            // Ignore malformed payload and fall back to plain text.
          }
        }
        const pasted = e.clipboardData.getData('text/plain')
        if (pasted.length > MAX_TEXT_LEN) {
          e.preventDefault()
          const truncated = pasted.slice(0, MAX_TEXT_LEN)
          const segments = parseSkillMarkersFromText(truncated, refCatalog)
          insertClipboardSegmentsAtCaret(segments)
          onInput()
          onPasteTextOversized?.()
          return
        }
        e.preventDefault()
        const text = e.clipboardData.getData('text/plain')
        const segments = parseSkillMarkersFromText(text, refCatalog)
        insertClipboardSegmentsAtCaret(segments)
        onInput()
      },
      [insertClipboardSegmentsAtCaret, onInput, onPasteImage, onPasteTextOversized, refCatalog]
    )

    return (
      <div style={{ position: 'relative', flex: 1, minWidth: 0 }}>
        <style>{`
          .rich-input-area[data-empty="true"]:before {
            content: attr(data-placeholder);
            color: var(--composer-placeholder, var(--text-dimmed));
            pointer-events: none;
            position: absolute;
          }
          .rich-input-area[data-chrome="default"]:focus {
            border-color: var(--border-active);
          }
          .dc-file-ref .dc-ref-icon-remove,
          .dc-command-ref .dc-ref-icon-remove,
          .dc-skill-ref .dc-ref-icon-remove {
            display: none;
          }
          .dc-ref-icon-default {
            display: inline-flex;
            align-items: center;
          }
          .dc-file-ref:hover .dc-ref-icon-default,
          .dc-command-ref:hover .dc-ref-icon-default,
          .dc-skill-ref:hover .dc-ref-icon-default {
            display: none;
          }
          .dc-file-ref:hover .dc-ref-icon-remove,
          .dc-command-ref:hover .dc-ref-icon-remove,
          .dc-skill-ref:hover .dc-ref-icon-remove {
            display: inline-flex;
            cursor: pointer;
          }
        `}</style>
        <div
          ref={editorRef}
          role="textbox"
          aria-multiline="true"
          aria-placeholder={placeholder}
          spellCheck={false}
          autoCorrect="off"
          autoCapitalize="off"
          contentEditable={!disabled}
          suppressContentEditableWarning
          data-placeholder={placeholder}
          data-empty={showPh ? 'true' : 'false'}
          data-chrome={chrome}
          onInput={onInput}
          onClick={onEditorClick}
          onKeyDown={onKeyDown}
          onKeyUp={captureSelectionRange}
          onCompositionStart={onCompositionStart}
          onCompositionEnd={onCompositionEnd}
          onMouseUp={captureSelectionRange}
          onCopy={(e) => onCopyOrCut(e, false)}
          onCut={(e) => onCopyOrCut(e, true)}
          onPaste={onPaste}
          onFocus={() => {
            captureSelectionRange()
            onFocusChange?.(true)
          }}
          onBlur={() => onFocusChange?.(false)}
          className="rich-input-area"
          style={{
            position: 'relative',
            flex: 1,
            resize: 'none',
            border: chrome === 'default' ? '1px solid var(--border-default)' : 'none',
            borderRadius: chrome === 'default' ? '8px' : '0',
            padding: chrome === 'inline' ? '4px 2px' : chrome === 'minimal' ? '8px 2px 6px' : '8px 12px',
            fontSize: 'var(--conversation-font-size)',
            fontWeight: 'var(--conversation-font-weight)',
            lineHeight: 'var(--conversation-line-height)',
            fontFamily: 'var(--font-sans)',
            backgroundColor:
              chrome !== 'default'
                ? 'transparent'
                : disabled
                  ? 'var(--bg-tertiary)'
                  : 'var(--bg-secondary)',
            color: disabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
            outline: 'none',
            overflowY: 'auto',
            minHeight: chrome === 'inline' ? '22px' : '40px',
            cursor: disabled ? 'not-allowed' : 'text',
            opacity: disabled ? 0.6 : 1
          }}
        />
      </div>
    )
  }
)
