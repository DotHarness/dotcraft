import { useEffect, useId, type ReactNode } from 'react'
import { Button } from './Button'
import { ModalDialog, ModalFooter } from './ModalDialog'
import { ModalHeader } from './ModalHeader'
import styles from './ChoiceDialog.module.css'

export interface ChoiceDialogOption {
  key: string
  icon: ReactNode
  title: string
  subtitle: string
  onSelect: () => void
}

interface ChoiceDialogProps {
  icon: ReactNode
  title: string
  description: ReactNode
  options: ChoiceDialogOption[]
  onCancel: () => void
  cancelLabel?: string
}

export function ChoiceDialog({ icon, title, description, options, onCancel, cancelLabel }: ChoiceDialogProps): JSX.Element {
  const titleId = useId()

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onCancel])

  return (
    <ModalDialog
      titleId={titleId}
      width={420}
      onDismiss={onCancel}
      header={<ModalHeader icon={icon} title={title} titleId={titleId} description={description} />}
      footer={cancelLabel ? (
        <ModalFooter>
          <Button variant="secondary" onClick={onCancel}>{cancelLabel}</Button>
        </ModalFooter>
      ) : undefined}
    >
      <div className={styles.options}>
        {options.map((option) => (
          <button key={option.key} type="button" className={styles.option} onClick={option.onSelect}>
            <span className={styles.icon}>{option.icon}</span>
            <span className={styles.text}>
              <span className={styles.title}>{option.title}</span>
              <span className={styles.subtitle}>{option.subtitle}</span>
            </span>
          </button>
        ))}
      </div>
    </ModalDialog>
  )
}
