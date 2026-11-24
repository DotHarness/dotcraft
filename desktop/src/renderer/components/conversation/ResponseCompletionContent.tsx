import { Fragment } from 'react'
import { TurnArtifacts } from './TurnArtifacts'
import { TurnAutomations } from './TurnAutomations'
import { TurnCompletionSummary } from './TurnCompletionSummary'
import { TurnNextStep } from './TurnNextStep'
import { TurnThreadActions } from './TurnThreadActions'

export function ResponseCompletionContent({ threadId, turnIds, lastTurnId }: {
  threadId: string
  turnIds: readonly string[]
  lastTurnId: string
}): JSX.Element {
  return (
    <>
      {turnIds.map((turnId) => <Fragment key={turnId}>
        <TurnThreadActions turnId={turnId} />
        <TurnAutomations turnId={turnId} />
      </Fragment>)}
      <TurnArtifacts turnIds={turnIds} />
      <TurnCompletionSummary turnIds={turnIds} threadId={threadId} />
      <TurnNextStep turnId={lastTurnId} />
    </>
  )
}
