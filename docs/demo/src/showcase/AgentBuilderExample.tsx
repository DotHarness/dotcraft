import { AgentBuilderView } from '@renderer/components/agents/AgentBuilderView'
import { RefreshIcon } from '@renderer/components/ui/AppIcons'
import { ShowcaseNote } from './notes'
import { lang } from './params'

const START_OVER = { en: 'Start over', zh: '重新开始' }[lang]

function startOver(): void {
  if (window.parent === window) window.location.reload()
  else window.parent.postMessage({ type: 'dotcraft-showcase:restart' }, '*')
}

export function AgentBuilderExample(): JSX.Element {
  return (
    <div className="showcase-builder">
      <AgentBuilderView initialRoute="new" leadingAction={{ label: START_OVER, icon: <RefreshIcon size={16} />, onClick: startOver }} />
      <ShowcaseNote floating />
    </div>
  )
}
