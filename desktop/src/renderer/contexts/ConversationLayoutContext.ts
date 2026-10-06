import { createContext, useContext } from 'react'

export const ConversationTargetWidthContext = createContext<number | null>(null)

export function useConversationTargetWidth(): number | null {
  return useContext(ConversationTargetWidthContext)
}
