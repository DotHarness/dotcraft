import { useEffect, useId, useRef, useState } from 'react'
import { Layers } from 'lucide-react'
import { create } from 'zustand'
import {
  MAX_SIDEBAR_SECTION_NAME_LENGTH,
  type SidebarSection,
  type SidebarSectionItem
} from '../../../../shared/sidebarSections'
import { useT } from '../../../contexts/LocaleContext'
import { useSidebarSectionsStore } from '../../../stores/sidebarSectionsStore'
import { Button } from '../../ui/Button'
import { Input } from '../../ui/Input'
import { ModalDialog, ModalFooter } from '../../ui/ModalDialog'
import { ModalHeader } from '../../ui/ModalHeader'

export interface SectionDialogRequest {
  section?: SidebarSection
  items?: SidebarSectionItem[]
  afterSectionId?: string
  beforeMove?: () => Promise<void> | void
}

const useSectionDialogStore = create<{ request: SectionDialogRequest | null }>(() => ({ request: null }))

export function openSectionDialog(request: SectionDialogRequest = {}): void {
  useSectionDialogStore.setState({ request })
}

function closeSectionDialog(): void {
  useSectionDialogStore.setState({ request: null })
}

export function SectionDialogHost(): JSX.Element | null {
  const request = useSectionDialogStore((s) => s.request)
  return request ? <SectionDialog request={request} /> : null
}

function SectionDialog({ request }: { request: SectionDialogRequest }): JSX.Element {
  const t = useT()
  const titleId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const editing = request.section != null
  const [name, setName] = useState(request.section?.name ?? '')
  const [busy, setBusy] = useState(false)
  const trimmed = name.trim()

  useEffect(() => {
    inputRef.current?.focus()
    inputRef.current?.select()
  }, [])

  async function submit(): Promise<void> {
    if (busy) return
    const store = useSidebarSectionsStore.getState()
    if (request.section) {
      if (trimmed) store.renameSection(request.section.id, trimmed)
      closeSectionDialog()
      return
    }
    setBusy(true)
    try {
      await request.beforeMove?.()
      store.createSection(trimmed || t('sidebarSections.defaultName'), {
        afterSectionId: request.afterSectionId,
        items: request.items
      })
      closeSectionDialog()
    } finally {
      setBusy(false)
    }
  }

  return (
    <ModalDialog
      titleId={titleId}
      width={400}
      onDismiss={closeSectionDialog}
      onSubmit={() => { void submit() }}
      header={(
        <ModalHeader
          icon={<Layers size={18} aria-hidden />}
          title={editing ? t('sidebarSections.editTitle') : t('sidebarSections.createTitle')}
          titleId={titleId}
          description={t('sidebarSections.dialogDescription')}
          onClose={closeSectionDialog}
          closeLabel={t('common.cancel')}
        />
      )}
      footer={(
        <ModalFooter>
          <Button type="button" variant="secondary" onClick={closeSectionDialog}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={busy} disabled={editing && !trimmed}>
            {editing ? t('sidebarSections.save') : t('sidebarSections.create')}
          </Button>
        </ModalFooter>
      )}
    >
      <Input
        ref={inputRef}
        value={name}
        maxLength={MAX_SIDEBAR_SECTION_NAME_LENGTH}
        placeholder={t('sidebarSections.namePlaceholder')}
        aria-label={t('sidebarSections.namePlaceholder')}
        onChange={(event) => setName(event.target.value)}
      />
    </ModalDialog>
  )
}
