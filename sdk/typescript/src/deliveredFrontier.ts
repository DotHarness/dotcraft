/**
 * Computes how much of {@link currentText} is already covered by {@link deliveredText}
 * for progressive segment delivery. Used by {@link ChannelAdapter.consumeTurnEventStream}.
 *
 * Prefer the first occurrence of a matching suffix so repeated substrings (e.g. "abc" in
 * "abcabc") do not advance the frontier past the first delivered span.
 */

export function getDeliveredFrontier(currentText: string, deliveredText: string): number {
  if (!currentText || !deliveredText) return 0;
  if (currentText.startsWith(deliveredText)) return deliveredText.length;
  if (deliveredText.endsWith(currentText)) return currentText.length;
  const maxProbe = Math.min(currentText.length, deliveredText.length);
  for (let len = maxProbe; len > 0; len -= 1) {
    const suffix = deliveredText.slice(deliveredText.length - len);
    const idx = currentText.indexOf(suffix);
    if (idx >= 0) return idx + len;
  }
  return 0;
}
