import { FolderGit2, Save, User } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ChoiceDialog } from '../ui/ChoiceDialog'
import type { SaveTarget } from './agentProfileDraft'

interface AgentSaveTargetDialogProps {
  name: string
  onChoose: (target: SaveTarget) => void
  onCancel: () => void
}

export function AgentSaveTargetDialog({ name, onChoose, onCancel }: AgentSaveTargetDialogProps): JSX.Element {
  const t = useT()
  return (
    <ChoiceDialog
      icon={<Save size={18} strokeWidth={2} aria-hidden />}
      title={t('agentBuilder.saveTarget.title')}
      description={name ? t('agentBuilder.saveTarget.description', { name }) : t('agentBuilder.saveTarget.descriptionUnnamed')}
      onCancel={onCancel}
      cancelLabel={t('common.cancel')}
      options={[
        {
          key: 'user',
          icon: <User size={18} strokeWidth={2} aria-hidden />,
          title: t('agentBuilder.saveTarget.user'),
          subtitle: t('agentBuilder.saveTarget.userDescription'),
          onSelect: () => onChoose('user')
        },
        {
          key: 'workspace',
          icon: <FolderGit2 size={18} strokeWidth={2} aria-hidden />,
          title: t('agentBuilder.saveTarget.workspace'),
          subtitle: t('agentBuilder.saveTarget.workspaceDescription'),
          onSelect: () => onChoose('workspace')
        }
      ]}
    />
  )
}
