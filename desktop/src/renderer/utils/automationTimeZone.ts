import type { AutomationSchedule } from "../types/automation";

const calendarScheduleKinds = new Set<AutomationSchedule["kind"]>([
  "daily",
  "weekdays",
  "weekly",
]);

export function resolveSystemTimeZone(): string {
  try {
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (timeZone) {
      new Intl.DateTimeFormat("en", { timeZone }).format();
      return timeZone;
    }
  } catch {
    // UTC keeps calendar schedules valid when the host cannot report an IANA zone.
  }
  return "UTC";
}

export function ensureScheduleTimeZone(
  schedule: AutomationSchedule,
): AutomationSchedule {
  if (!calendarScheduleKinds.has(schedule.kind) || schedule.timeZone)
    return schedule;
  return { ...schedule, timeZone: resolveSystemTimeZone() };
}
