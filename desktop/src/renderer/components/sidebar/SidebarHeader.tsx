import { DotCraftLogo } from '../ui/DotCraftLogo'
import { ThreadSearch } from './ThreadSearch'
import css from './SidebarHeader.module.css'

export function SidebarHeader({ workspaceName }: { workspaceName: string }): JSX.Element {
  return (
    <div className={css.header}>
      <span className={css.brand}>
        <span className={css.mark} aria-hidden="true">
          <DotCraftLogo size={18} />
        </span>
        <span className={css.wordmark}>DotCraft</span>
      </span>
      <ThreadSearch workspaceName={workspaceName} />
    </div>
  )
}
