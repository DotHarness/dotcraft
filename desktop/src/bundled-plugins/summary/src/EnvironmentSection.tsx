import {
  IconButton,
  Menu,
  type DesktopPluginHost,
  type DesktopPluginProjectActionIcon,
  type DesktopPluginProjectActionsSnapshot,
  type MenuEntry
} from '@dotcraft/plugin'
import { Bug, ChevronDown, FlaskConical, FolderCog, Play, Plus, SquareTerminal, TriangleAlert, Wrench } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { fill, type SummaryStrings } from './i18n'
import { GLYPH, Row, Section } from './SummaryRows'

const ACTION_GLYPHS: Record<DesktopPluginProjectActionIcon, typeof Play> = {
  tool: Wrench,
  run: Play,
  debug: Bug,
  test: FlaskConical
}

const HEADER_GLYPH = { size: 14, strokeWidth: 1.75 } as const

function useProjectActions(host: DesktopPluginHost, workspacePath: string | null): DesktopPluginProjectActionsSnapshot | null {
  const [snapshot, setSnapshot] = useState<DesktopPluginProjectActionsSnapshot | null>(null)
  useEffect(() => {
    setSnapshot(null)
    if (!workspacePath) return
    let active = true
    const apply = (next: DesktopPluginProjectActionsSnapshot): void => {
      if (active) setSnapshot(next)
    }
    host.projectActions.list(workspacePath).then(apply, () => {})
    const dispose = host.projectActions.onChange(workspacePath, apply)
    return () => {
      active = false
      dispose()
    }
  }, [host, workspacePath])
  return snapshot
}

function settle(promise: Promise<unknown>): void {
  promise.catch(() => {})
}

export function EnvironmentSection({ host, threadId, workspacePath, strings }: {
  host: DesktopPluginHost
  threadId: string
  workspacePath: string | null
  strings: SummaryStrings
}): JSX.Element | null {
  const snapshot = useProjectActions(host, workspacePath)
  if (!snapshot || !workspacePath) return null

  const { environments } = snapshot
  const selected = environments.find((environment) => environment.id === snapshot.selectedEnvironmentId) ?? null
  const primary = selected?.actions[0]
  const run = (environmentId: string, actionId: string): void =>
    settle(host.projectActions.run({ threadId, environmentId, actionId }))
  const addAction = (): void => settle(
    host.projectActions.promptAddAction({ workspacePath, environmentId: selected?.id }).then((saved) => {
      if (saved) run(saved.environmentId, saved.action.id)
    })
  )

  const groups: MenuEntry[][] = []
  if (selected && !selected.error) {
    if (selected.actions.length > 0) {
      groups.push(selected.actions.map((action) => {
        const Glyph = ACTION_GLYPHS[action.icon]
        return { label: action.name, icon: <Glyph {...HEADER_GLYPH} />, onSelect: () => run(selected.id, action.id) }
      }))
    }
    groups.push([{ label: strings.addAction, icon: <Plus {...HEADER_GLYPH} />, onSelect: addAction }])
  }
  if (environments.length > 1) {
    groups.push([
      { type: 'label', label: strings.changeEnvironment },
      ...environments.map((environment) => ({
        label: environment.name,
        checked: environment.id === selected?.id,
        onSelect: () => settle(host.projectActions.select(workspacePath, environment.id))
      }))
    ])
  }
  const menuItems = groups.flatMap((group, index) => (index === 0 ? group : [{ type: 'separator' as const }, ...group]))
  const PrimaryGlyph = primary ? ACTION_GLYPHS[primary.icon] : null
  const runLabel = primary ? fill(strings.runAction, { action: primary.name }) : ''

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
      {menuItems.length > 0 ? (
        <Menu items={menuItems}>
          <IconButton size={24} icon={<ChevronDown {...HEADER_GLYPH} />} label={strings.actions} tooltipLabel={strings.actions} />
        </Menu>
      ) : null}
      <IconButton
        size={24}
        icon={<SquareTerminal {...HEADER_GLYPH} />}
        label={strings.openTerminal}
        tooltipLabel={strings.openTerminal}
        onClick={() => settle(host.projectActions.openTerminal({ threadId }))}
      />
    </>
  )

  return (
    <Section title={strings.environment} actions={actions}>
      {!selected ? (
        <Row leading={<Plus {...GLYPH} />} label={strings.setUpEnvironment} onClick={addAction} />
      ) : selected.error ? (
        <Row
          leading={<TriangleAlert {...GLYPH} />}
          label={selected.error}
          title={selected.error}
          onClick={() => settle(host.navigation.openFile(selected.path))}
        />
      ) : (
        <Row leading={<FolderCog {...GLYPH} />} label={selected.name} title={selected.path} />
      )}
    </Section>
  )
}
