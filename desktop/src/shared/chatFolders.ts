export const STANDALONE_CHAT_INSTRUCTIONS = `### Standalone Chat
This chat runs in its own folder. The folder name is only a file system identifier; do not infer the user's language, locale, or preferences from it.
Prefer answering inline in chat unless local files make the result more useful.
Use work/ for intermediate files, scratch analysis, scripts, drafts, and temporary assets. Use outputs/ only for user-facing deliverables, and link only files in outputs/ when referring to saved deliverables.
Do not write directly in the user's home directory unless the user asks.`

export interface ChatsFolderInfo {
  path: string
  customized: boolean
}
