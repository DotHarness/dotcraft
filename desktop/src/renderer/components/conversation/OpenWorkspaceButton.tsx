import { useT } from '../../contexts/LocaleContext'
import { OpenTargetButton } from './OpenTargetButton'

interface OpenWorkspaceButtonProps {
  workspacePath: string
}

export function OpenWorkspaceButton({ workspacePath }: OpenWorkspaceButtonProps): JSX.Element {
  const t = useT()
  return (
    <OpenTargetButton
      targetPath={workspacePath}
      variant="outline"
      tooltipLabel={t('threadHeader.openTitle', { path: workspacePath })}
      menuAriaLabel={t('threadHeader.openMenuAria')}
    />
  )
}
