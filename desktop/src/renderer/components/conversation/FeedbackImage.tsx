import { useEffect, useState } from 'react'
import type { ContextImage } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { ImageLightbox } from './ImageLightbox'

export function FeedbackImage({ image, title }: { image: ContextImage; title: string }): JSX.Element {
  const t = useT()
  const [dataUrl, setDataUrl] = useState(image.dataUrl ?? '')
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)
  useEffect(() => {
    let current = true
    setDataUrl(image.dataUrl ?? '')
    setFailed(false)
    if (!image.dataUrl) {
      void window.api.workspace.readImageAsDataUrl({ path: image.tempPath })
        .then(result => { if (current) { setDataUrl(result.dataUrl ?? ''); setFailed(!result.dataUrl) } })
        .catch(() => { if (current) setFailed(true) })
    }
    return () => { current = false }
  }, [image.dataUrl, image.tempPath])
  return <>
    {failed ? <span role="status">{t('composer.context.imageUnavailable')}</span>
      : dataUrl ? <button type="button" className="dc-feedback-image" onClick={() => setOpen(true)} aria-label={title}>
        <img src={dataUrl} alt={title} onError={() => setFailed(true)} />
      </button> : <span role="status">{t('composer.context.loading')}</span>}
    {open && dataUrl && <ImageLightbox src={dataUrl} onClose={() => setOpen(false)} />}
  </>
}
