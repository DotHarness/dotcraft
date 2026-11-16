import { useContext, useEffect, useState } from 'react'
import { attachmentImageScope, ImageReadScopeContext, loadScopedImageDataUrl } from './imagePreview/galleryImages'
import type { ContextImage } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'

export function FeedbackImage({ image }: { image: ContextImage }): JSX.Element | null {
  const t = useT()
  const [dataUrl, setDataUrl] = useState(image.dataUrl ?? '')
  const imageScope = useContext(ImageReadScopeContext)
  useEffect(() => {
    let current = true
    setDataUrl(image.dataUrl ?? '')
    if (!image.dataUrl) {
      void loadScopedImageDataUrl(image.tempPath, imageScope ?? attachmentImageScope())
        .then(result => { if (current) setDataUrl(result) })
        .catch(() => undefined)
    }
    return () => { current = false }
  }, [image.dataUrl, image.tempPath, imageScope])
  return dataUrl
    ? <img className="dc-feedback-thumbnail" src={dataUrl} alt={t('composer.context.screenshotAttached')} onError={() => setDataUrl('')} />
    : null
}
