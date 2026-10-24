import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { threadFileSummaries } from '../../stores/turnDiffs'
import { Button } from '../ui/Button'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { Textarea } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { toRelativePath } from './diff/diffRows'
import styles from './ChangeSubmitDialog.module.css'

export interface ChangeSubmitCopy {
  title: string
  close: string
  changesLabel: string
  changesSummary(files: number): string
  filesHeader(counts: { written: number; all: number; reverted: number }): string
  inputLabel: string
  placeholder: string
  submit: string
}

interface ChangeSubmitDialogProps {
  icon: ReactNode
  copy: ChangeSubmitCopy
  workspacePath: string
  target: { label: string; value: ReactNode; control?: boolean }
  onSubmit: (text: string) => void
  onClose: () => void
}

export function ChangeSubmitDialog({ icon, copy, workspacePath, target, onSubmit, onClose }: ChangeSubmitDialogProps): JSX.Element {
  const titleId = useId()
  const inputId = useId()
  const turnDiffs = useConversationStore((s) => s.turnDiffs)
  const allFiles = useMemo(() => threadFileSummaries(turnDiffs), [turnDiffs])
  const writtenFiles = allFiles.filter((f) => f.status === 'written')
  const [text, setText] = useState('')
  const [filesExpanded, setFilesExpanded] = useState(false)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const totalAdditions = writtenFiles.reduce((sum, file) => sum + file.additions, 0)
  const totalDeletions = writtenFiles.reduce((sum, file) => sum + file.deletions, 0)
  const hasFiles = writtenFiles.length > 0

  useEffect(() => {
    inputRef.current?.focus()
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.defaultPrevented) return
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  function submit(): void {
    if (!hasFiles) return
    onSubmit(text.trim())
    onClose()
  }

  return (
    <ModalDialog
      titleId={titleId}
      width={440}
      onDismiss={onClose}
      header={<ModalHeader icon={icon} title={copy.title} titleId={titleId} onClose={onClose} closeLabel={copy.close} />}
      footer={(
        <ModalFooter>
          <Button variant="primary" onClick={submit} disabled={!hasFiles}>
            {copy.submit}
          </Button>
        </ModalFooter>
      )}
    >
      <div className={styles.content}>
        <div className={styles.row}>
          <span className={styles.label}>{target.label}</span>
          <span className={target.control ? styles.control : styles.value}>{target.value}</span>
        </div>
        <button
          type="button"
          className={`${styles.row} ${styles.toggle}`}
          onClick={() => setFilesExpanded((v) => !v)}
          aria-expanded={filesExpanded}
        >
          <span className={styles.label}>{copy.changesLabel}</span>
          <span className={`${styles.value} ${styles.summary}`}>
            <span>{copy.changesSummary(writtenFiles.length)}</span>
            <span className={styles.additions}>+{totalAdditions}</span>
            <span className={styles.deletions}>-{totalDeletions}</span>
            <DisclosureChevron expanded={filesExpanded} direction="reveal" />
          </span>
        </button>

        {filesExpanded && (
          <div className={styles.files}>
            <div className={styles.filesHeader}>
              {copy.filesHeader({ written: writtenFiles.length, all: allFiles.length, reverted: allFiles.length - writtenFiles.length })}
            </div>
            {writtenFiles.map((file) => (
              <div key={file.filePath} className={styles.file}>
                <span className={styles.fileDot} />
                <span className={styles.filePath}>{toRelativePath(file.filePath, workspacePath)}</span>
                <span className={styles.fileStats}>
                  {file.additions > 0 && <span className={styles.additions}>+{file.additions}</span>}
                  {file.deletions > 0 && <span className={styles.deletions}>-{file.deletions}</span>}
                </span>
              </div>
            ))}
          </div>
        )}

        <label htmlFor={inputId} className={styles.inputLabel}>{copy.inputLabel}</label>
        <Textarea
          ref={inputRef}
          id={inputId}
          frameless
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if ((e.metaKey || e.ctrlKey) && e.key === 'Enter') {
              e.preventDefault()
              submit()
            }
          }}
          rows={3}
          placeholder={copy.placeholder}
        />
      </div>
    </ModalDialog>
  )
}
