const paragraphSeparators = ["\r\n\r\n", "\n\n"];
const lineSeparators = ["\r\n", "\n"];
const sentenceSeparators = ["。", ". ", "！", "! ", "？", "? "];

export const WeComTextMaxBytes = 2048;
export const WeComMarkdownMaxBytes = 4096;
export const WeComInterChunkDelayMs = 200;

export function splitWeComMessage(content: string, maxBytes: number): string[] {
  if (!content) return [content ?? ""];
  if (byteLength(content) <= maxBytes) return [content];

  const chunks: string[] = [];
  let remaining = content;

  while (remaining.length > 0) {
    if (byteLength(remaining) <= maxBytes) {
      chunks.push(remaining);
      break;
    }

    const index = findSplitIndex(remaining, maxBytes);
    chunks.push(remaining.slice(0, index).trimEnd());
    remaining = remaining.slice(index).trimStart();
  }

  return chunks;
}

function findSplitIndex(text: string, maxBytes: number): number {
  let lo = 0;
  let hi = text.length;
  while (lo < hi) {
    const mid = lo + Math.floor((hi - lo + 1) / 2);
    if (byteLength(text.slice(0, mid)) <= maxBytes) lo = mid;
    else hi = mid - 1;
  }

  const limit = lo;
  return (
    findSeparator(text, limit, paragraphSeparators) ??
    findSeparator(text, limit, lineSeparators) ??
    findSeparator(text, limit, sentenceSeparators) ??
    findSpace(text, limit) ??
    limit
  );
}

function findSeparator(text: string, limit: number, separators: string[]): number | null {
  const minAcceptable = Math.floor(limit / 4);
  let best = -1;
  const searchArea = text.slice(0, limit);
  for (const separator of separators) {
    const pos = searchArea.lastIndexOf(separator);
    if (pos >= minAcceptable) best = Math.max(best, pos + separator.length);
  }
  return best > 0 ? best : null;
}

function findSpace(text: string, limit: number): number | null {
  const pos = text.slice(0, limit).lastIndexOf(" ");
  return pos > Math.floor(limit / 4) ? pos + 1 : null;
}

function byteLength(text: string): number {
  return Buffer.byteLength(text, "utf-8");
}

