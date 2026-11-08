# Calendar

- Use the built-in `<viz-calendar>` for a compact day schedule; use custom HTML for any other calendar view. Supply verified dates and events; the element handles durations, overlap lanes, and responsive layout. Short blocks show only the title; hover, focus, or tap reveals the full title, time, and detail in the shared tooltip. Events that ended before `now` are drawn quieter. It draws its own card, so never wrap it in `.card` or add an inset, and do not stretch the schedule to fit secondary text.
- Attributes: `date` (required, `YYYY-MM-DD`), `start` and `end` (visible window as `HH:MM`, default `09:00`-`17:00`), `now` (current-time marker; only when verified), `time-zone` (short label; does not convert times), `lang` (date locale; defaults to the host language), `empty-label` (defaults to "No events"), and the boolean `interactive`.
- Each event needs `title`, `start`, and `end` as local wall-clock `HH:MM` (`24:00` is allowed as an end). Optional fields are `tone` (`blue`, `green`, `red`, `orange`, `purple`, or `yellow`; default `blue`), `detail` (short secondary text), and `video` (`true` shows a video-call icon). Keep a small, consistent tone mapping per source calendar or category. Convert source timestamps to the requested date and zone first. Supply at most 200 events; events outside the window are clipped or omitted. Invalid data shows an inline error until valid data arrives.
- Use the three hours around the next event for a brief check-in, or the requested range for a full day.
- For a fixed snapshot, put a JSON array in the `events` attribute, escaping apostrophes as `&#39;` inside single quotes. From JavaScript, configure the element before appending it and assign an array to `calendar.events`; assigning a new array redraws, while changing the array in place does not. The property overrides the attribute until it is set to `undefined`. Use the literal `viz-calendar` name; no import or initialization is needed.
- Add `interactive` only when the fragment handles selection. Listen for `eventselect`; `event.detail.event` is the supplied object and `event.detail.index` its position in the input array.
- The element shows one day. Add [Tabs](design-system.md#tabs) outside it only when two or more supplied days exist, and assign the chosen day's `date` and `events` to the same element on selection.

```html
<viz-calendar date="2026-07-14" start="11:00" end="14:00" now="11:50"
  events='[{"title":"Standup","start":"12:00","end":"12:30"},{"title":"Design sync","start":"13:00","end":"14:00","detail":"Room 4","video":true}]'>
</viz-calendar>
```
