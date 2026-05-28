import type { JSX } from 'react'
import {
  EditorGenericIcon,
  ExplorerIcon,
  TerminalBashIcon
} from '../components/ui/AppIcons'

export type EditorInfo = Awaited<ReturnType<typeof window.api.shell.listEditors>>[number]
export type EditorId = EditorInfo['id']

let editorsCache: EditorInfo[] | null = null
let editorsCachePromise: Promise<EditorInfo[]> | null = null

export const EDITOR_ICON_SIZE = 16

export function listEditorsCached(): Promise<EditorInfo[]> {
  if (editorsCache !== null) return Promise.resolve(editorsCache)
  if (editorsCachePromise !== null) return editorsCachePromise
  editorsCachePromise = window.api.shell.listEditors().then((entries) => {
    editorsCache = entries
    return entries
  }).finally(() => {
    editorsCachePromise = null
  })
  return editorsCachePromise
}

export function renderEditorIcon(entry: EditorInfo, size = EDITOR_ICON_SIZE): JSX.Element {
  if (entry.iconDataUrl) {
    return (
      <img
        src={entry.iconDataUrl}
        alt=""
        width={size}
        height={size}
        style={{ display: 'block', objectFit: 'contain', borderRadius: 2 }}
        draggable={false}
      />
    )
  }
  if (entry.iconKey === 'explorer') return <ExplorerIcon size={size} />
  if (entry.iconKey === 'terminal') return <TerminalBashIcon size={size} />
  return <EditorGenericIcon size={size} />
}

export function placeExplorerFirst(editors: EditorInfo[]): EditorInfo[] {
  const explorer = editors.find((entry) => entry.id === 'explorer')
  const withoutExplorer = editors.filter((entry) => entry.id !== 'explorer')
  return explorer ? [explorer, ...withoutExplorer] : withoutExplorer
}
