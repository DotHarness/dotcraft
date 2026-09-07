import { BookOpen, CirclePlay, WandSparkles, FileSearch, FileText, Tag, type LucideIcon } from 'lucide-react'
import type { CSSProperties, JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'

export function AgentBuilderChatEmptyState({ creating = false, onPick }: { creating?: boolean; onPick: (prompt: string) => void }): JSX.Element {
  const t = useT()
  const prompts: { key: string; icon: LucideIcon; label: string; prompt: string }[] = creating ? [
    { key: 'review', icon: FileSearch, label: t('agentBuilder.chat.createReview'), prompt: t('agentBuilder.chat.createReviewPrompt') },
    { key: 'docs', icon: FileText, label: t('agentBuilder.chat.createDocs'), prompt: t('agentBuilder.chat.createDocsPrompt') },
    { key: 'triage', icon: Tag, label: t('agentBuilder.chat.createTriage'), prompt: t('agentBuilder.chat.createTriagePrompt') }
  ] : [
    {
      key: 'test',
      icon: CirclePlay,
      label: t('agentBuilder.chat.quick.test'),
      prompt: t('agentBuilder.chat.prompt.test')
    },
    {
      key: 'advanced',
      icon: BookOpen,
      label: t('agentBuilder.chat.quick.advanced'),
      prompt: t('agentBuilder.chat.prompt.advanced')
    },
    {
      key: 'optimize',
      icon: WandSparkles,
      label: t('agentBuilder.chat.quick.optimize'),
      prompt: t('agentBuilder.chat.prompt.optimize')
    }
  ]

  return (
    <div style={agentBuilderEmptyStyle}>
      <div style={agentBuilderEmptyInnerStyle}>
        <div style={agentBuilderEmptyTitleStyle}>{t(creating ? 'agentBuilder.chat.createTitle' : 'agentBuilder.chat.emptyTitle')}</div>
        <div style={agentBuilderQuickListStyle}>
          {prompts.map((item) => {
            const Icon = item.icon
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => onPick(item.prompt)}
                style={agentBuilderQuickButtonStyle}
              >
                <Icon size={15} strokeWidth={1.9} aria-hidden />
                <span>{item.label}</span>
              </button>
            )
          })}
        </div>
      </div>
    </div>
  )
}

const agentBuilderEmptyStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'flex-end',
  justifyContent: 'flex-start',
  padding: '0 clamp(20px, 4vw, 40px) 18px'
}

const agentBuilderEmptyInnerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 14,
  width: '100%',
  maxWidth: 440
}

const agentBuilderEmptyTitleStyle: CSSProperties = {
  color: 'var(--text-primary)',
  fontSize: 18,
  lineHeight: '24px',
  fontWeight: 650
}

const agentBuilderQuickListStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 8
}

const agentBuilderQuickButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 10,
  width: 'fit-content',
  minHeight: 32,
  padding: '4px 0',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  font: 'inherit',
  fontSize: 14,
  lineHeight: '20px',
  cursor: 'pointer',
  textAlign: 'left'
}
