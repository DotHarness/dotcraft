import { useRouter } from 'expo-router'
import { useMemo, useState } from 'react'
import { Pressable, StyleSheet, TextInput, View } from 'react-native'
import { useMobileState } from '../../app-state/SessionContext'
import {
  computerStatus,
  homeLists,
  isReachable,
  projectById,
  projectsByRecentUse,
  runningChats,
  stateOf,
  type ComputerStatus,
  type MobileState,
} from '../../core/state'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { BottomBar, Screen, ScrollArea } from '../layout'
import { Mascot, MascotNote, MascotTransition, type MascotMoment } from '../mascot/Mascot'
import { ComputerStatusLine, PhoneButton, ReadOnlyNotice, RoundIconButton, Section, Txt } from '../parts'
import { ChatRow, chatTitle, ProjectRow } from '../rows'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'

function computerMoment(status: ComputerStatus, waiting: number): MascotMoment {
  if (status === 'offline' || status === 'access-off') return 'asleep'
  if (status === 'connecting') return 'look-around'
  return waiting > 0 ? 'question' : 'idle'
}

export function chatHref(key: string) {
  const index = key.indexOf(':')
  return { pathname: '/chat/[projectId]/[threadId]' as const, params: { projectId: key.slice(0, index), threadId: key.slice(index + 1) } }
}

function ProjectPicker({ state, visible, onClose, onPick }: { state: MobileState; visible: boolean; onClose: () => void; onPick: (id: string) => void }) {
  const { t } = useI18n()
  const projects = useMemo(() => projectsByRecentUse(state), [state])
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={t('picker.title')} onClose={onClose} />
      <View accessibilityLabel={t('picker.label', { computer: state.computer?.name ?? '' })}>
        {projects.map((project, index) => (
          <ProjectRow
            key={project.id}
            project={project}
            meta={index === 0 ? t(project.running ? 'picker.lastUsed' : 'picker.lastUsedNotRunning') : undefined}
            running={false}
            live
            onPress={() => onPick(project.id)}
          />
        ))}
      </View>
    </SheetLayer>
  )
}

export function HomeScreen() {
  const state = useMobileState()
  const router = useRouter()
  const { t } = useI18n()
  const { colors } = useTheme()
  const [query, setQuery] = useState('')
  const [focused, setFocused] = useState(false)
  const [picking, setPicking] = useState(false)
  const [scrolled, setScrolled] = useState(false)
  const computer = state.computer
  const status = computerStatus(state)
  const live = isReachable(status)
  const { waiting, recent } = useMemo(() => homeLists(state), [state])
  const visible = useMemo(() => runningChats(state), [state])
  if (!computer) return <Screen>{null}</Screen>

  const trimmed = query.trim().toLowerCase()
  const untitled = t('chat.untitled')
  const results = trimmed ? visible.filter((chat) => chatTitle(chat, untitled).toLowerCase().includes(trimmed)) : []
  const nameOf = (projectId: string) => projectById(state, projectId)?.name ?? ''
  const openChat = (key: string) => router.push(chatHref(key))
  const openSettings = () => router.push('/settings')

  return (
    <Screen>
      <View style={[styles.top, { backgroundColor: colors.bgPrimary, borderBottomColor: scrolled ? colors.borderDefault : 'transparent' }]}>
        <Pressable accessibilityRole="button" style={styles.computer} onPress={openSettings}>
          <Mascot moment={computerMoment(status, waiting.length)} size={40} style={styles.avatar} />
          <View style={styles.computerText}>
            <Txt numberOfLines={1} style={styles.computerName}>
              {computer.name}
            </Txt>
            <ComputerStatusLine status={status} updatedAt={state.syncedAt} />
          </View>
        </Pressable>
        <RoundIconButton label={t('common.settings')} icon="settings" onPress={openSettings} />
      </View>
      <ScrollArea onScroll={({ nativeEvent }) => setScrolled(nativeEvent.contentOffset.y > 0)}>
        {status === 'access-off' ? <ReadOnlyNotice status={status} computer={computer.name} style={styles.notice} /> : null}
        <View style={styles.lists}>
          {state.syncing ? (
            <MascotTransition line={t('home.catchingUp', { computer: computer.name })} />
          ) : trimmed ? (
            <Section title={t('home.results')} grow>
              {results.length > 0 ? (
                results.map((chat) => (
                  <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                ))
              ) : (
                <MascotNote moment="curious">{t('home.noResults', { query: query.trim() })}</MascotNote>
              )}
            </Section>
          ) : (
            <>
              {waiting.length > 0 ? (
                <Section title={t('home.needsYou')}>
                  {waiting.map((chat) => (
                    <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                  ))}
                </Section>
              ) : null}
              <Section title={t('home.projects')}>
                {state.projects.map((project) => (
                  <ProjectRow
                    key={project.id}
                    project={project}
                    running={visible.some((chat) => chat.projectId === project.id && stateOf(chat) === 'running')}
                    live={live}
                    onPress={() => router.push({ pathname: '/project/[projectId]', params: { projectId: project.id } })}
                  />
                ))}
              </Section>
              <Section title={t('home.recent')} grow>
                {visible.length === 0 ? (
                  <MascotNote moment="content">{t('home.empty', { computer: computer.name })}</MascotNote>
                ) : (
                  recent.map((chat) => (
                    <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                  ))
                )}
              </Section>
            </>
          )}
        </View>
      </ScrollArea>
      <BottomBar>
        <View style={[styles.search, { borderColor: focused ? colors.accent : colors.borderDefault, backgroundColor: colors.bgSecondary }]}>
          <Icon name="search" size={18} color={colors.textSecondary} />
          <TextInput
            value={query}
            onChangeText={setQuery}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
            placeholder={t('home.search')}
            placeholderTextColor={colors.textDimmed}
            accessibilityLabel={t('home.search')}
            returnKeyType="search"
            autoCorrect={false}
            style={[type.text, styles.searchInput, { color: colors.textPrimary }]}
          />
        </View>
        <PhoneButton icon="squarePen" disabled={status !== 'online'} onPress={() => setPicking(true)}>
          {t('home.newChat')}
        </PhoneButton>
      </BottomBar>
      <ProjectPicker
        state={state}
        visible={picking}
        onClose={() => setPicking(false)}
        onPick={(projectId) => {
          setPicking(false)
          router.push({ pathname: '/new/[projectId]', params: { projectId } })
        }}
      />
    </Screen>
  )
}

const styles = StyleSheet.create({
  top: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingVertical: 8,
    paddingLeft: metrics.gutter,
    paddingRight: 12,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  computer: { flex: 1, minWidth: 0, flexDirection: 'row', alignItems: 'center', gap: 12 },
  avatar: { marginLeft: -4, transform: [{ translateY: -3 }] },
  computerText: { flexShrink: 1, minWidth: 0, gap: 1 },
  computerName: { fontWeight: '600' },
  notice: { marginTop: 8 },
  lists: { flexGrow: 1, marginTop: -12 },
  search: {
    flex: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    height: metrics.touch,
    paddingHorizontal: 16,
    borderWidth: 1,
    borderRadius: metrics.pill,
  },
  searchInput: { flex: 1, minWidth: 0, padding: 0, outlineWidth: 0 },
})
