import { useCallback, useContext, useState, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { ContextMenu, type ContextMenuPosition } from '../../ui/ContextMenu'
import { ImageLightbox } from '../ImageLightbox'
import { ConversationImagesContext, galleryForImage, type GalleryImage } from './galleryImages'
import { imageContextMenuEntries } from './imageActions'

interface ImagePreviewControls {
  open: (image: GalleryImage, local: GalleryImage[]) => void
  openMenu: (event: ReactMouseEvent, image: GalleryImage, local: GalleryImage[]) => void
  overlay: ReactNode
}

export function useImagePreview(): ImagePreviewControls {
  const t = useT()
  const locale = useLocale()
  const getConversationImages = useContext(ConversationImagesContext)
  const [preview, setPreview] = useState<{ images: GalleryImage[]; index: number } | null>(null)
  const [menu, setMenu] = useState<{
    position: ContextMenuPosition
    image: GalleryImage
    local: GalleryImage[]
  } | null>(null)

  const open = useCallback((image: GalleryImage, local: GalleryImage[]): void => {
    setPreview(galleryForImage(image, local, getConversationImages))
  }, [getConversationImages])

  const openMenu = useCallback((event: ReactMouseEvent, image: GalleryImage, local: GalleryImage[]): void => {
    event.preventDefault()
    event.stopPropagation()
    setMenu({ position: { x: event.clientX, y: event.clientY }, image, local })
  }, [])

  const overlay = (
    <>
      {preview && (
        <ImageLightbox
          images={preview.images}
          initialIndex={preview.index}
          onClose={() => setPreview(null)}
        />
      )}
      {menu && (
        <ContextMenu
          items={imageContextMenuEntries(menu.image, {
            t,
            locale,
            onOpen: () => open(menu.image, menu.local)
          })}
          position={menu.position}
          onClose={() => setMenu(null)}
        />
      )}
    </>
  )

  return { open, openMenu, overlay }
}
