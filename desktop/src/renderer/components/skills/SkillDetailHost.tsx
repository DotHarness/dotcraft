import { useEffect, useState } from 'react'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useSkillDetailStore } from '../../stores/skillDetailStore'
import { useSkillsStore, type SkillEntry } from '../../stores/skillsStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { stripYamlFrontmatter } from '../../utils/skillMarkdown'
import { SkillDetailDialog } from './SkillDetailDialog'
import { stageSkillTryInChat } from './skillDraft'

export function SkillDetailHost(): JSX.Element | null {
  const name = useSkillDetailStore((state) => state.name)
  const close = useSkillDetailStore((state) => state.close)
  const activeMainView = useUIStore((state) => state.activeMainView)
  const activeThreadId = useThreadStore((state) => state.activeThreadId)

  useEffect(() => close, [activeMainView, activeThreadId, close])

  return name ? <SkillDetail key={name} name={name} onClose={close} /> : null
}

function SkillDetail({ name, onClose }: { name: string; onClose(): void }): JSX.Element {
  const listed = useSkillsStore((state) => state.skills.find((skill) => skill.name === name))
  const [content, setContent] = useState<string | null>(null)
  const [error, setError] = useState(false)
  const skill: SkillEntry = listed ?? { name, description: '', source: 'workspace', enabled: true, path: '' }

  useEffect(() => {
    if (!useSkillsStore.getState().skills.some((entry) => entry.name === name)) void useSkillsStore.getState().fetchSkills()
    let current = true
    window.api.appServer.sendRequest('skills/view', { name })
      .then((result) => { if (current) setContent((result as { content?: string }).content ?? '') })
      .catch(() => { if (current) setError(true) })
    return () => { current = false }
  }, [name])

  return (
    <LayerBoundary blocksNativeViews>
      <SkillDetailDialog
        skill={skill}
        markdownBody={content != null ? stripYamlFrontmatter(content) : ''}
        loading={content == null && !error}
        error={error}
        onClose={onClose}
        onTryInChat={() => {
          onClose()
          stageSkillTryInChat(skill)
        }}
      />
    </LayerBoundary>
  )
}
