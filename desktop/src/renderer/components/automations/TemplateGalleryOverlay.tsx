import { useEffect, useState } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  useAutomationsStore,
  type AutomationTemplate
} from '../../stores/automationsStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface Props {
  onSelect(template: AutomationTemplate): void
  onClose(): void
  /** Called when the user clicks the edit pencil on a user template. */
  onEdit?(template: AutomationTemplate): void
  /** Called when the user clicks the "+ Create template" affordance. */
  onCreateNew?(): void
}

/**
 * Modal gallery of automation templates. Built-in templates come first; user-authored templates
 * appear in a dedicated "My templates" section with edit + delete affordances. Used from the
 * AutomationsView templates strip and from the New Task dialog's "Use template" button.
 */
export function TemplateGalleryOverlay({
  onSelect,
  onClose,
  onEdit,
  onCreateNew
}: Props): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const templates = useAutomationsStore((s) => s.templates)
  const fetchTemplates = useAutomationsStore((s) => s.fetchTemplates)
  const deleteTemplate = useAutomationsStore((s) => s.deleteTemplate)

  useEffect(() => {
    void fetchTemplates(locale)
  }, [fetchTemplates, locale])

  const builtIns = templates.filter((tpl) => !tpl.isUser)
  const userTemplates = templates.filter((tpl) => tpl.isUser)

  async function handleDelete(id: string): Promise<void> {
    try {
      await deleteTemplate(id)
    } catch {
      // errors surfaced via toast in future; silent for now.
    }
  }

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 1100,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
          width: '640px',
          maxHeight: '80vh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--bg-primary)',
          border: '1px solid var(--border-default)',
          borderRadius: '10px',
          overflow: 'hidden',
          boxShadow: '0 8px 32px rgba(0,0,0,0.3)'
        }}
      >
        <div
          style={{
            padding: '14px 18px',
            borderBottom: '1px solid var(--border-default)',
            display: 'flex',
            flexDirection: 'column',
            gap: '4px'
          }}
        >
          <div style={{ fontSize: '15px', fontWeight: 600, color: 'var(--text-primary)' }}>
            {t('auto.templates.title')}
          </div>
          <div style={{ fontSize: '12px', color: 'var(--text-tertiary)' }}>
            {t('auto.templates.subtitle')}
          </div>
        </div>

        <div style={{ flex: 1, overflowY: 'auto', padding: '16px' }}>
          {templates.length === 0 && (
            <div
              style={{
                padding: '32px 16px',
                textAlign: 'center',
                fontSize: '13px',
                color: 'var(--text-tertiary)'
              }}
            >
              {t('auto.templates.empty')}
            </div>
          )}

          {onCreateNew && (
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '8px',
                marginBottom: userTemplates.length > 0 || builtIns.length > 0 ? '16px' : 0
              }}
            >
              <SectionHeader label={t('auto.gallery.my.heading')} />
              <div
                style={{
                  display: 'grid',
                  gap: '10px',
                  gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))'
                }}
              >
                {userTemplates.map((tpl) => (
                  <TemplateCard
                    key={tpl.id}
                    template={tpl}
                    onSelect={onSelect}
                    onEdit={onEdit}
                    onDelete={handleDelete}
                  />
                ))}
                <button
                  type="button"
                  onClick={onCreateNew}
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: '4px',
                    padding: '12px',
                    borderRadius: '10px',
                    border: '1px dashed var(--border-default)',
                    backgroundColor: 'transparent',
                    color: 'var(--text-secondary)',
                    textAlign: 'center',
                    cursor: 'pointer',
                    minHeight: '96px',
                    fontSize: '12px',
                    fontWeight: 500
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.borderColor = 'var(--accent)'
                    e.currentTarget.style.color = 'var(--accent)'
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.borderColor = 'var(--border-default)'
                    e.currentTarget.style.color = 'var(--text-secondary)'
                  }}
                >
                  <span style={{ fontSize: '20px', lineHeight: 1 }}>＋</span>
                  <span>{t('auto.gallery.my.create')}</span>
                </button>
              </div>
              {userTemplates.length === 0 && (
                <div
                  style={{
                    fontSize: '11px',
                    color: 'var(--text-tertiary)',
                    marginTop: '4px'
                  }}
                >
                  {t('auto.gallery.my.empty')}
                </div>
              )}
            </div>
          )}

          {builtIns.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
              {onCreateNew && <SectionHeader label={t('auto.gallery.builtin.heading')} />}
              <div
                style={{
                  display: 'grid',
                  gap: '10px',
                  gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))'
                }}
              >
                {builtIns.map((tpl) => (
                  <TemplateCard key={tpl.id} template={tpl} onSelect={onSelect} />
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function SectionHeader({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        fontSize: '11px',
        fontWeight: 600,
        color: 'var(--text-secondary)',
        textTransform: 'uppercase',
        letterSpacing: '0.04em'
      }}
    >
      {label}
    </div>
  )
}

function TemplateCard({
  template,
  onSelect,
  onEdit,
  onDelete
}: {
  template: AutomationTemplate
  onSelect(tpl: AutomationTemplate): void
  onEdit?(tpl: AutomationTemplate): void
  onDelete?(id: string): Promise<void> | void
}): JSX.Element {
  const t = useT()
  const [hover, setHover] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const showActions = !!(template.isUser && (onEdit || onDelete))

  return (
    <div
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => {
        setHover(false)
        setConfirmDelete(false)
      }}
      style={{ position: 'relative' }}
    >
      <button
        type="button"
        onClick={() => onSelect(template)}
        style={{
          width: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'flex-start',
          gap: '6px',
          padding: '12px',
          borderRadius: '10px',
          border: hover ? '1px solid var(--accent)' : '1px solid var(--border-default)',
          backgroundColor: 'var(--bg-secondary)',
          color: 'var(--text-primary)',
          textAlign: 'left',
          cursor: 'pointer',
          minHeight: '96px',
          transition: 'border-color 0.15s'
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', width: '100%' }}>
          <span style={{ fontSize: '20px' }}>{template.icon ?? '✦'}</span>
          <span style={{ fontSize: '13px', fontWeight: 600, flex: 1 }}>{template.title}</span>
          {template.isUser && (
            <span
              style={{
                padding: '2px 6px',
                borderRadius: '999px',
                fontSize: '10px',
                fontWeight: 500,
                color: 'var(--accent)',
                backgroundColor: 'color-mix(in srgb, var(--accent) 12%, transparent)',
                border: '1px solid color-mix(in srgb, var(--accent) 30%, transparent)'
              }}
            >
              {t('auto.gallery.my.badge')}
            </span>
          )}
        </div>
        {template.description && (
          <span style={{ fontSize: '12px', color: 'var(--text-secondary)', lineHeight: 1.4 }}>
            {template.description}
          </span>
        )}
        {template.needsThreadBinding && (
          <span
            style={{
              marginTop: 'auto',
              fontSize: '11px',
              color: 'var(--accent)',
              fontWeight: 500
            }}
          >
            💬 {t('auto.templates.needsThread')}
          </span>
        )}
      </button>

      {showActions && hover && !confirmDelete && (
        <div
          style={{
            position: 'absolute',
            top: '8px',
            right: '8px',
            display: 'flex',
            gap: '4px'
          }}
        >
          {onEdit && (
            <ActionTooltip label={t('auto.gallery.my.edit')} placement="top">
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                onEdit(template)
              }}
              aria-label={t('auto.gallery.my.edit')}
              style={iconBtnStyle}
            >
              ✎
            </button>
            </ActionTooltip>
          )}
          {onDelete && (
            <ActionTooltip label={t('auto.gallery.my.delete')} placement="top">
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                setConfirmDelete(true)
              }}
              aria-label={t('auto.gallery.my.delete')}
              style={{ ...iconBtnStyle, color: 'var(--error)' }}
            >
              🗑
            </button>
            </ActionTooltip>
          )}
        </div>
      )}

      {showActions && confirmDelete && (
        <div
          style={{
            position: 'absolute',
            inset: 0,
            borderRadius: '10px',
            backgroundColor: 'color-mix(in srgb, var(--bg-primary) 88%, transparent)',
            border: '1px solid var(--error)',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '12px',
            gap: '8px'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <span
            style={{
              fontSize: '12px',
              fontWeight: 500,
              color: 'var(--text-primary)',
              textAlign: 'center'
            }}
          >
            {t('auto.gallery.my.deleteConfirm')}
          </span>
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              type="button"
              onClick={() => setConfirmDelete(false)}
              disabled={deleting}
              style={{
                padding: '4px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'transparent',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                cursor: deleting ? 'default' : 'pointer'
              }}
            >
              {t('common.cancel')}
            </button>
            <button
              type="button"
              onClick={async () => {
                if (!onDelete) return
                setDeleting(true)
                try {
                  await onDelete(template.id)
                } finally {
                  setDeleting(false)
                  setConfirmDelete(false)
                }
              }}
              disabled={deleting}
              style={{
                padding: '4px 10px',
                borderRadius: '6px',
                border: 'none',
                backgroundColor: 'var(--error)',
                color: '#fff',
                fontSize: '11px',
                fontWeight: 600,
                cursor: deleting ? 'default' : 'pointer',
                opacity: deleting ? 0.7 : 1
              }}
            >
              {deleting ? t('auto.newTemplate.deleting') : t('auto.newTemplate.deleteConfirmBtn')}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

const iconBtnStyle: React.CSSProperties = {
  width: '24px',
  height: '24px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  cursor: 'pointer',
  padding: 0
}
