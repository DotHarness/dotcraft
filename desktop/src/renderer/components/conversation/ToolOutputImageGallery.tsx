import { Fragment, useMemo, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import type { ConversationItem, PluginFunctionContentItem } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'
import { generatedImage, toolOutputImageKey, type GalleryImage } from './imagePreview/galleryImages'
import { useImagePreview } from './imagePreview/useImagePreview'

export interface ToolOutputImageItem {
  id: string
  mediaType: string
  dataBase64: string
  generated?: boolean
  savedPath?: string
  revealPath?: string
}

export function ToolOutputImageGallery({ images }: { images: ToolOutputImageItem[] }): JSX.Element {
  const t = useT()
  const { open, openMenu, overlay } = useImagePreview()
  const gallery = useMemo<GalleryImage[]>(
    () => images.map((image) => ({
      key: image.id,
      src: toolOutputImageDataUrl(image),
      revealPath: image.revealPath
    })),
    [images]
  )

  return (
    <>
      <div data-testid="tool-output-image-gallery" style={galleryStyle}>
        {images.map((image, index) => {
          const entry = gallery[index]
          const button = (
            <button
              type="button"
              aria-label={image.generated
                ? t('conversation.generatedImage.previewAria')
                : t('conversation.toolOutputImage.previewAria', { index: index + 1 })}
              onClick={() => open(entry, gallery)}
              onContextMenu={(event) => openMenu(event, entry, gallery)}
              style={buttonStyle}
            >
              <img
                data-testid="tool-output-image"
                src={entry.src}
                alt={image.generated
                  ? t('conversation.generatedImage.alt')
                  : t('conversation.toolOutputImage.alt', { index: index + 1 })}
                style={imageStyle}
              />
            </button>
          )
          return image.savedPath
            ? <ActionTooltip key={image.id} label={image.savedPath} multiline>{button}</ActionTooltip>
            : <Fragment key={image.id}>{button}</Fragment>
        })}
      </div>
      {overlay}
    </>
  )
}

export function useGeneratedOutputImage(item: ConversationItem): ToolOutputImageItem | null {
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const image = generatedImage(item, !remoteWorkspaceActive)
  if (!image) return null
  return {
    id: image.key,
    mediaType: item.mediaType?.trim() || 'image/png',
    dataBase64: item.result?.trim() ?? '',
    generated: true,
    savedPath: item.savedPath?.trim() || undefined,
    revealPath: image.revealPath
  }
}

export function getToolOutputImages(items: ConversationItem[]): ToolOutputImageItem[] {
  return items.flatMap((item) =>
    (item.contentItems ?? [])
      .map((contentItem, index) => toToolOutputImage(item.id, contentItem, index))
      .filter((image): image is ToolOutputImageItem => image != null)
  )
}

function toToolOutputImage(
  itemId: string,
  contentItem: PluginFunctionContentItem,
  index: number
): ToolOutputImageItem | null {
  const dataBase64 = contentItem.dataBase64?.trim()
  if (contentItem.type !== 'image' || !dataBase64) return null
  return {
    id: toolOutputImageKey(itemId, index),
    mediaType: contentItem.mediaType?.trim() || 'image/png',
    dataBase64
  }
}

function toolOutputImageDataUrl(image: ToolOutputImageItem): string {
  return `data:${image.mediaType};base64,${image.dataBase64}`
}

const galleryStyle: CSSProperties = {
  display: 'flex',
  flexWrap: 'wrap',
  alignItems: 'flex-start',
  gap: '8px',
  padding: '0 6px'
}

const buttonStyle: CSSProperties = {
  display: 'block',
  padding: 0,
  border: 'none',
  borderRadius: '4px',
  background: 'transparent',
  lineHeight: 0,
  cursor: 'zoom-in'
}

const imageStyle: CSSProperties = {
  display: 'block',
  maxWidth: '240px',
  maxHeight: '180px',
  objectFit: 'contain',
  border: '1px solid var(--border-default)',
  borderRadius: '4px',
  background: 'var(--bg-primary)'
}
