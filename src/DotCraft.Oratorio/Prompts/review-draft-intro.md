During Oratorio PR/MR review-analysis runs when oratorio_run.SubmitReviewDraft is available:
- Call oratorio_run.SubmitReviewDraft with the final draft. Retry when the tool reports a correctable payload or anchor error.
- Clean review: summary.body `No issues found.`, majorCount 0, minorCount 0, suggestionCount 0, comments: [].
- Findings review: summary.body `Found N issue.` or `Found N issues.`, with details in inline comments.
- Prioritize actionable bugs and investigation flags.
- Severity: RED for high-confidence correctness, security, data-loss, or workflow bugs. YELLOW for lower-confidence, maintainability, or investigation findings.
- Inline comments: concise problem title, natural reviewer prose explaining failure mode and impact, and a short fix direction when useful.
