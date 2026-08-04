import { useEffect, useMemo, useRef } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { ExternalLink } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'
import { SkillAvatar } from '../skills/SkillAvatar'
import { VariantBadge } from '../skills/VariantBadge'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'

interface SkillToolCardProps {
  locale: AppLocale
  skillName: string
  badge: string
  subtitle: string
  showVariantBadge?: boolean
  children?: ReactNode
}

export function SkillToolCard({
  locale,
  skillName,
  badge,
  subtitle,
  showVariantBadge = false,
  children
}: SkillToolCardProps): JSX.Element {
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setPluginCatalogSurface = useUIStore((s) => s.setPluginCatalogSurface)
  const fetchSkills = useSkillsStore((s) => s.fetchSkills)
  const selectSkill = useSkillsStore((s) => s.selectSkill)
  const skills = useSkillsStore((s) => s.skills)
  const skillsLoading = useSkillsStore((s) => s.loading)
  const normalizedSkillName = skillName.trim().toLocaleLowerCase()
  const attemptedFetchForSkillRef = useRef<string | null>(null)
  const skillEntry = useMemo(
    () => skills.find((skill) => skill.name.trim().toLocaleLowerCase() === normalizedSkillName),
    [normalizedSkillName, skills]
  )
  const hasVariant = showVariantBadge || skillEntry?.hasVariant === true

  useEffect(() => {
    if (skillEntry || skillsLoading) return
    if (attemptedFetchForSkillRef.current === normalizedSkillName) return
    attemptedFetchForSkillRef.current = normalizedSkillName
    void fetchSkills()
  }, [fetchSkills, normalizedSkillName, skillEntry, skillsLoading])

  async function openSkill(): Promise<void> {
    usePluginStore.getState().clearSelection()
    setPluginCatalogSurface('skills')
    setActiveMainView('skills')
    await fetchSkills()
    await selectSkill(skillName)
  }

  return (
    <div style={card}>
      <div style={header}>
        <SkillAvatar
          name={skillName}
          displayName={skillEntry?.displayName ?? skillName}
          size={34}
          iconDataUrl={skillEntry?.iconSmallDataUrl ?? skillEntry?.iconLargeDataUrl}
        />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={eyebrowRow}>
            <span style={eyebrow}>{translate(locale, 'skillTool.card.title')}</span>
            <span style={badgeStyle}>{badge}</span>
            {hasVariant ? <VariantBadge compact /> : null}
          </div>
          <div style={title}>{skillName}</div>
        </div>
        <ActionTooltip label={translate(locale, 'skillTool.card.viewInSkills')} placement="top">
          <Button
            variant="ghost"
            size="toolbar"
            iconLeft={<ExternalLink size={13} />}
            onClick={() => void openSkill()}
            aria-label={translate(locale, 'skillTool.card.viewInSkills')}
          >
            {translate(locale, 'skillTool.card.view')}
          </Button>
        </ActionTooltip>
      </div>
      <div style={subtitleStyle}>{subtitle}</div>
      {children}
    </div>
  )
}

const card: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  padding: '12px 14px',
  display: 'flex',
  flexDirection: 'column',
  gap: '8px'
}

const header: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  minWidth: 0
}

const eyebrowRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0
}

const eyebrow: CSSProperties = {
  color: 'var(--text-secondary)',
  fontSize: '11px',
  fontWeight: 600
}

const badgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  padding: '1px 8px',
  borderRadius: '999px',
  background: 'var(--bg-tertiary)',
  color: 'var(--success)',
  fontSize: '10px',
  fontWeight: 600,
  lineHeight: 1.4,
  whiteSpace: 'nowrap'
}

const title: CSSProperties = {
  marginTop: '3px',
  color: 'var(--text-primary)',
  fontSize: '14px',
  fontWeight: 700,
  lineHeight: 1.25,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const subtitleStyle: CSSProperties = {
  color: 'var(--text-secondary)',
  fontSize: '12px',
  lineHeight: 1.45,
  wordBreak: 'break-word'
}
