import { useCallback, useState, type JSX, type MouseEvent } from 'react'
import { Copy, ExternalLink, Globe, Hash, Link2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { copyLinkText, isWebUrl, linkAddress, openWebLink } from '../../utils/webLinks'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'

type Translator = (key: string, vars?: Record<string, string | number>) => string

const MENU_WIDTH = 220
const ICON_SIZE = 16

export interface LinkMenuOptions {
  url: string
  threadId: string | null
  reference?: string
}

export function linkMenuItems(options: LinkMenuOptions & { selectionText: string; t: Translator }): ContextMenuEntry[] {
  const { url, threadId, reference, selectionText, t } = options
  const copySelection: ContextMenuEntry[] = selectionText
    ? [{ label: t('conversation.selection.copy'), icon: <Copy size={ICON_SIZE} />, onClick: () => { void copyLinkText(selectionText, t) } }]
    : []
  if (!isWebUrl(url)) {
    return [
      {
        label: t('conversation.link.open'),
        icon: <ExternalLink size={ICON_SIZE} />,
        onClick: () => { void window.api.shell.openExternal(url).catch(() => undefined) }
      },
      { type: 'separator' },
      ...copySelection,
      { label: t('conversation.link.copyAddress'), icon: <Copy size={ICON_SIZE} />, onClick: () => { void copyLinkText(linkAddress(url), t) } }
    ]
  }
  return [
    { type: 'label', label: t('conversation.link.openIn') },
    ...(threadId
      ? [{
          label: t('conversation.link.newBrowserTab'),
          icon: <Globe size={ICON_SIZE} />,
          onClick: () => { void openWebLink({ url, destination: 'newBrowserTab', threadId, t }) }
        }]
      : []),
    {
      label: t('conversation.link.defaultBrowser'),
      icon: <ExternalLink size={ICON_SIZE} />,
      onClick: () => { void openWebLink({ url, destination: 'systemBrowser', threadId, t }) }
    },
    { type: 'separator' },
    ...copySelection,
    { label: t('conversation.link.copyLink'), icon: <Link2 size={ICON_SIZE} />, onClick: () => { void copyLinkText(url, t) } },
    ...(reference
      ? [{ label: t('conversation.link.copyReference'), icon: <Hash size={ICON_SIZE} />, onClick: () => { void copyLinkText(reference, t) } }]
      : [])
  ]
}

interface OpenMenu {
  position: ContextMenuPosition
  items: ContextMenuEntry[]
  autoFocus: boolean
}

export function useLinkContextMenu(): {
  openFromEvent: (event: MouseEvent<HTMLElement>, options: LinkMenuOptions) => void
  openBelow: (element: HTMLElement, options: LinkMenuOptions) => void
  menu: JSX.Element | null
} {
  const t = useT()
  const [open, setOpen] = useState<OpenMenu | null>(null)

  const show = useCallback((position: ContextMenuPosition, autoFocus: boolean, options: LinkMenuOptions) => {
    const selectionText = window.getSelection()?.toString() ?? ''
    setOpen({ position, autoFocus, items: linkMenuItems({ ...options, selectionText, t }) })
  }, [t])

  const openFromEvent = useCallback((event: MouseEvent<HTMLElement>, options: LinkMenuOptions) => {
    event.preventDefault()
    event.stopPropagation()
    show({ x: event.clientX, y: event.clientY }, false, options)
  }, [show])

  const openBelow = useCallback((element: HTMLElement, options: LinkMenuOptions) => {
    const rect = element.getBoundingClientRect()
    show({ x: rect.left, y: rect.bottom + 4 }, true, options)
  }, [show])

  const menu = open
    ? <ContextMenu items={open.items} position={open.position} width={MENU_WIDTH} autoFocus={open.autoFocus} onClose={() => setOpen(null)} />
    : null

  return { openFromEvent, openBelow, menu }
}
