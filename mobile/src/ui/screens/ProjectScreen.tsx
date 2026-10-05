import { useRouter } from 'expo-router'
import { useCallback, useMemo, useState } from 'react'
import { ScrollView, StyleSheet, View } from 'react-native'
import { useMobileState } from '../../app-state/SessionContext'
import { EMPTY_DRAFT } from '../../core/draft'
import { computerStatus, projectById, projectChats } from '../../core/state'
import { useI18n } from '../../i18n'
import { BAR_HEIGHT, ChatBar } from '../chat/ChatBar'
import { animateLayout, CompactComposer, NewChatPane, useProjectStart } from '../chat/NewChat'
import { Screen } from '../layout'
import { MascotNote, MascotTransition } from '../mascot/Mascot'
import { Section } from '../parts'
import { ChatRow, projectTitle } from '../rows'
import { metrics } from '../theme'
import { chatHref } from './HomeScreen'

export function ProjectScreen({ projectId, compose = false }: { projectId: string; compose?: boolean }) {
  const state = useMobileState()
  const router = useRouter()
  const { t } = useI18n()
  const [composing, setComposing] = useState(compose)
  const [draft, setDraft] = useState(EMPTY_DRAFT)
  const project = projectById(state, projectId)
  const phase = state.phases[projectId]
  const chats = useMemo(() => projectChats(state, projectId), [state, projectId])
  const computer = state.computer
  const status = computerStatus(state)
  const online = status === 'online'
  const needsStart = useProjectStart(projectId)
  const collapse = useCallback(() => {
    animateLayout()
    setComposing(false)
  }, [])

  if (!computer || !project) return <Screen>{null}</Screen>
  const projectName = projectTitle(project, t)
  const bar = (
    <ChatBar title={projectName} computer={computer.name} status={status} onBack={composing ? collapse : () => router.back()} />
  )

  if (composing) {
    return (
      <Screen>
        <NewChatPane
          key={projectId}
          projectId={projectId}
          draft={draft}
          overlay={bar}
          onDraft={setDraft}
          onPickProject={(id) => router.setParams({ projectId: id })}
          onCollapse={collapse}
          onCreated={(key) => router.replace(chatHref(key))}
        />
      </Screen>
    )
  }

  return (
    <Screen>
      <View style={styles.fill}>
        {needsStart || phase === 'starting' ? (
          <MascotTransition line={t('project.starting', { project: projectName })} />
        ) : phase === 'cantStart' ? (
          <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: projectName })}</MascotNote>
        ) : chats.length > 0 ? (
          <ScrollView style={styles.fill} contentContainerStyle={styles.list}>
            <Section title={t('home.recent')} grow>
              {chats.map((chat) => (
                <ChatRow key={chat.key} chat={chat} live={online} projectName={null} onPress={() => router.push(chatHref(chat.key))} />
              ))}
            </Section>
          </ScrollView>
        ) : (
          <View style={[styles.fill, styles.empty]}>
            <MascotNote moment="greeting">
              {project.running
                ? t('newChat.runsOn', { computer: computer.name, project: projectName })
                : t('newChat.startsOn', { computer: computer.name, project: projectName })}
            </MascotNote>
          </View>
        )}
        {bar}
      </View>
      <CompactComposer
        draft={draft}
        disabled={!online || phase === 'cantStart'}
        onPress={() => {
          animateLayout()
          setComposing(true)
        }}
      />
    </Screen>
  )
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  empty: { paddingHorizontal: metrics.gutter },
  list: { flexGrow: 1, paddingTop: BAR_HEIGHT + 20, paddingHorizontal: metrics.gutter, paddingBottom: 16 },
})
