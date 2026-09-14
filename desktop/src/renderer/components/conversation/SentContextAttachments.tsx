import { useState } from 'react'
import { FileText } from 'lucide-react'
import type { ComposerContextRecord, PastedTextContext } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { ContextFeedback } from './ContextFeedback'
import { ContextPreview } from './ContextPreview'

export function SentContextAttachments({ contexts }: { contexts: ComposerContextRecord[] }): JSX.Element | null {
  const t = useT()
  const [active, setActive] = useState<PastedTextContext | null>(null)
  if (!contexts.length) return null
  return <div className="dc-sent-contexts">
    <ContextFeedback contexts={contexts.filter(context => context.kind !== 'pastedText')} />
    {contexts.filter(context => context.kind === 'pastedText').map(context => <div className="dc-context-attachment" key={context.id}>
      <span className="dc-context-attachment__icon"><FileText size={20} /></span>
      <div className="dc-context-attachment__copy">
        <button type="button" className="dc-context-attachment__title" onClick={() => setActive(context)} aria-label={t('composer.context.previewPaste')}>
          {context.preview.replace(/\s+/g, ' ').slice(0, 80) || t('composer.context.pastedText')}
        </button>
        <span className="dc-context-attachment__subtitle">{t('composer.context.pastedText')}</span>
      </div>
    </div>)}
    {active && <ContextPreview context={active} onClose={() => setActive(null)} restoring={false} />}
  </div>
}
