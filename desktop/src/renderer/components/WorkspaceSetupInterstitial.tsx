import type { Ref } from 'react'
import { useT } from '../contexts/LocaleContext'
import { ArrowRight, Folder } from 'lucide-react'
import { DotCraftFullLogo } from './ui/DotCraftLogo'
import { Button } from './ui/Button'

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
          width: '100%'
        }}
      >
        <h1
          style={{
            margin: '0 0 10px',
            fontSize: 'var(--type-title-size)',
            lineHeight: 'var(--type-title-line-height)',
            fontWeight: 'var(--type-title-weight)',
            letterSpacing: 0,
            textAlign: 'center'
          }}
        >
          {t('setupInterstitial.title')}
        </h1>
        <p
          style={{
            maxWidth: '620px',
            margin: '0 0 18px',
            fontSize: 'var(--type-body-size)',
            lineHeight: 'var(--type-body-line-height)',
            color: 'var(--text-secondary)',
            textAlign: 'center'
          }}
        >
          {t('setupInterstitial.description')}
        </p>
        <div
          style={{
            maxWidth: '640px',
            width: '100%',
            display: 'flex',
            alignItems: 'center',
            gap: '12px',
            padding: '14px 16px',
            borderRadius: '10px',
            border: '1px solid var(--border-default)',
            background: 'var(--bg-secondary)',
            color: 'var(--text-secondary)',
            marginBottom: '22px',
            boxSizing: 'border-box'
          }}
        >
          <Folder size={22} strokeWidth={1.8} aria-hidden="true" style={{ color: 'var(--accent)', flexShrink: 0 }} />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontSize: '12px', fontWeight: 600, color: 'var(--text-primary)', marginBottom: '5px' }}>
              {t('setupInterstitial.workspaceLabel')}
            </div>
            <div
              style={{
                fontSize: 'var(--type-secondary-size)',
                lineHeight: 'var(--type-secondary-line-height)',
                fontFamily: 'var(--font-mono)',
                wordBreak: 'break-all'
              }}
            >
              {workspacePath}
            </div>
          </div>
        </div>
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: '10px',
            alignItems: 'stretch',
            width: 'min(300px, 100%)'
          }}
        >
          <Button
            size="prominent"
            variant="primary"
            onClick={onStart}
            loading={isOpening}
            style={{ width: '100%' }}
          >
            {t('setupInterstitial.start')}
            <ArrowRight size={18} strokeWidth={2.2} aria-hidden="true" />
          </Button>
          <Button
            variant="secondary"
            onClick={onChooseDifferentWorkspace}
            disabled={isOpening}
            style={{ width: '100%' }}
          >
            {t('setupInterstitial.chooseDifferent')}
          </Button>
        </div>
      </div>
    </div>
  )
}
