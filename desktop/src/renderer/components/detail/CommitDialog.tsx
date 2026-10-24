import { useEffect, useState } from 'react'
import { GitBranch, GitCommitHorizontal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ChangeSubmitDialog } from './ChangeSubmitDialog'

interface CommitDialogProps {
  workspacePath: string
  /** The owner runs the commit so it outlives this dialog; a blank message means autogenerate. */
  onCommit: (message: string) => void
  onClose: () => void
}

export function CommitDialog({ workspacePath, onCommit, onClose }: CommitDialogProps): JSX.Element {
  const t = useT()
  const [branch, setBranch] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    if (!workspacePath.trim()) {
      setBranch(null)
      return
    }
    void window.api.git
      .getBranch(workspacePath)
      .then((value) => {
        if (!cancelled) setBranch(value)
      })
      .catch(() => {
        if (!cancelled) setBranch(null)
      })
    return () => {
      cancelled = true
    }
  }, [workspacePath])

  return (
    <ChangeSubmitDialog
      icon={<GitCommitHorizontal size={18} aria-hidden="true" />}
      workspacePath={workspacePath}
      target={{
        label: t('commit.branchLabel'),
        value: (
          <>
            <GitBranch size={14} />
            {branch || t('commit.detachedHead')}
          </>
        )
      }}
      copy={{
        title: t('commit.title'),
        close: t('commit.close'),
        changesLabel: t('commit.changesLabel'),
        changesSummary: (files) => t('commit.changesSummary', { files }),
        filesHeader: ({ written, all, reverted }) => t('commit.filesHeader', {
          written,
          all,
          reverted: reverted > 0 ? t('commit.revertedSuffix', { count: reverted }) : ''
        }),
        inputLabel: t('commit.messageLabel'),
        placeholder: t('commit.placeholderAuto'),
        submit: t('commit.button')
      }}
      onSubmit={onCommit}
      onClose={onClose}
    />
  )
}
