import type { ToolDisclosureProps } from '@dotcraft/plugin'
import { useEffect } from 'react'
import { ToolDisclosure } from '../conversation/ToolDisclosure'
import { useToolCallRevealTarget } from '../../stores/toolCallRevealStore'

export function DesktopPluginToolDisclosure({ icon, ...props }: ToolDisclosureProps): JSX.Element {
  const revealNonce = useToolCallRevealTarget(target => target.callId === props.callId ? target.nonce : null)
  const expandable = props.expandable !== false
  useEffect(() => {
    if (revealNonce != null && expandable && !props.expanded) props.onToggle()
  }, [revealNonce, expandable])
  return <ToolDisclosure {...props} iconNode={icon} />
}
