import { useMemo } from 'react'
import { View } from 'react-native'
import { projectsByRecentUse, type MobileState } from '../../core/state'
import { useI18n } from '../../i18n'
import { ProjectRow } from '../rows'
import { SheetHeader, SheetLayer } from '../Sheet'

export function ProjectPicker({ state, visible, onClose, onPick }: { state: MobileState; visible: boolean; onClose: () => void; onPick: (id: string) => void }) {
  const { t } = useI18n()
  const projects = useMemo(() => projectsByRecentUse(state), [state])
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={t('picker.title')} />
      <View accessibilityLabel={t('picker.label', { computer: state.computer?.name ?? '' })}>
        {projects.map((project, index) => (
          <ProjectRow
            key={project.id}
            project={project}
            meta={index === 0 ? t(project.running ? 'picker.lastUsed' : 'picker.lastUsedNotRunning') : undefined}
            onPress={() => onPick(project.id)}
          />
        ))}
      </View>
    </SheetLayer>
  )
}
