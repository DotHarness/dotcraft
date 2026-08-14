/** Compose distinct AgentMessage items as Markdown blocks without changing text within each item. */
export function composeTranscriptMarkdown(replyParts: readonly string[]): string {
  const parts = replyParts.filter((part) => part.trim().length > 0);
  if (parts.length === 0) return "";

  let transcript = parts[0];
  for (const part of parts.slice(1)) {
    const trailingNewlines = countBoundaryNewlines(transcript, "end");
    const leadingNewlines = countBoundaryNewlines(part, "start");
    transcript += "\n".repeat(Math.max(0, 2 - trailingNewlines - leadingNewlines));
    transcript += part;
  }
  return transcript;
}

function countBoundaryNewlines(text: string, side: "start" | "end"): number {
  const match = side === "start"
    ? text.match(/^(?:[\t ]*\n)+/)
    : text.match(/(?:\n[\t ]*)+$/);
  return match?.[0].match(/\n/g)?.length ?? 0;
}
