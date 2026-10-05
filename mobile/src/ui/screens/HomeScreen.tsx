import { useRouter } from 'expo-router'
import { useCallback, useMemo, useState } from 'react'
import { Pressable, StyleSheet, TextInput, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useMobileState } from '../../app-state/SessionContext'
import { EMPTY_DRAFT } from '../../core/draft'
import {
  computerStatus,
  homeLists,
  isReachable,
  projectById,
  projectsByRecentUse,
  runningChats,
  type ComputerStatus,
} from '../../core/state'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { animateLayout, CompactComposer, NewChatPane, useProjectStart } from '../chat/NewChat'
import { Screen, ScrollArea } from '../layout'
import { Mascot, MascotNote, MascotTransition, type MascotMoment } from '../mascot/Mascot'
import { MenuRow, PopoverMenu } from '../Menu'
import { ComputerStatusLine, ReadOnlyNotice, RoundIconButton, Section, Txt } from '../parts'
import { ChatRow, chatTitle, ProjectRow, projectTitle } from '../rows'
import { metrics, type, useTheme } from '../theme'
import { PairDifferentSheet } from './SettingsScreen'

function computerMoment(status: ComputerStatus, waiting: number): MascotMoment {
  if (status === 'offline' || status === 'access-off') return 'asleep'
  if (status === 'connecting') return 'look-around'
  return waiting > 0 ? 'question' : 'idle'
}

export function chatHref(key: string) {
  const index = key.indexOf(':')
  return { pathname: '/chat/[projectId]/[threadId]' as const, params: { projectId: key.slice(0, index), threadId: key.slice(index + 1) } }
}

export function HomeScreen() {
  const state = useMobileState()
  const router = useRouter()
  const { t } = useI18n()
  const { colors } = useTheme()
  const [query, setQuery] = useState('')
  const [searching, setSearching] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [replacing, setReplacing] = useState(false)
  const [rowHeight, setRowHeight] = useState(0)
  const [composing, setComposing] = useState<string | null>(null)
  const [draft, setDraft] = useState(EMPTY_DRAFT)
  const insets = useSafeAreaInsets()
  const computer = state.computer
  const status = computerStatus(state)
  const live = isReachable(status)
  const { waiting, recent } = useMemo(() => homeLists(state), [state])
  const visible = useMemo(() => runningChats(state), [state])
  const collapse = useCallback(() => {
    animateLayout()
    setComposing(null)
  }, [])
  useProjectStart(composing)
  if (!computer) return <Screen>{null}</Screen>

  const trimmed = query.trim().toLowerCase()
  const untitled = t('chat.untitled')
  const results = trimmed ? visible.filter((chat) => chatTitle(chat, untitled).toLowerCase().includes(trimmed)) : []
  const nameOf = (projectId: string) => {
    const project = projectById(state, projectId)
    return project ? projectTitle(project, t) : ''
  }
  const openChat = (key: string) => router.push(chatHref(key))
  const menuLabel = t('home.computerMenu', { computer: computer.name })
  const lastProject = projectsByRecentUse(state)[0]
  const canStart = status === 'online' && Boolean(lastProject)
  const closeSearch = () => {
    setSearching(false)
    setQuery('')
  }

  return (
    <Screen>
      <View
        onLayout={({ nativeEvent }) => setRowHeight(nativeEvent.layout.height)}
        style={[styles.top, { backgroundColor: colors.bgPrimary }]}
      >
        {searching ? (
          <View style={styles.searchRow}>
            <View style={[styles.search, { borderColor: colors.accent, backgroundColor: colors.bgSecondary }]}>
              <Icon name="search" size={18} color={colors.textSecondary} />
              <TextInput
                value={query}
                onChangeText={setQuery}
                autoFocus
                placeholder={t('home.search')}
                placeholderTextColor={colors.textDimmed}
                accessibilityLabel={t('home.search')}
                returnKeyType="search"
                autoCorrect={false}
                style={[type.text, styles.searchInput, { color: colors.textPrimary }]}
              />
            </View>
            <RoundIconButton label={t('common.close')} icon="x" onPress={closeSearch} />
          </View>
        ) : (
          <>
            {composing ? (
              <View style={styles.backButton}>
                <RoundIconButton label={t('common.back')} icon="chevronLeft" onPress={collapse} />
              </View>
            ) : null}
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={menuLabel}
              accessibilityState={{ expanded: menuOpen }}
              style={({ pressed }) => [styles.computer, pressed && { backgroundColor: colors.roundFill }]}
              onPress={() => setMenuOpen(true)}
            >
              <Mascot moment={computerMoment(status, waiting.length)} size={40} style={styles.avatar} />
              <View style={styles.computerText}>
                <Txt numberOfLines={1} style={styles.computerName}>
                  {computer.name}
                </Txt>
                <ComputerStatusLine status={status} updatedAt={state.syncedAt} />
              </View>
              <Icon name="chevronDown" size={16} color={colors.textSecondary} strokeWidth={2} />
            </Pressable>
            <View style={styles.searchButton}>
              <RoundIconButton
                label={t('home.search')}
                icon="search"
                onPress={() => {
                  setComposing(null)
                  setSearching(true)
                }}
              />
            </View>
          </>
        )}
      </View>
      {composing ? (
        <NewChatPane
          key={composing}
          projectId={composing}
          draft={draft}
          onDraft={setDraft}
          onPickProject={setComposing}
          onCollapse={collapse}
          onCreated={(key) => {
            setComposing(null)
            openChat(key)
          }}
        />
      ) : (
        <>
          <ScrollArea>
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
                        onPress={() => router.push({ pathname: '/project/[projectId]', params: { projectId: project.id } })}
                      />
                    ))}
                  </Section>
                  {recent.length > 0 ? (
                    <Section title={t('home.recent')}>
                      {recent.map((chat) => (
                        <ChatRow key={chat.key} chat={chat} live={live} projectName={nameOf(chat.projectId)} onPress={() => openChat(chat.key)} />
                      ))}
                    </Section>
                  ) : null}
                </>
              )}
            </View>
          </ScrollArea>
          <CompactComposer
            draft={draft}
            disabled={!canStart}
            onPress={() => {
              if (!lastProject) return
              animateLayout()
              setComposing(lastProject.id)
            }}
          />
        </>
      )}
      <PopoverMenu visible={menuOpen} label={menuLabel} anchor={{ top: insets.top + rowHeight }} onClose={() => setMenuOpen(false)}>
        <MenuRow
          icon="arrowLeftRight"
          label={t('settings.pairDifferent')}
          onPress={() => {
            setMenuOpen(false)
            setReplacing(true)
          }}
        />
        <MenuRow
          icon="settings"
          label={t('common.settings')}
          onPress={() => {
            setMenuOpen(false)
            router.push('/settings')
          }}
        />
      </PopoverMenu>
      <PairDifferentSheet computer={computer.name} visible={replacing} onClose={() => setReplacing(false)} />
    </Screen>
  )
}

const styles = StyleSheet.create({
  top: {
    flexDirection: 'row',
    justifyContent: 'center',
    alignItems: 'center',
    minHeight: 64,
    paddingVertical: 8,
    paddingHorizontal: metrics.gutter + 52,
  },
  backButton: { position: 'absolute', left: metrics.gutter },
  searchButton: { position: 'absolute', right: metrics.gutter },
  searchRow: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: 8, marginHorizontal: -52 },
  computer: {
    flexShrink: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    paddingVertical: 4,
    paddingLeft: 8,
    paddingRight: 12,
    borderRadius: metrics.heroRadius,
  },
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
