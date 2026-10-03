import { useRouter } from 'expo-router'
import { useEffect, useMemo } from 'react'
import { StyleSheet, View } from 'react-native'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { computerStatus, isReachable, projectById, projectChats } from '../../core/state'
import { useI18n } from '../../i18n'
import { BottomBar, Hero, Screen, ScrollArea, TopBar } from '../layout'
import { MascotNote, MascotTransition } from '../mascot/Mascot'
import { BackButton, PhoneButton, Txt } from '../parts'
import { ChatRow } from '../rows'
import { type } from '../theme'
import { chatHref } from './HomeScreen'

export function ProjectScreen({ projectId }: { projectId: string }) {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const project = projectById(state, projectId)
  const computer = state.computer
  const chats = useMemo(() => projectChats(state, projectId), [state, projectId])
  const status = computerStatus(state)
  const live = isReachable(status)
  const phase = state.phases[projectId]
  const online = status === 'online'
  const needsStart = Boolean(project && !project.running && online && phase !== 'cantStart')

  useEffect(() => {
    if (needsStart && phase !== 'starting') void session.startProject(projectId)
  }, [needsStart, phase, projectId, session])

  if (!project || !computer) return <Screen>{null}</Screen>
  const starting = needsStart || phase === 'starting'
  const body = starting ? (
    <MascotTransition inList line={t('project.starting', { project: project.name })} />
  ) : !project.running ? (
    <MascotNote moment="asleep">{t('project.cantStart', { computer: computer.name, project: project.name })}</MascotNote>
  ) : chats.length === 0 ? (
    <MascotNote moment="idle">{t('project.empty', { project: project.name })}</MascotNote>
  ) : (
    chats.map((chat) => <ChatRow key={chat.key} chat={chat} live={live} projectName={null} onPress={() => router.push(chatHref(chat.key))} />)
  )

  return (
    <Screen>
      <TopBar>
        <BackButton onPress={() => router.back()} />
      </TopBar>
      <ScrollArea>
        <Hero>
          <Txt accessibilityRole="header" numberOfLines={1} style={type.title}>
            {project.name}
          </Txt>
        </Hero>
        <View style={styles.section}>{body}</View>
      </ScrollArea>
      <BottomBar>
        <PhoneButton
          icon="squarePen"
          style={styles.wide}
          disabled={!online || starting}
          onPress={() => router.push({ pathname: '/new/[projectId]', params: { projectId } })}
        >
          {t('project.newChat', { project: project.name })}
        </PhoneButton>
      </BottomBar>
    </Screen>
  )
}

const styles = StyleSheet.create({
  section: { marginTop: 26 },
  wide: { flex: 1 },
})
