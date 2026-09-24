import { useEffect, useState } from 'react'
import type { ContextImage } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'

export function FeedbackImage({ image }: { image: ContextImage }): JSX.Element | null {
  const t = useT()
  const [dataUrl, setDataUrl] = useState(image.dataUrl ?? '')
  useEffect(() => {
    let current = true
    setDataUrl(image.dataUrl ?? '')
    if (!image.dataUrl) {
      void window.api.workspace.readImageAsDataUrl({ path: image.tempPath })
        .then(result => { if (current) setDataUrl(result.dataUrl ?? '') })
        .catch(() => undefined)
    }
    return () => { current = false }
  }, [image.dataUrl, image.tempPath])
  return dataUrl
    ? <img className="dc-feedback-thumbnail" src={dataUrl} alt={t('composer.context.screenshotAttached')} onError={() => setDataUrl('')} />
    : null
}
