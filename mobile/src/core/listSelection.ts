import type { ComputerState } from './state'

export function chatListState(state: ComputerState) {
  return {
    computer: state.computer,
    accessOff: state.accessOff,
    link: state.link,
    identityChanged: state.identityChanged,
    syncing: state.syncing,
    projects: state.projects,
    phases: state.phases,
    chats: state.chats,
  }
}
