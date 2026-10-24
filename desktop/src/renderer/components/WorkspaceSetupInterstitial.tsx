import type { Ref } from 'react'
import { useT } from '../contexts/LocaleContext'
import { ArrowRight, Folder } from 'lucide-react'
import { DotCraftFullLogo } from './ui/DotCraftLogo'
import { Button } from './ui/Button'
import { ActionTooltip } from './ui/ActionTooltip'
import { ChangePath } from './detail/changes/ChangePath'

interface WorkspaceSetupInterstitialProps {
  workspacePath: string
  isOpening: boolean
  hideLogo?: boolean
  logoAnchorRef?: Ref<HTMLDivElement>
  onStart: () => void
  onChooseDifferentWorkspace: () => void
}

export function WorkspaceSetupInterstitial({
  workspacePath,
  isOpening,
  hideLogo = false,
  logoAnchorRef,
  onStart,
  onChooseDifferentWorkspace
}: WorkspaceSetupInterstitialProps): JSX.Element {
  const t = useT()

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        padding: '40px 24px',
        background: 'var(--welcome-surface)',
        color: 'var(--text-primary)',
        boxSizing: 'border-box'
      }}
      className={isOpening ? 'setup-interstitial--opening' : undefined}
    >
      <div
        className={hideLogo ? 'setup-logo-focus setup-logo-focus--hidden' : 'setup-logo-focus'}
        aria-hidden="true"
        ref={logoAnchorRef}
      >
        <DotCraftFullLogo size={96} className="setup-logo-image" />
      </div>
      <div
        className="setup-interstitial-content"
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          width: '100%',
          maxWidth: '400px'
        }}
      >
        <h1
          style={{
            margin: '0 0 8px',
            fontSize: 'var(--type-detail-title-size)',
            lineHeight: 'var(--type-detail-title-line-height)',
            fontWeight: 'var(--type-title-weight)',
            letterSpacing: 0,
            textAlign: 'center'
          }}
        >
          {t('setupInterstitial.title')}
        </h1>
        <p
          style={{
            margin: '0 0 16px',
            fontSize: 'var(--type-body-size)',
            lineHeight: 'var(--type-body-line-height)',
            color: 'var(--text-secondary)',
            textAlign: 'center'
          }}
        >
          {t('setupInterstitial.description')}
        </p>
        <ActionTooltip
          label={workspacePath}
          multiline
          wrapperStyle={{ display: 'flex', maxWidth: '100%', marginBottom: '24px' }}
        >
          <div
            tabIndex={0}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              minWidth: 0,
              color: 'var(--text-secondary)',
              fontSize: 'var(--type-secondary-size)',
              lineHeight: 'var(--type-secondary-line-height)',
              fontFamily: 'var(--font-mono)'
            }}
          >
            <Folder size={14} strokeWidth={1.8} aria-hidden="true" style={{ flexShrink: 0 }} />
            <ChangePath path={workspacePath} />
          </div>
        </ActionTooltip>
        <div style={{ display: 'flex', gap: '8px', width: '100%' }}>
          <Button
            size="prominent"
            variant="secondary"
            onClick={onChooseDifferentWorkspace}
            disabled={isOpening}
            style={{ flex: 1 }}
          >
            {t('setupInterstitial.chooseDifferent')}
          </Button>
          <Button
            size="prominent"
            variant="primary"
            onClick={onStart}
            loading={isOpening}
            style={{ flex: 1 }}
          >
            {t('setupInterstitial.start')}
            <ArrowRight size={16} strokeWidth={2.2} aria-hidden="true" />
          </Button>
        </div>
      </div>
    </div>
  )
}
