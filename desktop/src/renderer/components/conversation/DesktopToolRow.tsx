import type { JSX } from 'react'
import type { AppLocale } from '../../../shared/locales'
import { openThread } from '../../utils/desktopNavigation'
import { openThread as openChatInProject } from '../sidebar/projectConnectionActions'
import { usePluginStore } from '../../stores/pluginStore'
import { pluginTitle } from '../plugins/PluginCatalogItem'
import {
  formatDesktopToolLabel,
  getDesktopToolIcon,
  getDesktopToolTargetPlugin,
  getDesktopToolTargetProjectPath,
  getDesktopToolTargetThreadId,
  type DesktopToolOperation
} from '../../utils/desktopToolDisplay'
import type { ConversationItem } from '../../types/conversation'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ToolDisclosure } from './ToolDisclosure'
import styles from './DesktopToolRow.module.css'

export function DesktopToolRow({ item, operation, running, success, locale }: {
  item: ConversationItem
  operation: DesktopToolOperation
  running: boolean
  success: boolean
  locale: AppLocale
}): JSX.Element {
  const targetPlugin = getDesktopToolTargetPlugin(operation, item.arguments, item.structuredResult)
  const listedPlugin = usePluginStore((state) => targetPlugin
    ? state.plugins.find((plugin) => plugin.installed && plugin.id === targetPlugin.id)
      ?? state.plugins.find((plugin) => plugin.id === targetPlugin.id)
    : undefined)
  const pluginName = targetPlugin && (targetPlugin.name ?? (listedPlugin ? pluginTitle(listedPlugin) : targetPlugin.id))
  const label = formatDesktopToolLabel(operation, running ? 'active' : success ? 'completed' : 'failed', locale, pluginName)
  const threadId = running || success ? getDesktopToolTargetThreadId(operation, item.arguments) : null
  const followsChat = operation === 'followChat' || operation === 'unfollowChat'
  const project = followsChat
    ? (item.structuredResult as { projectId?: string } | undefined)?.projectId ?? item.arguments?.project
    : undefined
  const projectPath = !running && success ? getDesktopToolTargetProjectPath(operation, item.structuredResult) : null
  const title = running
    ? <RunningShimmer className="dc-tool-row-running-label">{label}</RunningShimmer>
    : label
  return (
    <ToolDisclosure
      expanded={false}
      onToggle={() => undefined}
      expandable={false}
      callId={item.toolCallId}
      icon={{ icon: getDesktopToolIcon(operation) }}
      title={threadId
        ? <button
          type="button"
          className={styles.link}
          onClick={() => void (followsChat
            ? openChatInProject(threadId, typeof project === 'string' ? project : undefined)
            : openThread(threadId))}
        >{title}</button>
        : projectPath
          ? <button type="button" className={styles.link} onClick={() => void window.api.workspace.switch(projectPath, undefined, { existingProjectOnly: true })}>{title}</button>
          : title}
    />
  )
}
