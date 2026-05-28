/**
 * Helpers to build the final assistant reply from turn/completed snapshots and streamed deltas.
 * turn/completed includes full turn items; delta aggregation can miss early chunks if handlers
 * attach after turn/start returns (race). Prefer the snapshot when it is at least as long as deltas.
 */

/**
 * Concatenates text from all agentMessage items in turn/completed params (wire order).
 */
export function extractAgentReplyTextFromTurnCompletedParams(
  params: Record<string, unknown> | null | undefined,
): string {
  return extractAgentReplyTextsFromTurnCompletedParams(params).join("");
}

/**
 * Extracts text from each agentMessage item in turn/completed params (wire order).
 */
export function extractAgentReplyTextsFromTurnCompletedParams(
  params: Record<string, unknown> | null | undefined,
): string[] {
  if (!params) return [];
  const turn = params.turn as Record<string, unknown> | undefined;
  if (!turn) return [];
  const items = turn.items;
  if (!Array.isArray(items)) return [];
  const parts: string[] = [];
  for (const raw of items) {
    if (!raw || typeof raw !== "object") continue;
    const item = raw as Record<string, unknown>;
    if (item.type !== "agentMessage") continue;
    const payload = item.payload as Record<string, unknown> | undefined;
    if (!payload) continue;
    const text = payload.text;
    if (typeof text === "string" && text.length > 0) parts.push(text);
  }
  return parts;
}

function commonPrefixLength(left: string, right: string): number {
  const max = Math.min(left.length, right.length);
  let idx = 0;
  while (idx < max && left[idx] === right[idx]) idx += 1;
  return idx;
}

/**
 * Like {@link commonPrefixLength}, but when the first word after the last space differs between
 * strings (e.g. "Hello streamed" vs "Hello snapshot"), roll back to that space so we do not treat a
 * shared first letter ("s") as part of a safe prefix — otherwise snapshot tails become truncated
 * (e.g. "napshot" instead of "snapshot").
 */
function commonPrefixLengthForMerge(left: string, right: string): number {
  const raw = commonPrefixLength(left, right);
  if (raw === 0) return 0;
  const lastSpace = left.lastIndexOf(" ", raw - 1);
  if (lastSpace < 0) return raw;
  const afterSpace = lastSpace + 1;
  const w1 = (left.slice(afterSpace).split(/\s/)[0] ?? "") as string;
  const w2 = (right.slice(afterSpace).split(/\s/)[0] ?? "") as string;
  if (w1 === w2) return raw;
  const wordLcp = commonPrefixLength(w1, w2);
  if (wordLcp === 0) return afterSpace;
  if (wordLcp === 1 && w1.length > 1 && w2.length > 1) return afterSpace;
  return afterSpace + wordLcp;
}

/** Set by {@link configureTextMergeDebug}; only `true` enables merge stderr traces. */
let textMergeDebugOverride: boolean | undefined;

/**
 * Configure whether `mergeReplyTextFromDeltaAndSnapshot` emits stderr traces.
 * Pass `true` to enable; `false` or `undefined` to disable.
 */
export function configureTextMergeDebug(enabled: boolean | undefined): void {
  textMergeDebugOverride = enabled;
}

function isDebugTextMergeEnabled(): boolean {
  return textMergeDebugOverride === true;
}

function debugTextMerge(message: string, data: Record<string, unknown>): void {
  if (!isDebugTextMergeEnabled()) return;
  console.error(`[dotcraft-sdk:text-merge] ${message}`, data);
}

/**
 * Merge streamed deltas with the authoritative snapshot from the server.
 * When neither string contains the other but they share a common prefix, append the non-overlapping
 * tail from the other side so unique text is not dropped (pick-longer-only loses one branch).
 */
export function mergeReplyTextFromDeltaAndSnapshot(deltaText: string, snapshotText: string): string {
  if (!deltaText) {
    debugTextMerge("empty_delta", { snapshotChars: snapshotText.length });
    return snapshotText;
  }
  if (!snapshotText) {
    debugTextMerge("empty_snapshot", { deltaChars: deltaText.length });
    return deltaText;
  }
  if (deltaText === snapshotText) return snapshotText;
  if (snapshotText.includes(deltaText)) return snapshotText;
  if (deltaText.includes(snapshotText)) return deltaText;

  const cp = commonPrefixLengthForMerge(deltaText, snapshotText);
  if (cp > 0) {
    const snapTail = snapshotText.slice(cp).trim();
    const deltaTail = deltaText.slice(cp).trim();
    if (snapTail && !deltaText.includes(snapTail)) {
      const merged = `${deltaText.trimEnd()}\n\n${snapTail}`;
      debugTextMerge("appended_snapshot_tail", {
        commonPrefixLen: cp,
        deltaChars: deltaText.length,
        snapshotChars: snapshotText.length,
        mergedChars: merged.length,
      });
      return merged;
    }
    if (deltaTail && !snapshotText.includes(deltaTail)) {
      const merged = `${snapshotText.trimEnd()}\n\n${deltaTail}`;
      debugTextMerge("appended_delta_tail", {
        commonPrefixLen: cp,
        deltaChars: deltaText.length,
        snapshotChars: snapshotText.length,
        mergedChars: merged.length,
      });
      return merged;
    }
  }

  const picked = snapshotText.length >= deltaText.length ? snapshotText : deltaText;
  debugTextMerge("fallback_longer", {
    commonPrefixLen: cp,
    deltaChars: deltaText.length,
    snapshotChars: snapshotText.length,
    pickedChars: picked.length,
  });
  return picked;
}
