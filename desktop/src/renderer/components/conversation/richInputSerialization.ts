import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from './richInputConstants'
import { SPARKLE_ICON_SVG, TERMINAL_ICON_SVG } from './refIconSvgs'
import { paintFileRefIcon } from './fileRefIconDom'
import type { ComposerDraftSegment } from '../../types/composerDraft'

type RefType = Exclude<ComposerDraftSegment, { type: 'text' }>['type']
type Match = { type: 'file' | 'command' | 'skill'; start: number; end: number; value: string }

export interface RefCatalogCommand {
  name: string
  aliases?: string[]
}

export interface RefCatalogSkill {
  name: string
  available?: boolean
}

export interface ComposerRefCatalog {
  commands?: RefCatalogCommand[]
  skills?: RefCatalogSkill[]
}

export function serializeSkillMarker(skillName: string): string {
  return `$${skillName}`
}

function pushTextSegment(out: ComposerDraftSegment[], value: string): void {
  if (!value) return
  const prev = out[out.length - 1]
  if (prev?.type === 'text') {
    prev.value += value
    return
  }
  out.push({ type: 'text', value })
}

export function createRefSpan(kind: RefType, value: string): HTMLSpanElement {
  const span = document.createElement('span')
  const label = document.createElement('span')
  const icon = document.createElement('span')
  const removeIcon = document.createElement('span')
  span.setAttribute('contenteditable', 'false')
  span.setAttribute('data-ref-type', kind)
  // The chip's box (layout, rest-quiet/hover-revealed border + fill, type colour)
  // comes from the shared .dc-ref* classes in tokens.css so the editor pill and
  // the sent-message bubble ref stay visually identical. Only the few props that
  // differ between the two surfaces (font size, baseline nudge) stay inline.
  span.style.fontSize = '13px'
  span.style.cursor = 'default'

  icon.className = 'dc-ref-icon dc-ref-icon-default'
  icon.setAttribute('aria-hidden', 'true')
  removeIcon.className = 'dc-ref-icon dc-ref-icon-remove'
  removeIcon.setAttribute('aria-hidden', 'true')
  removeIcon.textContent = '✕'
  removeIcon.style.fontWeight = '700'
  removeIcon.style.cursor = 'pointer'

  label.className = 'dc-ref-label'
  if (kind === 'file') {
    span.className = `${FILE_REF_CLASS} dc-ref dc-ref-file`
    span.setAttribute('data-relative-path', value)
    const fileName = value.split('/').pop() ?? value
    label.textContent = fileName
    // Colored VS Code file-type icon (same set as the bubble ref / explorer),
    // painted into raw DOM with a neutral fallback that upgrades when ready.
    paintFileRefIcon(icon, value, 14)
    span.title = value
  } else if (kind === 'command') {
    span.className = `${COMMAND_REF_CLASS} dc-ref dc-ref-command`
    span.setAttribute('data-command', value)
    label.textContent = value.startsWith('/') ? value.slice(1) : value
    icon.innerHTML = TERMINAL_ICON_SVG
  } else {
    span.className = `${SKILL_REF_CLASS} dc-ref dc-ref-skill`
    span.setAttribute('data-skill', value)
    label.textContent = value
    icon.innerHTML = SPARKLE_ICON_SVG
    span.title = `$${value}`
  }
  span.append(icon, removeIcon, label)
  return span
}

export function collectComposerDraftSegments(root: HTMLElement): ComposerDraftSegment[] {
  const out: ComposerDraftSegment[] = []
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      pushTextSegment(out, node.textContent ?? '')
      return
    }
    if (!(node instanceof HTMLElement)) return
    if (node.classList.contains(FILE_REF_CLASS)) {
      const relativePath = node.getAttribute('data-relative-path') ?? ''
      if (relativePath) out.push({ type: 'file', relativePath })
      return
    }
    if (node.classList.contains(COMMAND_REF_CLASS)) {
      const command = node.getAttribute('data-command') ?? ''
      if (command) out.push({ type: 'command', command })
      return
    }
    if (node.classList.contains(SKILL_REF_CLASS)) {
      const skillName = node.getAttribute('data-skill') ?? ''
      if (skillName) out.push({ type: 'skill', skillName })
      return
    }
    if (node.tagName === 'BR') {
      pushTextSegment(out, '\n')
      return
    }
    for (const child of Array.from(node.childNodes)) {
      walk(child)
    }
  }
  for (const child of Array.from(root.childNodes)) {
    walk(child)
  }
  return out
}

export function stringifyComposerDraftSegments(segments: ComposerDraftSegment[]): string {
  let out = ''
  for (const seg of segments) {
    if (seg.type === 'text') out += seg.value
    else if (seg.type === 'file') out += `@${seg.relativePath}`
    else if (seg.type === 'command') out += seg.command
    else out += serializeSkillMarker(seg.skillName)
  }
  return out
}

type BuildEditorFragmentOptions = {
  addSpacers?: boolean
}

export function buildEditorFragmentFromSegments(
  segments: ComposerDraftSegment[],
  options: BuildEditorFragmentOptions = {}
): DocumentFragment {
  const frag = document.createDocumentFragment()
  const { addSpacers = false } = options
  for (const seg of segments) {
    if (seg.type === 'text') {
      if (seg.value.length > 0) {
        frag.appendChild(document.createTextNode(seg.value))
      }
      continue
    }
    if (seg.type === 'file') {
      frag.appendChild(createRefSpan('file', seg.relativePath))
      if (addSpacers) frag.appendChild(document.createTextNode('\u00a0'))
      continue
    }
    if (seg.type === 'command') {
      frag.appendChild(createRefSpan('command', seg.command))
      if (addSpacers) frag.appendChild(document.createTextNode('\u00a0'))
      continue
    }
    frag.appendChild(createRefSpan('skill', seg.skillName))
    if (addSpacers) frag.appendChild(document.createTextNode('\u00a0'))
  }
  return frag
}

export function replaceEditorContentFromSegments(root: HTMLElement, segments: ComposerDraftSegment[]): void {
  root.innerHTML = ''
  root.appendChild(buildEditorFragmentFromSegments(segments))
}

function findNextFileRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '@' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j++
      }
      const relativePath = text.slice(i + 1, j)
      if (relativePath.length > 0) {
        return { type: 'file', start: i, end: j, value: relativePath }
      }
    }
    i++
  }
  return null
}

function findNextSkillRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '$' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && /[a-z0-9_-]/i.test(text[j]!)) {
        j++
      }
      const skillName = text.slice(i + 1, j)
      if (skillName.length > 0) {
        return {
          type: 'skill',
          start: i,
          end: j,
          value: skillName
        }
      }
    }
    i++
  }
  return null
}

function normalizeCommandToken(token: string): string {
  const trimmed = token.trim()
  if (!trimmed) return ''
  return (trimmed.startsWith('/') ? trimmed : `/${trimmed}`).toLowerCase()
}

function normalizeSkillToken(token: string): string {
  return token.trim().replace(/^[$/]+/, '').toLowerCase()
}

function commandCatalogSet(commands: RefCatalogCommand[] | undefined): Set<string> {
  const set = new Set<string>()
  for (const command of commands ?? []) {
    const name = normalizeCommandToken(command.name)
    if (name) set.add(name)
    for (const alias of command.aliases ?? []) {
      const normalized = normalizeCommandToken(alias)
      if (normalized) set.add(normalized)
    }
  }
  return set
}

function skillCatalogSet(skills: RefCatalogSkill[] | undefined): Set<string> {
  const set = new Set<string>()
  for (const skill of skills ?? []) {
    if (skill.available === false) continue
    const name = normalizeSkillToken(skill.name)
    if (name) set.add(name)
  }
  return set
}

function findNextSkillRefWithCatalog(text: string, from: number, skills: Set<string>): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '$' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && /[a-z0-9_-]/i.test(text[j]!)) {
        j++
      }
      const skillName = text.slice(i + 1, j)
      if (skillName.length > 0 && skills.has(normalizeSkillToken(skillName))) {
        return {
          type: 'skill',
          start: i,
          end: j,
          value: skillName
        }
      }
    }
    i++
  }
  return null
}

function isLegacyCommandToken(token: string): boolean {
  return /^\/[a-z0-9][a-z0-9-]*$/i.test(token)
}

function findNextCommandRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '/' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j++
      }
      const token = text.slice(i, j)
      if (isLegacyCommandToken(token)) {
        return { type: 'command', start: i, end: j, value: token }
      }
    }
    i++
  }
  return null
}

function findNextSlashRefWithCatalog(
  text: string,
  from: number,
  commands: Set<string>,
  skills: Set<string>
): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '/' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j++
      }
      const token = text.slice(i, j)
      if (commands.has(normalizeCommandToken(token))) {
        return { type: 'command', start: i, end: j, value: token }
      }
      if (isLegacyCommandToken(token) && skills.has(normalizeSkillToken(token))) {
        return { type: 'skill', start: i, end: j, value: token.slice(1) }
      }
    }
    i++
  }
  return null
}

export function parseLegacyComposerText(text: string): ComposerDraftSegment[] {
  const out: ComposerDraftSegment[] = []
  let cursor = 0

  while (cursor < text.length) {
    const matches = [
      findNextFileRef(text, cursor),
      findNextCommandRef(text, cursor),
      findNextSkillRef(text, cursor)
    ].filter((match): match is Match => match != null)

    if (matches.length === 0) {
      pushTextSegment(out, text.slice(cursor))
      break
    }

    const next = matches.reduce((best, match) => {
      if (match.start < best.start) return match
      return best
    })

    if (next.start > cursor) {
      pushTextSegment(out, text.slice(cursor, next.start))
    }

    if (next.type === 'file') {
      out.push({ type: 'file', relativePath: next.value })
    } else if (next.type === 'command') {
      out.push({ type: 'command', command: next.value })
    } else {
      out.push({ type: 'skill', skillName: next.value })
    }

    cursor = next.end
  }

  return out
}

export function parseComposerTextWithCatalog(
  text: string,
  catalog: ComposerRefCatalog
): ComposerDraftSegment[] {
  const out: ComposerDraftSegment[] = []
  const commands = commandCatalogSet(catalog.commands)
  const skills = skillCatalogSet(catalog.skills)
  let cursor = 0

  while (cursor < text.length) {
    const matches = [
      findNextFileRef(text, cursor),
      findNextSlashRefWithCatalog(text, cursor, commands, skills),
      findNextSkillRefWithCatalog(text, cursor, skills)
    ].filter((match): match is Match => match != null)

    if (matches.length === 0) {
      pushTextSegment(out, text.slice(cursor))
      break
    }

    const next = matches.reduce((best, match) => {
      if (match.start < best.start) return match
      return best
    })

    if (next.start > cursor) {
      pushTextSegment(out, text.slice(cursor, next.start))
    }

    if (next.type === 'file') {
      out.push({ type: 'file', relativePath: next.value })
    } else if (next.type === 'command') {
      out.push({ type: 'command', command: next.value })
    } else {
      out.push({ type: 'skill', skillName: next.value })
    }

    cursor = next.end
  }

  return out
}

export function serializeEditor(root: HTMLElement): string {
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
    if (el.classList.contains(FILE_REF_CLASS)) {
      const p = el.getAttribute('data-relative-path') ?? ''
      if (p) out += `@${p}`
      return
    }
    if (el.classList.contains(COMMAND_REF_CLASS)) {
      const command = el.getAttribute('data-command') ?? ''
      if (command) out += command
      return
    }
    if (el.classList.contains(SKILL_REF_CLASS)) {
      const skill = el.getAttribute('data-skill') ?? ''
      if (skill) out += serializeSkillMarker(skill)
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

/**
 * Trims the editor subtree so {@link serializeEditor} length is <= max, preserving
 * file-ref spans when they fit entirely before the cut (never replaces the whole editor with plain text).
 */
export function truncateEditorDomToSerializedLength(root: HTMLElement, max: number): void {
  if (serializeEditor(root).length <= max) return

  let remaining = max

  function removeTrailingAfter(node: Node): void {
    let cur: Node | null = node
    while (cur && cur !== root) {
      while (cur.nextSibling) {
        cur.nextSibling.remove()
      }
      cur = cur.parentNode
    }
  }

  function process(node: Node): boolean {
    if (remaining <= 0) {
      node.parentNode?.removeChild(node)
      return false
    }
    if (node.nodeType === Node.TEXT_NODE) {
      const t = node.textContent ?? ''
      if (t.length <= remaining) {
        remaining -= t.length
        if (remaining === 0) {
          removeTrailingAfter(node)
          return false
        }
        return true
      }
      node.textContent = t.slice(0, remaining)
      remaining = 0
      removeTrailingAfter(node)
      return false
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return true
    const el = node as HTMLElement
    if (el.tagName === 'STYLE' || el.tagName === 'SCRIPT') {
      return true
    }
    if (el.classList.contains(FILE_REF_CLASS)) {
      const p = el.getAttribute('data-relative-path') ?? ''
      const len = p ? 1 + p.length : 0
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.classList.contains(COMMAND_REF_CLASS)) {
      const command = el.getAttribute('data-command') ?? ''
      const len = command.length
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.classList.contains(SKILL_REF_CLASS)) {
      const skill = el.getAttribute('data-skill') ?? ''
      const len = skill ? serializeSkillMarker(skill).length : 0
      if (len <= remaining) {
        remaining -= len
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    if (el.tagName === 'BR') {
      if (remaining >= 1) {
        remaining -= 1
        if (remaining === 0) {
          removeTrailingAfter(el)
          return false
        }
        return true
      }
      removeTrailingAfter(el)
      el.remove()
      remaining = 0
      return false
    }
    const children = Array.from(node.childNodes)
    for (let i = 0; i < children.length; i++) {
      const child = children[i]!
      const cont = process(child)
      if (!cont) {
        for (let j = i + 1; j < children.length; j++) {
          children[j]!.remove()
        }
        return false
      }
    }
    return true
  }

  const top = Array.from(root.childNodes)
  for (let i = 0; i < top.length; i++) {
    const cont = process(top[i]!)
    if (!cont) {
      for (let j = i + 1; j < top.length; j++) {
        top[j]!.remove()
      }
      break
    }
  }
}
