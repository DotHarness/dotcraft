import { useMemo } from "react";
import { useLocale } from "../../contexts/LocaleContext";
import { Select } from "./Select";

const MINUTES_PER_DAY = 24 * 60;
const SLOT_MINUTES = 15;

export function TimeSelect({
  hour,
  minute,
  ariaLabel,
  disabled = false,
  onChange,
}: {
  hour: number;
  minute: number;
  ariaLabel: string;
  disabled?: boolean;
  onChange(hour: number, minute: number): void;
}): JSX.Element {
  const locale = useLocale();
  const value = timeValue(hour, minute);
  const options = useMemo(() => {
    const values = Array.from(
      { length: MINUTES_PER_DAY / SLOT_MINUTES },
      (_, index) => {
        const total = index * SLOT_MINUTES;
        return timeValue(Math.floor(total / 60), total % 60);
      },
    );
    if (!values.includes(value)) values.push(value);
    return values
      .sort()
      .map((option) => ({ value: option, label: formatTime(option, locale) }));
  }, [locale, value]);

  return (
    <Select
      appearance="frameless"
      adaptiveWidth={false}
      disabled={disabled}
      menuMaxHeight={320}
      value={value}
      options={options}
      ariaLabel={ariaLabel}
      valueProps={{ className: "dc-time-select-value" }}
      onValueChange={(next) => {
        const [nextHour, nextMinute] = next.split(":").map(Number);
        onChange(nextHour, nextMinute);
      }}
    />
  );
}

function timeValue(hour: number, minute: number): string {
  return `${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}`;
}

function formatTime(value: string, locale: string): string {
  const [hour, minute] = value.split(":").map(Number);
  try {
    return new Intl.DateTimeFormat(locale, {
      hour: "numeric",
      minute: "2-digit",
    }).format(new Date(2000, 0, 1, hour, minute));
  } catch {
    return value;
  }
}
