/**
 * PDF viewer using Electron's built-in Chromium PDF renderer.
 * The `dotcraft-viewer://` URL is served from the main process with
 * workspace boundary enforcement.
 */
import { useState } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { buildViewerUrlRenderer } from '../../../utils/viewerUrl'

interface PdfViewerProps {
  absolutePath: string
}

export function PdfViewer({ absolutePath }: PdfViewerProps): JSX.Element {
  const t = useT()
  const [loadError, setLoadError] = useState(false)
  const src = buildViewerUrlRenderer(absolutePath)

  if (loadError) {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: '13px',
        padding: '24px',
        textAlign: 'center'
      }}>
        {t('viewer.pdfRenderFailed')}
      </div>
    )
  }

  return (
    <embed
      type="application/pdf"
      src={src}
      style={{ width: '100%', height: '100%', border: 'none', display: 'block' }}
      title={absolutePath.replace(/\\/g, '/').split('/').pop()}
      onError={() => setLoadError(true)}
    />
  )
}
