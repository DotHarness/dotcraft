import { Plus } from 'lucide-react'
import { Button } from './ui'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import { OratorioBatonIcon } from './OratorioBatonIcon'
import { useOratorioConnectT } from './settings/oratorio-connect-i18n'
import type { SourceProvider } from './settings/oratorio-settings-model'

export function OratorioBoardEmptyState({ onConnect, onCreateTask }: { onConnect: (provider: SourceProvider) => void; onCreateTask: () => void }): JSX.Element {
  const t = useOratorioConnectT()
  return (
    <section className="ora-board__onboarding" aria-label={t('boardEmptyTitle')}>
      <span className="ora-board__onboarding-mark" aria-hidden="true"><OratorioBatonIcon size={30} strokeWidth={1.8} /></span>
      <div><h2>{t('boardEmptyTitle')}</h2><p>{t('boardEmptyDescription')}</p></div>
      <div className="ora-board__onboarding-actions">
        <button type="button" className="ora-board__onboarding-cta" onClick={() => onConnect('github')}>
          <span aria-hidden="true"><GithubGlyph size={18} /></span>
          <span><strong>{t('connectGitHub')}</strong><span>{t('githubNeed')}</span></span>
        </button>
        <button type="button" className="ora-board__onboarding-cta" onClick={() => onConnect('gitlab')}>
          <span aria-hidden="true"><GitlabGlyph size={18} /></span>
          <span><strong>{t('connectGitLab')}</strong><span>{t('gitlabNeed')}</span></span>
        </button>
      </div>
      <Button variant="ghost" size="sm" iconLeft={<Plus size={13} aria-hidden="true" />} onClick={onCreateTask}>{t('startLocalTask')}</Button>
    </section>
  )
}
