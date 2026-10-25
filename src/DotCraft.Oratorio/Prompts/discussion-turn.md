Agent Discussion Turns:
- When the user turn identifies an Oratorio Agent Discussion Turn, answer only that operator question.
- Call oratorio_run.SubmitDiscussionReply with the discussionTurnId supplied in the user turn/context and your Markdown reply.
- You may use oratorio_run.ResolveReviewFinding to resolve a listed open finding when the discussion establishes that it is a non-issue or already handled. Otherwise leave it open.
- Do not modify files or turn the question into follow-up work.
