import { SidebarBrand } from './SidebarBrand'
import { ThreadSearch } from './ThreadSearch'
import { useBrandPartner } from './useBrandPartner'
import css from './SidebarHeader.module.css'

export function SidebarHeader({ workspaceName }: { workspaceName: string }): JSX.Element {
  return (
    <div className={css.header}>
      <SidebarBrand partner={useBrandPartner()} />
      <ThreadSearch workspaceName={workspaceName} />
    </div>
  )
}
