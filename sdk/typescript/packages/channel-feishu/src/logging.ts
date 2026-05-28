const PREFIX = "[FeishuAdapter]";

type LogValue = string | number | boolean | null | undefined;
type LogFields = Record<string, LogValue>;

function renderFieldValue(value: LogValue): string {
  if (value === null || value === undefined) return "-";
  return String(value);
}

function formatFields(fields?: LogFields): string {
  if (!fields) return "";
  const pairs = Object.entries(fields)
    .filter(([, value]) => value !== undefined)
    .map(([key, value]) => `${key}=${renderFieldValue(value)}`);
  return pairs.length ? ` ${pairs.join(" ")}` : "";
}

export function shortId(value: string | undefined | null): string {
  if (!value) return "-";
  if (value.length <= 12) return value;
  return `${value.slice(0, 6)}...${value.slice(-4)}`;
}

export function logInfo(evt: string, fields?: LogFields): void {
  const message = `${PREFIX} evt=${evt}${formatFields(fields)}`;
  if (process.env.DOTCRAFT_CHANNEL_TRANSPORT === "stdio") {
    console.error(message);
  } else {
    console.log(message);
  }
}

export function logWarn(evt: string, fields?: LogFields): void {
  console.warn(`${PREFIX} evt=${evt}${formatFields(fields)}`);
}

export function logError(evt: string, fields?: LogFields): void {
  console.error(`${PREFIX} evt=${evt}${formatFields(fields)}`);
}

export function errorMessage(err: unknown): string {
  if (err instanceof Error) return err.message;
  return String(err);
}
