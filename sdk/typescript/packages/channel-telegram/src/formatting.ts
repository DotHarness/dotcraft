export function markdownToTelegramHtml(text: string): string {
  if (!text) {
    return "";
  }

  const codeBlocks: string[] = [];
  const inlineCodes: string[] = [];
  const linkHrefs: string[] = [];

  let output = text.replace(/```[\w]*\n?([\s\S]*?)```/g, (_match, code: string) => {
    codeBlocks.push(code);
    return `\u0000CB${codeBlocks.length - 1}\u0000`;
  });

  output = output.replace(/`([^`]+)`/g, (_match, code: string) => {
    inlineCodes.push(code);
    return `\u0000IC${inlineCodes.length - 1}\u0000`;
  });

  output = output.replace(/^#{1,6}\s+(.+)$/gm, "$1");
  output = output.replace(/^>\s*(.*)$/gm, "$1");
  output = output.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
  output = output.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_match, linkText: string, href: string) => {
    linkHrefs.push(href);
    return `<a href="\u0000LK${linkHrefs.length - 1}\u0000">${linkText}</a>`;
  });
  output = output.replace(/\*\*(.+?)\*\*/g, "<b>$1</b>");
  output = output.replace(/__(.+?)__/g, "<b>$1</b>");
  output = output.replace(/(?<![a-zA-Z0-9])_([^_]+)_(?![a-zA-Z0-9])/g, "<i>$1</i>");
  output = output.replace(/~~(.+?)~~/g, "<s>$1</s>");
  output = output.replace(/^[-*]\s+/gm, "• ");

  for (let index = 0; index < inlineCodes.length; index += 1) {
    const escaped = escapeHtml(inlineCodes[index] ?? "");
    output = output.replace(`\u0000IC${index}\u0000`, `<code>${escaped}</code>`);
  }

  for (let index = 0; index < codeBlocks.length; index += 1) {
    const escaped = escapeHtml(codeBlocks[index] ?? "");
    output = output.replace(`\u0000CB${index}\u0000`, `<pre><code>${escaped}</code></pre>`);
  }

  for (let index = 0; index < linkHrefs.length; index += 1) {
    output = output.replace(`\u0000LK${index}\u0000`, linkHrefs[index] ?? "");
  }

  return output;
}

export function splitTelegramMessage(content: string, maxLength = 4000): string[] {
  if (content.length <= maxLength) {
    return [content];
  }

  const chunks: string[] = [];
  let remaining = content;

  while (remaining.length > 0) {
    if (remaining.length <= maxLength) {
      chunks.push(remaining);
      break;
    }

    const cut = remaining.slice(0, maxLength);
    let position = cut.lastIndexOf("\n\n");
    if (position === -1) {
      position = cut.lastIndexOf("\n");
    }
    if (position === -1) {
      position = cut.lastIndexOf(" ");
    }
    if (position === -1) {
      position = maxLength;
    }

    chunks.push(remaining.slice(0, position).trimEnd());
    remaining = remaining.slice(position).trimStart();
  }

  return chunks;
}

function escapeHtml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}
