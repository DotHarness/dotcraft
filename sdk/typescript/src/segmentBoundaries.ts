/**
 * Returns whether an item/started event should flush the current streamed text segment.
 */
export function shouldFlushSegmentOnItemStarted(itemType: string): boolean {
  return itemType === "toolCall" || itemType === "pluginFunctionCall";
}
