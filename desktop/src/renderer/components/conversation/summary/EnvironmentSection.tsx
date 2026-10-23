import type {
  DesktopPluginProjectActionIcon,
  DesktopPluginProjectActionsSnapshot,
  MenuEntry
} from '@dotcraft/plugin'
import { Bug, Ellipsis, FileDiff, FlaskConical, Play, Plus, SquareTerminal, TriangleAlert, Wrench } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { DesktopPluginMenu as Menu } from '../../desktopPlugins/DesktopPluginMenu'
import { IconButton } from '../../ui/IconButton'
import type { ChangeTotals } from './sources'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'

const ACTION_GLYPHS: Record<DesktopPluginProjectActionIcon, typeof Play> = {
  tool: Wrench,
  run: Play,
  debug: Bug,
  test: FlaskConical
}

const HEADER_GLYPH = { size: 14, strokeWidth: 1.75 } as const

function useProjectActions(services: SummaryServices, workspacePath: string | null): DesktopPluginProjectActionsSnapshot | null {
  const [snapshot, setSnapshot] = useState<DesktopPluginProjectActionsSnapshot | null>(null)
  useEffect(() => {
    setSnapshot(null)
    if (!workspacePath) return
    let active = true
    const apply = (next: DesktopPluginProjectActionsSnapshot): void => {
      if (active) setSnapshot(next)
    }
    services.projectActions.list(workspacePath).then(apply, () => {})
    const dispose = services.projectActions.onChange(workspacePath, apply)
    return () => {
      active = false
      dispose()
    }
  }, [services, workspacePath])
  return snapshot
}

function settle(promise: Promise<unknown>): void {
  promise.catch(() => {})
}

function ChangesRow({ services, changes }: {
  services: SummaryServices
  changes: ChangeTotals
}): JSX.Element {
  const t = useT()
  const meta = changes.additions > 0 || changes.deletions > 0 ? (
    <>
      {changes.additions > 0 ? <span className="conversation-summary-row__added">+{changes.additions}</span> : null}
      {changes.deletions > 0 ? <span className="conversation-summary-row__removed">-{changes.deletions}</span> : null}
    </>
  ) : null
  return (
    <Row
      leading={<FileDiff {...GLYPH} />}
      label={t('summary.changes')}
      meta={meta}
      onClick={() => services.navigation.openDetailPanel('changes')}
    />
  )
}

export function EnvironmentSection({ services, threadId, workspacePath, changes }: {
  services: SummaryServices
  threadId: string
  workspacePath: string | null
  changes: ChangeTotals | null
}): JSX.Element | null {
  const t = useT()
  const snapshot = useProjectActions(services, workspacePath)
  const changesRow = changes ? <ChangesRow services={services} changes={changes} /> : null
  if (!snapshot || !workspacePath) {
    return changesRow ? <Section title={t('summary.environment')}>{changesRow}</Section> : null
  }

  const { environments } = snapshot
  const selected = environments.find((environment) => environment.id === snapshot.selectedEnvironmentId) ?? null
  const primary = selected?.actions[0]
  const run = (environmentId: string, actionId: string): void =>
    settle(services.projectActions.run({ threadId, environmentId, actionId }))
  const addAction = (): void => settle(
    services.projectActions.promptAddAction({ workspacePath, environmentId: selected?.id }).then((saved) => {
      if (saved) run(saved.environmentId, saved.action.id)
    })
  )

  const groups: MenuEntry[][] = []
  if (!selected) {
    groups.push([{ label: t('summary.setUpEnvironment'), icon: <Plus {...HEADER_GLYPH} />, onSelect: addAction }])
  } else if (selected.error) {
    groups.push([{
      label: t('summary.fixEnvironment'),
      icon: <TriangleAlert {...HEADER_GLYPH} />,
      onSelect: () => settle(services.navigation.openFile(selected.path))
    }])
  } else {
    groups.push([
      ...selected.actions.map((action) => {
        const Glyph = ACTION_GLYPHS[action.icon]
        return { label: action.name, icon: <Glyph {...HEADER_GLYPH} />, onSelect: () => run(selected.id, action.id) }
      }),
      { label: t('summary.addAction'), icon: <Plus {...HEADER_GLYPH} />, onSelect: addAction }
    ])
  }
  if (environments.length > 1) {
    groups.push([
      { type: 'label', label: t('summary.changeEnvironment') },
      ...environments.map((environment) => ({
        label: environment.name,
        checked: environment.id === selected?.id,
        onSelect: () => settle(services.projectActions.select(workspacePath, environment.id))
      }))
    ])
  }
  groups.push([{
    label: t('summary.openTerminal'),
    icon: <SquareTerminal {...HEADER_GLYPH} />,
    onSelect: () => settle(services.projectActions.openTerminal({ threadId }))
  }])
  const menuItems = groups.flatMap((group, index) => (index === 0 ? group : [{ type: 'separator' as const }, ...group]))
  const PrimaryGlyph = primary ? ACTION_GLYPHS[primary.icon] : null
  const runLabel = primary ? t('summary.runAction', { action: primary.name }) : ''

  const actions = (
    <>
      {primary && PrimaryGlyph && selected ? (
        <IconButton
          size={24}
          icon={<PrimaryGlyph {...HEADER_GLYPH} />}
          label={runLabel}
          tooltipLabel={runLabel}
          onClick={() => run(selected.id, primary.id)}
        />
      ) : null}
      <Menu items={menuItems}>
        <IconButton size={24} icon={<Ellipsis {...HEADER_GLYPH} />} label={t('summary.actions')} tooltipLabel={t('summary.actions')} />
      </Menu>
    </>
  )

  return <Section title={t('summary.environment')} actions={actions}>{changesRow}</Section>
}
