const CARD_TEXT_LIMIT = 3200;

export function normalizeMarkdownForFeishu(markdown: string): string {
  return normalizeMarkdownForFeishuV2(markdown);
}

export function normalizeMarkdownForFeishuV2(markdown: string): string {
  let text = markdown.replace(/\r\n/g, "\n").replace(/\t/g, "  ");
  text = text.replace(/([^\n])```/g, "$1\n```");
  text = text.replace(/```([^\n])/g, "```\n$1");

  const rawLines = text.split("\n");
  const normalizedLines: string[] = [];
  let inFence = false;
  for (const rawLine of rawLines) {
    const line = rawLine.replace(/\s+$/g, "");
    if (isFenceDelimiter(line)) {
      inFence = !inFence;
      normalizedLines.push(line.trim());
      continue;
    }
    if (!inFence) {
      normalizedLines.push(normalizeHeadingLine(line));
    } else {
      normalizedLines.push(line);
    }
  }

  const withTableSpacing = addTableSpacing(normalizedLines);
  return withTableSpacing.join("\n").replace(/\n{3,}/g, "\n\n").trim();
}

export function chunkMarkdown(markdown: string, limit = CARD_TEXT_LIMIT): string[] {
  const text = normalizeMarkdownForFeishuV2(markdown);
  if (!text) return [];
  if (text.length <= limit) return [text];

  const blocks = splitMarkdownBlocks(text);
  const chunks: string[] = [];
  let current = "";

  for (const block of blocks) {
    if (!block.trim()) continue;
    const candidate = current ? `${current}\n\n${block}` : block;
    if (candidate.length <= limit) {
      current = candidate;
      continue;
    }
    if (current) chunks.push(current);
    if (block.length <= limit) {
      current = block;
      continue;
    }
    const splitBlocks = splitOversizedBlock(block, limit);
    if (splitBlocks.length === 0) continue;
    chunks.push(...splitBlocks.slice(0, -1));
    current = splitBlocks[splitBlocks.length - 1];
  }
  if (current) chunks.push(current);
  return chunks;
}

export function summarizeApprovalOperation(
  approvalType: string,
  operation: string,
  target: string,
): string {
  if (approvalType === "shell") {
    return `Command: \`${operation}\``;
  }
  return `Operation: \`${operation}\`\nTarget: \`${target || "(not provided)"}\``;
}

function normalizeHeadingLine(line: string): string {
  return line.replace(/^(\s{0,3})(#{1,6})([^\s#].*)$/, "$1$2 $3");
}

function isFenceDelimiter(line: string): boolean {
  return /^\s*```/.test(line);
}

function isTableRow(line: string): boolean {
  return /^\s*\|.*\|\s*$/.test(line);
}

function isTableSeparator(line: string): boolean {
  const trimmed = line.trim();
  if (!trimmed.includes("|")) return false;
  const core = trimmed.replace(/^\|/, "").replace(/\|$/, "");
  const cells = core.split("|").map((cell) => cell.trim());
  if (cells.length < 2) return false;
  return cells.every((cell) => /^:?-{3,}:?$/.test(cell));
}

function addTableSpacing(lines: string[]): string[] {
  const out: string[] = [];
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i];
    if (isTableRow(line) && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
      if (out.length > 0 && out[out.length - 1].trim() !== "") out.push("");
      out.push(line);
      out.push(lines[i + 1]);
      i += 1;
      while (i + 1 < lines.length && isTableRow(lines[i + 1])) {
        out.push(lines[i + 1]);
        i += 1;
      }
      if (i + 1 < lines.length && lines[i + 1].trim() !== "") out.push("");
      continue;
    }
    out.push(line);
  }
  return out;
}

function splitMarkdownBlocks(text: string): string[] {
  const lines = text.split("\n");
  const blocks: string[] = [];
  let i = 0;

  while (i < lines.length) {
    while (i < lines.length && lines[i].trim() === "") i += 1;
    if (i >= lines.length) break;

    if (isFenceDelimiter(lines[i])) {
      const blockLines: string[] = [lines[i]];
      i += 1;
      while (i < lines.length) {
        blockLines.push(lines[i]);
        if (isFenceDelimiter(lines[i])) {
          i += 1;
          break;
        }
        i += 1;
      }
      blocks.push(blockLines.join("\n").trim());
      continue;
    }

    if (isTableRow(lines[i]) && i + 1 < lines.length && isTableSeparator(lines[i + 1])) {
      const blockLines: string[] = [lines[i], lines[i + 1]];
      i += 2;
      while (i < lines.length && isTableRow(lines[i])) {
        blockLines.push(lines[i]);
        i += 1;
      }
      blocks.push(blockLines.join("\n").trim());
      continue;
    }

    if (/^\s*>/.test(lines[i])) {
      const blockLines: string[] = [lines[i]];
      i += 1;
      while (i < lines.length && lines[i].trim() !== "" && /^\s*>/.test(lines[i])) {
        blockLines.push(lines[i]);
        i += 1;
      }
      blocks.push(blockLines.join("\n").trim());
      continue;
    }

    const blockLines: string[] = [lines[i]];
    i += 1;
    while (
      i < lines.length &&
      lines[i].trim() !== "" &&
      !isFenceDelimiter(lines[i]) &&
      !(isTableRow(lines[i]) && i + 1 < lines.length && isTableSeparator(lines[i + 1]))
    ) {
      blockLines.push(lines[i]);
      i += 1;
    }
    blocks.push(blockLines.join("\n").trim());
  }

  return blocks.filter((block) => block.length > 0);
}

function splitOversizedBlock(block: string, limit: number): string[] {
  if (block.length <= limit) return [block];
  const lines = block.split("\n");
  const chunks: string[] = [];
  let current = "";

  for (const line of lines) {
    const append = current ? `${current}\n${line}` : line;
    if (append.length <= limit) {
      current = append;
      continue;
    }
    if (current) chunks.push(current);
    if (line.length <= limit) {
      current = line;
      continue;
    }
    let start = 0;
    while (start < line.length) {
      const end = Math.min(start + limit, line.length);
      chunks.push(line.slice(start, end));
      start = end;
    }
    current = "";
  }

  if (current) chunks.push(current);
  return chunks.filter((chunk) => chunk.trim().length > 0);
}
