/**
 * Convert markdown-heavy agent output to plain text for Weixin.
 */

export function markdownToPlainText(text: string): string {
  let result = text;
  result = result.replace(/```[^\n]*\n?([\s\S]*?)```/g, (_, code: string) => code.trim());
  result = result.replace(/!\[[^\]]*\]\([^)]*\)/g, "");
  result = result.replace(/\[([^\]]+)\]\([^)]*\)/g, "$1");
  result = result.replace(/^\|[\s:|-]+\|$/gm, "");
  result = result.replace(/^\|(.+)\|$/gm, (_, inner: string) =>
    inner
      .split("|")
      .map((c: string) => c.trim())
      .join("  "),
  );
  result = result.replace(/\*\*([^*]+)\*\*/g, "$1");
  result = result.replace(/\*([^*]+)\*/g, "$1");
  result = result.replace(/^#+\s*/gm, "");
  return result.trim();
}
