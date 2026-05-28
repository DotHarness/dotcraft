import { useT } from '../../../contexts/LocaleContext'
import { FileX2 } from 'lucide-react'

interface UnsupportedViewerProps {
  filePath: string
}

export function UnsupportedViewer({ filePath }: UnsupportedViewerProps): JSX.Element {
  const t = useT()
  const fileName = filePath.replace(/\\/g, '/').split('/').pop() ?? filePath

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        gap: '12px',
        color: 'var(--text-secondary)',
        padding: '24px',
        textAlign: 'center'
      }}
    >
      <FileX2 size={32} strokeWidth={1.5} aria-hidden style={{ opacity: 0.5 }} />
      <div style={{ fontSize: '14px', fontWeight: 500 }}>
        {t('viewer.unsupportedTitle')}
      </div>
      <div style={{ fontSize: '13px', opacity: 0.7, maxWidth: '300px', wordBreak: 'break-all' }}>
        {t('viewer.unsupportedHint')} — {fileName}
      </div>
    </div>
  )
}
