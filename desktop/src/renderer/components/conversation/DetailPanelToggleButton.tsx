import { useDetailPanelStore } from '../../stores/detailPanelStore'
import { PanelLeft } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

export function DetailPanelToggleButton(): JSX.Element | null {
  const t = useT()
  const detailPanelPreferredVisible = useDetailPanelStore((s) => s.getCurrentPanel().detailPanelPreferredVisible)
  const toggleDetailPanel = useDetailPanelStore((s) => s.toggleDetailPanel)
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
