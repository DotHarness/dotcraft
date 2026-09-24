import { PanelLeft } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

export function DetailPanelToggleButton(): JSX.Element | null {
  const t = useT()
  const detailPanelPreferredVisible = useUIStore((s) => s.detailPanelPreferredVisible)
  const toggleDetailPanel = useUIStore((s) => s.toggleDetailPanel)
  if (detailPanelPreferredVisible) return null
  return (
    <IconButton
      size={28}
      label={t('threadHeader.panelToggleShowLabel')}
      tooltipLabel={t('threadHeader.panelToggleShowLabel')}
      shortcut={ACTION_SHORTCUTS.toggleDetailPanel}
      tooltipPlacement="bottom"
      onClick={toggleDetailPanel}
      icon={<PanelLeft size={16} aria-hidden />}
    />
  )
}
