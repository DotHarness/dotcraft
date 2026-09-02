import { useMemo, useState, type JSX, type ReactNode } from 'react'
import { Cpu, Server, ShieldCheck, SlidersHorizontal, Tag, Wrench, type LucideIcon } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import {
  BUILDER_FIELD_LABEL_KEYS,
  parseBuilderToolResult,
  type BuilderField,
  type BuilderToolChange,
  type BuilderToolResult
} from '../agents/agentBuilderDraftSync'
import { AGENT_CONTROL_OPTIONS, APPROVAL_OPTIONS, type AgentProviderPreference } from '../agents/agentProfileDraft'
import { MarkdownRenderer } from './MarkdownRenderer'
import { SkillRef } from './SkillToolLabel'
import { ToolDisclosure } from './ToolDisclosure'

interface AgentBuilderEditCardProps {
  item: ConversationItem
  field: BuilderField
  locale: AppLocale
}

const TITLE_CHIP_LIMIT = 3
const SLOT = '__DOTCRAFT_SLOT__'

type Args = Record<string, unknown> | undefined

interface EditDisplay {
  title: ReactNode
  panel: ReactNode | null
  failed: boolean
}

/** A completed Agent Builder edit stated as a profile-change row. */
export function AgentBuilderEditCard({ item, field, locale }: AgentBuilderEditCardProps): JSX.Element {
  const result = useMemo(() => parseBuilderToolResult(item.result), [item.result])
  const [expanded, setExpanded] = useState(false)
  const display = describeBuilderEdit(field, result, item.arguments, item.result, locale)

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={display.panel != null}
      tone={display.failed ? 'error' : undefined}
      title={display.title}
    >
      <div className="dc-tool-panel-surface dc-profile-edit-panel" data-padded="true">
        {display.panel}
      </div>
    </ToolDisclosure>
  )
}

export function describeBuilderEdit(
  field: BuilderField,
  result: BuilderToolResult | null,
  args: Args,
  rawResult: string | undefined,
  locale: AppLocale
): EditDisplay {
  const fieldLabel = translate(locale, BUILDER_FIELD_LABEL_KEYS[field])
  const t = (key: string, vars?: Record<string, string>): string => translate(locale, key, vars)

  if (!result) {
    const text = rawResult?.trim() ?? ''
    return {
      title: t('agentBuilder.editing.updatedField', { field: fieldLabel }),
      panel: text ? <div className="dc-profile-edit-text">{text}</div> : null,
      failed: false
    }
  }

  if (!result.ok) {
    const error = result.error?.trim() ?? ''
    const preview = error.length > 80 ? `${error.slice(0, 80)}…` : error
    return {
      title: (
        <>
          {t('toolCall.agentBuilder.failed', { field: fieldLabel })}
          {preview ? <span className="dc-profile-edit-preview"> - {preview}</span> : null}
        </>
      ),
      panel: error ? <div className="dc-profile-edit-text">{error}</div> : null,
      failed: true
    }
  }

  const change = result.change
  const rejected = change?.rejected ?? []
  const rejectedPanel = rejected.length > 0
    ? <div className="dc-profile-edit-rejected">{t('toolCall.agentBuilder.rejected', { items: rejected.join(', ') })}</div>
    : null

  switch (field) {
    case 'name': {
      const value = stringOf(change?.value) ?? stringOf(args?.name) ?? ''
      return {
        title: value
          ? withSlot(t('toolCall.agentBuilder.name', { value: SLOT }), <ProfileRef icon={Tag} label={value} />)
          : t('agentBuilder.editing.updatedField', { field: fieldLabel }),
        panel: null,
        failed: false
      }
    }
    case 'description': {
      const value = stringOf(change?.value) ?? stringOf(args?.description) ?? ''
      return {
        title: t('toolCall.agentBuilder.description'),
        panel: value ? <div className="dc-profile-edit-text">{value}</div> : null,
        failed: false
      }
    }
    case 'instructions': {
      const appended = change?.op === 'append'
      const text = appended
        ? stringOf(args?.text) ?? stringOf(change?.value) ?? ''
        : stringOf(change?.value) ?? stringOf(args?.text) ?? ''
      return {
        title: t(appended ? 'toolCall.agentBuilder.instructions.append' : 'toolCall.agentBuilder.instructions.set'),
        panel: text.trim()
          ? (
            <div className="dc-profile-edit-markdown">
              <MarkdownRenderer content={text} containOverflow enableMermaid={false} />
            </div>
          )
          : null,
        failed: false
      }
    }
    case 'tools.policy': {
      const mode = stringOf(change?.mode) ?? stringOf(args?.mode) ?? 'all'
      const list = change?.list ?? stringList(args?.names)
      if (mode === 'all') {
        return { title: t('toolCall.agentBuilder.tools.all'), panel: rejectedPanel, failed: false }
      }
      if (list.length === 0) {
        return { title: t('toolCall.agentBuilder.tools.none'), panel: rejectedPanel, failed: false }
      }
      const key = mode === 'denyList' ? 'toolCall.agentBuilder.tools.denyList' : 'toolCall.agentBuilder.tools.allowList'
      return {
        title: withSlot(t(key, { items: SLOT }), titleChips(list, (name) => <ProfileRef key={name} icon={Wrench} label={name} />, locale)),
        panel: list.length > TITLE_CHIP_LIMIT || rejectedPanel
          ? (
            <>
              <div className="dc-profile-edit-chips">
                {list.map((name) => <ProfileRef key={name} icon={Wrench} label={name} />)}
              </div>
              {rejectedPanel}
            </>
          )
          : null,
        failed: false
      }
    }
    case 'tools.agentControl': {
      const value = stringOf(change?.value) ?? stringOf(args?.value) ?? ''
      const label = AGENT_CONTROL_OPTIONS.find((option) => option.value === value)?.label ?? value
      return {
        title: withSlot(t('toolCall.agentBuilder.toolControl', { value: SLOT }), <ProfileRef icon={SlidersHorizontal} label={label} />),
        panel: null,
        failed: false
      }
    }
    case 'skills.preload':
      return listEdit(
        change,
        args,
        'toolCall.agentBuilder.skills',
        (name) => <SkillRef key={name} name={name} />,
        rejectedPanel,
        fieldLabel,
        locale
      )
    case 'mcp.servers':
      return listEdit(
        change,
        args,
        'toolCall.agentBuilder.mcp',
        (name) => <ProfileRef key={name} icon={Server} label={name} />,
        rejectedPanel,
        fieldLabel,
        locale
      )
    case 'providerPreference': {
      if (change?.op === 'remove') {
        return { title: t('toolCall.agentBuilder.model.clear'), panel: null, failed: false }
      }
      const preference = change?.providerPreference ?? preferenceFromArgs(args)
      const model = preference?.model ?? stringOf(args?.model) ?? ''
      return {
        title: model
          ? withSlot(t('toolCall.agentBuilder.model.set', { value: SLOT }), <ProfileRef icon={Cpu} label={model} />)
          : t('agentBuilder.editing.updatedField', { field: fieldLabel }),
        panel: preference ? <ModelDetails preference={preference} locale={locale} /> : null,
        failed: false
      }
    }
    case 'approval': {
      const value = stringOf(change?.value) ?? stringOf(args?.policy) ?? ''
      const label = APPROVAL_OPTIONS.find((option) => option.value === value)?.label ?? value
      const outside = args?.requireApprovalOutsideWorkspace
      return {
        title: label
          ? withSlot(t('toolCall.agentBuilder.approval', { value: SLOT }), <ProfileRef icon={ShieldCheck} label={label} />)
          : t('agentBuilder.editing.updatedField', { field: fieldLabel }),
        panel: typeof outside === 'boolean'
          ? (
            <div className="dc-profile-edit-text">
              {t(outside ? 'toolCall.agentBuilder.outsideWorkspace.required' : 'toolCall.agentBuilder.outsideWorkspace.notRequired')}
            </div>
          )
          : null,
        failed: false
      }
    }
    default:
      return { title: t('agentBuilder.editing.updatedField', { field: fieldLabel }), panel: null, failed: false }
  }
}

function listEdit(
  change: BuilderToolChange | undefined,
  args: Args,
  keyPrefix: string,
  chip: (name: string) => ReactNode,
  rejectedPanel: ReactNode | null,
  fieldLabel: string,
  locale: AppLocale
): EditDisplay {
  const removed = change?.op === 'remove'
  const items = change?.values ?? stringList(args?.names)
  if (items.length === 0) {
    return {
      title: translate(locale, 'agentBuilder.editing.updatedField', { field: fieldLabel }),
      panel: rejectedPanel,
      failed: false
    }
  }
  const key = `${keyPrefix}.${removed ? 'remove' : 'add'}.${items.length === 1 ? 'one' : 'many'}`
  return {
    title: withSlot(translate(locale, key, { items: SLOT }), titleChips(items, chip, locale)),
    panel: items.length > TITLE_CHIP_LIMIT || rejectedPanel
      ? (
        <>
          <div className="dc-profile-edit-chips">{items.map(chip)}</div>
          {rejectedPanel}
        </>
      )
      : null,
    failed: false
  }
}

function titleChips(items: string[], chip: (name: string) => ReactNode, locale: AppLocale): ReactNode {
  const shown = items.slice(0, TITLE_CHIP_LIMIT)
  const rest = items.length - shown.length
  return (
    <span className="dc-profile-edit-inline">
      {shown.map(chip)}
      {rest > 0 ? (
        <span className="dc-profile-edit-more">{translate(locale, 'toolCall.agentBuilder.more', { count: String(rest) })}</span>
      ) : null}
    </span>
  )
}

/** Splices a React node into a translated sentence at the `{{value}}` / `{{items}}` slot. */
function withSlot(template: string, node: ReactNode): ReactNode {
  const parts = template.split(SLOT)
  if (parts.length === 1) return template
  return (
    <>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 ? node : null}
        </span>
      ))}
    </>
  )
}

function ProfileRef({ icon: Icon, label }: { icon: LucideIcon; label: string }): JSX.Element {
  return (
    <span className="dc-ref dc-ref-profile">
      <Icon size={12} strokeWidth={2.25} aria-hidden />
      <span>{label}</span>
    </span>
  )
}

function ModelDetails({ preference, locale }: { preference: AgentProviderPreference; locale: AppLocale }): JSX.Element {
  const effort = preference.reasoning.effort
  const effortKey = `agentBuilder.model.reasoning${effort.charAt(0).toUpperCase()}${effort.slice(1)}`
  const effortLabel = translate(locale, effortKey)
  const rows: Array<[string, string]> = [
    [translate(locale, 'agentBuilder.model.provider'), preference.providerId],
    [translate(locale, 'agentBuilder.model.model'), preference.model],
    [
      translate(locale, 'agentBuilder.model.reasoning'),
      preference.reasoning.enabled
        ? (effortLabel === effortKey ? effort : effortLabel)
        : translate(locale, 'agentBuilder.model.reasoningOff')
    ],
    [
      translate(locale, 'agentBuilder.model.speed'),
      translate(locale, preference.speed === 'fast' ? 'agentBuilder.model.speed.fast' : 'agentBuilder.model.speed.standard')
    ],
    [
      translate(locale, 'agentBuilder.model.contextWindow'),
      translate(locale, preference.contextWindow.mode === 'max' ? 'agentBuilder.model.context.max' : 'agentBuilder.model.context.default')
    ]
  ]
  return (
    <dl className="dc-profile-edit-rows">
      {rows.map(([label, value]) => (
        <div key={label} className="dc-profile-edit-row">
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  )
}

function preferenceFromArgs(args: Args): AgentProviderPreference | null {
  const providerId = stringOf(args?.providerId)
  const model = stringOf(args?.model)
  if (!providerId || !model) return null
  const effort = stringOf(args?.reasoningEffort) ?? 'medium'
  return {
    providerId,
    model,
    reasoning: {
      enabled: args?.reasoningEnabled !== false,
      effort: effort as AgentProviderPreference['reasoning']['effort']
    },
    speed: (stringOf(args?.speed) ?? 'standard') as AgentProviderPreference['speed'],
    contextWindow: { mode: (stringOf(args?.contextWindowMode) ?? 'default') as AgentProviderPreference['contextWindow']['mode'] }
  }
}

function stringOf(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null
}

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((entry): entry is string => typeof entry === 'string' && entry.trim().length > 0) : []
}
