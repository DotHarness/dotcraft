import { ArrowRightLeft, GitBranch, Laptop } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ChoiceDialog } from '../ui/ChoiceDialog'
import type { ThreadForkMode } from '../../utils/threadFork'

interface ForkChoiceDialogProps {
  onChoose: (mode: ThreadForkMode) => void
  onCancel: () => void
}

export function ForkChoiceDialog({ onChoose, onCancel }: ForkChoiceDialogProps): JSX.Element {
  const t = useT()
  return (
    <ChoiceDialog
      icon={<GitBranch size={18} strokeWidth={2} aria-hidden />}
      title={t('fork.choice.title')}
      description={t('fork.choice.description')}
      onCancel={onCancel}
      options={[
        {
          key: 'local',
          icon: <Laptop size={18} strokeWidth={2} aria-hidden />,
          title: t('fork.intoLocal'),
          subtitle: t('fork.choice.localDesc'),
          onSelect: () => onChoose('local')
        },
        {
          key: 'worktree',
          icon: <ArrowRightLeft size={18} strokeWidth={2} aria-hidden />,
          title: t('fork.intoWorktree'),
          subtitle: t('fork.choice.worktreeDesc'),
          onSelect: () => onChoose('worktree')
        }
      ]}
    />
  )
}
