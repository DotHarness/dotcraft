import { ArrowLeft, ArrowRight } from 'lucide-react'
import type { CSSProperties } from 'react'

import { useT } from '../../contexts/LocaleContext'
import { useAppNavigationStore } from '../../stores/appNavigationStore'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

const noDrag: CSSProperties = { WebkitAppRegion: 'no-drag' }

export function AppNavigationControls(): JSX.Element | null {
  const t = useT()
  const canGoBack = useAppNavigationStore((state) => state.canGoBack)
  const canGoForward = useAppNavigationStore((state) => state.canGoForward)
  const goBack = useAppNavigationStore((state) => state.goBack)
  const goForward = useAppNavigationStore((state) => state.goForward)

  if (window.api.platform === 'darwin') return null

  return (
    <div
      data-testid="app-navigation-controls"
      style={{
        ...noDrag,
        display: 'inline-flex',
        alignItems: 'center',
        gap: 2,
        flexShrink: 0
      }}
    >
      <IconButton
        icon={<ArrowLeft size={16} strokeWidth={1.8} aria-hidden />}
        label={t('navigation.back')}
        tooltipLabel={t('navigation.back')}
        shortcut={ACTION_SHORTCUTS.navigateBack}
        tooltipPlacement="bottom"
        size={28}
        radius={7}
        disabled={!canGoBack}
        onClick={goBack}
      />
      <IconButton
        icon={<ArrowRight size={16} strokeWidth={1.8} aria-hidden />}
        label={t('navigation.forward')}
        tooltipLabel={t('navigation.forward')}
        shortcut={ACTION_SHORTCUTS.navigateForward}
        tooltipPlacement="bottom"
        size={28}
        radius={7}
        disabled={!canGoForward}
        onClick={goForward}
      />
    </div>
  )
}
