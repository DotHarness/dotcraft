import { Box, FileText, Globe, Link, Server } from 'lucide-react'
import { useEffect, useLayoutEffect, useRef, useState, type JSX, type ReactNode } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useSkillIconUrl } from '../../../utils/skillIcon'
import { useMcpSourceIconUrl } from '../../../utils/mcpPluginIcon'
import type { Source, SourceKind } from './sources'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'

const SOURCE_LIMIT = 3
const SOURCE_ROWS = ':scope > [data-summary-sources] > li, :scope > [data-dotcraft-plugin-surface] > li'

const SOURCE_GLYPHS: Record<SourceKind, typeof FileText> = {
  file: FileText,
  skill: Box,
  mcp: Server,
  web: Globe
}

interface SourcesProps {
  services: SummaryServices
  threadId: string
  sources: readonly Source[]
  webSearch: boolean
  contributed?: ReactNode
}

function SourceThumbnail({ services, path }: { services: SummaryServices; path: string }): JSX.Element {
  const [src, setSrc] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    setSrc(null)
    services.workspaces.readImageAttachment(path).then(
      (url) => { if (!cancelled) setSrc(url) },
      () => undefined
    )
    return () => { cancelled = true }
  }, [services, path])
  return src ? <img className="conversation-summary-thumbnail" src={src} alt="" draggable={false} /> : <FileText {...GLYPH} />
}

export function SourceIcon({ services, source, threadId }: { services: SummaryServices; source: Source; threadId: string }): JSX.Element {
  const skillIconUrl = useSkillIconUrl(source.kind === 'skill' ? source.id : null)
  const mcpIconUrl = useMcpSourceIconUrl(source.kind === 'mcp' ? source.id : null, source.pluginId, threadId)
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const customIconUrl = skillIconUrl || mcpIconUrl
  if (customIconUrl && failedSrc !== customIconUrl) {
    return (
      <img
        className="conversation-summary-skill-icon"
        src={customIconUrl}
        alt=""
        draggable={false}
        onError={() => setFailedSrc(customIconUrl)}
      />
    )
  }
  if (source.image && source.target) return <SourceThumbnail services={services} path={source.target} />
  const Glyph = SOURCE_GLYPHS[source.kind]
  return <Glyph {...GLYPH} />
}

export function openSource(services: SummaryServices, source: Source): (() => void) | undefined {
  const target = source.target
  if (!target) return undefined
  if (source.kind === 'file') return () => void services.navigation.openFile(target)
  if (source.kind === 'web') return () => void services.navigation.openExternal(target)
  if (source.kind === 'skill') return () => services.navigation.openSkill(target)
  return undefined
}

function SourceRows({ services, threadId, sources, webSearch }: Omit<SourcesProps, 'contributed'>): JSX.Element {
  const t = useT()
  return (
    <div data-summary-sources style={{ display: 'contents' }}>
      {sources.map((source) => (
        <Row
          key={`${source.kind}:${source.id}`}
          leading={<SourceIcon services={services} source={source} threadId={threadId} />}
          label={source.title}
          title={source.kind === 'skill' || source.kind === 'mcp' ? source.title : source.id}
          onClick={openSource(services, source)}
        />
      ))}
      {webSearch && <Row leading={<Globe {...GLYPH} />} label={t('summary.webSearch')} />}
    </div>
  )
}

export function SummarySources({ contributed, onViewAll, ...props }: SourcesProps & { onViewAll: () => void }): JSX.Element {
  const t = useT()
  const list = useRef<HTMLUListElement>(null)
  const [hasSources, setHasSources] = useState(false)

  useLayoutEffect(() => {
    const element = list.current
    if (!element) return
    const apply = (): void => {
      const rows = element.querySelectorAll<HTMLElement>(SOURCE_ROWS)
      rows.forEach((row, index) => row.toggleAttribute('data-source-overflow', index >= SOURCE_LIMIT))
      setHasSources(rows.length > 0)
    }
    apply()
    const observer = new MutationObserver(apply)
    observer.observe(element, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [])

  return (
    <Section title={t('summary.sources')} hideWhenEmpty listRef={list}>
      <SourceRows {...props} />
      {contributed}
      {hasSources && (
        <Row leading={<Link {...GLYPH} />} label={t('summary.viewAll')} muted onClick={onViewAll} />
      )}
    </Section>
  )
}
