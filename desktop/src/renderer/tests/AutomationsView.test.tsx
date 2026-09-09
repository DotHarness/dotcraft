import { beforeEach, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { AutomationsView } from "../components/automations/AutomationsView";
import { LocaleProvider } from "../contexts/LocaleContext";
import { useAutomationsStore } from "../stores/automationsStore";
import { useConnectionStore } from "../stores/connectionStore";
import { useThreadStore } from "../stores/threadStore";
import { useUIStore } from "../stores/uiStore";
import { AUTOMATION_TASK_DRAG_MIME } from "../utils/automationDrag";
import { installDesktopApiMock } from "./desktopApiMock";
import type { AutomationDefinition, AutomationRun } from "../types/automation";

const automation: AutomationDefinition = {
  id: "a",
  version: 1,
  name: "Review changes",
  prompt: "Review recent changes",
  status: "active",
  executionMode: "independent",
  workspaceMode: "project",
  approvalPolicy: "workspaceScope",
  notificationPolicy: "important",
  schedule: { kind: "daily", hour: 9, minute: 0, timeZone: "UTC" },
  createdAt: "2026-09-08T00:00:00Z",
  updatedAt: "2026-09-08T00:00:00Z",
};
const presets = [
  {
    id: "daily-summary",
    name: "Daily summary",
    prompt: "Summarize changes.",
    schedule: { kind: "weekdays" as const, hour: 8, minute: 0 },
  },
];

beforeEach(() => {
  useConnectionStore.setState({ status: "connected", connectionEpoch: 0 });
  useAutomationsStore.setState({
    automations: [],
    runs: {},
    presets: [],
    loading: false,
    error: null,
    selectedAutomationId: null,
  });
  useThreadStore.setState({ activeThreadId: null, threadList: [] });
  useUIStore.setState({ activeMainView: "automations" });
  installDesktopApiMock({
    settings: { get: async () => ({ locale: "en" }) },
    appServer: { onNotification: () => () => {},
      sendRequest: vi.fn(async (method: string) => {
        if (method === "automation/list") return { automations: [automation] };
        if (method === "automation/read") return { automation };
        if (method === "automation/presets/list") return { presets };
        if (method === "automation/runs/list") return { runs: [] };
        return {};
      }),
    },
  });
});

it("loads automations after reconnecting", async () => {
  const send = vi.fn(async (method: string) => {
    if (method === "automation/list") return { automations: [automation] };
    if (method === "automation/presets/list") return { presets };
    if (method === "automation/runs/list") return { runs: [] };
    return {};
  });
  installDesktopApiMock({
    settings: { get: async () => ({ locale: "en" }) },
    appServer: { sendRequest: send, onNotification: () => () => {} },
  });
  useConnectionStore.setState({ status: "disconnected", connectionEpoch: 0 });

  render(<LocaleProvider><AutomationsView /></LocaleProvider>);
  expect(send).not.toHaveBeenCalledWith("automation/list", {});

  act(() => useConnectionStore.getState().setStatus({ status: "connected" }));

  await screen.findByRole("button", { name: /Review changes/ });
  expect(send).toHaveBeenCalledWith("automation/list", {});
  expect(send).toHaveBeenCalledWith("automation/presets/list", { locale: "en" });
});

it("lists definitions before suggestions, retains them in detail, and exposes the drag payload", async () => {
  render(
    <LocaleProvider>
      <AutomationsView />
    </LocaleProvider>,
  );
  const automationButton = await screen.findByRole("button", {
    name: /Review changes/,
  });
  const suggestionsHeading = screen.getByRole("heading", {
    name: "Suggestions",
  });
  expect(
    automationButton.compareDocumentPosition(suggestionsHeading) &
      Node.DOCUMENT_POSITION_FOLLOWING,
  ).toBeTruthy();

  const payload = new Map<string, string>();
  const dataTransfer = {
    setData: (type: string, value: string) => payload.set(type, value),
    effectAllowed: "",
  };
  fireEvent.dragStart(automationButton.closest("article")!, { dataTransfer });
  expect(payload.get(AUTOMATION_TASK_DRAG_MIME)).toBe("a");

  fireEvent.click(automationButton);
  await waitFor(() =>
    expect(screen.getByLabelText("Details")).toBeInTheDocument(),
  );
  expect(
    screen.getByRole("button", { name: /Review changes/ }),
  ).toBeInTheDocument();
  expect(
    screen.getByRole("heading", { name: "Suggestions" }),
  ).toBeInTheDocument();
});

it("leads with suggestions when there are no definitions", async () => {
  installDesktopApiMock({
    settings: { get: async () => ({ locale: "en" }) },
    appServer: { onNotification: () => () => {},
      sendRequest: vi.fn(async (method: string) =>
        method === "automation/presets/list"
          ? { presets }
          : { automations: [] },
      ),
    },
  });
  render(
    <LocaleProvider>
      <AutomationsView />
    </LocaleProvider>,
  );
  const suggestion = await screen.findByRole("button", { name: /Daily summary/ });
  expect(suggestion).not.toHaveTextContent(/\([A-Za-z_]+\/[A-Za-z_]+\)|\(UTC\)/);
  expect(
    screen.queryByRole("heading", { name: "Your automations" }),
  ).not.toBeInTheDocument();
});

it("adds a suggestion directly without leaving the automations surface", async () => {
  let created: AutomationDefinition | null = null;
  const send = vi.fn(async (method: string, params: any) => {
    if (method === "automation/list") return { automations: [] };
    if (method === "automation/presets/list") return { presets };
    if (method === "automation/create") {
      created = { ...automation, ...params.automation, id: "created-from-preset" };
      return { automation: created };
    }
    return { runs: [] };
  });
  installDesktopApiMock({ settings: { get: async () => ({ locale: "en" }) }, appServer: { sendRequest: send, onNotification: () => () => {} } });
  render(<LocaleProvider><AutomationsView /></LocaleProvider>);

  fireEvent.click(await screen.findByRole("button", { name: /Daily summary/ }));

  await screen.findByRole("button", { name: /Daily summary/ });
  await waitFor(() => expect(created).not.toBeNull());
  const createCall = send.mock.calls.find(([method]) => method === "automation/create");
  expect(createCall?.[1].automation).toMatchObject({
    name: "Daily summary", prompt: "Every weekday morning, summarize important changes in this project.", status: "active",
    executionMode: "independent", approvalPolicy: "workspaceScope", notificationPolicy: "all"
  });
  expect(createCall?.[1].automation.schedule.timeZone).toBeTruthy();
  expect(screen.getByRole("heading", { name: "Scheduled tasks" })).toBeInTheDocument();
  await waitFor(() => expect(screen.getAllByRole("button", { name: /Daily summary/ })).toHaveLength(1));
  expect(useUIStore.getState().activeMainView).toBe("automations");
  expect(useAutomationsStore.getState().selectedAutomationId).toBeNull();
});

it("exposes row quick actions and keeps command pending separate from running state", async () => {
  let releaseRun!: () => void;
  const runPending = new Promise<void>(resolve => { releaseRun = resolve; });
  const unreadRun: AutomationRun = {
    id: "unread", automationId: automation.id, definitionVersion: 1, status: "succeeded",
    createdAt: "2026-09-08T01:00:00Z", completedAt: "2026-09-08T01:01:00Z", deliveryStatus: "sent"
  };
  const send = vi.fn(async (method: string) => {
    if (method === "automation/list") return { automations: [automation] };
    if (method === "automation/presets/list") return { presets: [] };
    if (method === "automation/runs/list") return { runs: [unreadRun] };
    if (method === "automation/run") {
      await runPending;
      return { run: { ...unreadRun, id: "queued", status: "queued" } };
    }
    return { automation };
  });
  installDesktopApiMock({ settings: { get: async () => ({ locale: "en" }) }, appServer: { sendRequest: send, onNotification: () => () => {} } });
  render(<LocaleProvider><AutomationsView /></LocaleProvider>);

  const task = (await screen.findByRole("button", { name: /Review changes/ })).closest("article")!;
  await waitFor(() => expect(within(task).getByLabelText("Unread")).toBeInTheDocument());
  fireEvent.click(within(task).getByRole("button", { name: "Automation actions" }));
  expect(screen.getByRole("menuitem", { name: "Run now" })).toBeInTheDocument();
  expect(screen.getByRole("menuitem", { name: "Pause" })).toBeInTheDocument();
  expect(screen.getByRole("menuitem", { name: "Delete" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("menuitem", { name: "Run now" }));
  await waitFor(() => expect(within(task).getByRole("button", { name: "Pause" })).toBeDisabled());
  expect(within(task).queryByLabelText("Running")).not.toBeInTheDocument();
  releaseRun();
  await waitFor(() => expect(send).toHaveBeenCalledWith("automation/run", { automationId: "a" }));
});

it("prioritizes unread tasks and keeps schedule state distinct from execution state", async () => {
  const active = { ...automation, id: "active", name: "Active task", nextRunAt: "2026-09-10T09:00:00Z" };
  const paused = { ...automation, id: "paused", name: "Paused task", status: "paused" as const, nextRunAt: null };
  const completed = { ...automation, id: "completed", name: "Completed task", status: "completed" as const, nextRunAt: null };
  const executing = { ...automation, id: "executing", name: "Executing task" };
  const unread: AutomationRun = { id: "unread", automationId: paused.id, definitionVersion: 1, status: "succeeded", createdAt: "2026-09-08T01:00:00Z", deliveryStatus: "sent" };
  const running: AutomationRun = { ...unread, id: "running", automationId: executing.id, status: "running", readAt: "2026-09-08T01:02:00Z" };
  const send = vi.fn(async (method: string, params: any) => {
    if (method === "automation/list") return { automations: [completed, active, paused, executing] };
    if (method === "automation/presets/list") return { presets: [] };
    if (method === "automation/runs/list") return { runs: params.automationId === paused.id ? [unread] : params.automationId === executing.id ? [running] : [] };
    return { automation: [completed, active, paused, executing].find(item => item.id === params.automationId) };
  });
  installDesktopApiMock({ settings: { get: async () => ({ locale: "en" }) }, appServer: { sendRequest: send, onNotification: () => () => {} } });
  const { container } = render(<LocaleProvider><AutomationsView /></LocaleProvider>);

  const pausedMain = await screen.findByRole("button", { name: /Paused task/ });
  await waitFor(() => expect(within(pausedMain.closest("article")!).getByLabelText("Unread")).toBeInTheDocument());
  const articles = [...container.querySelectorAll(".dc-automation-list-rows article")];
  expect(articles[0]).toContainElement(pausedMain);
  expect(within(pausedMain.closest("article")!).getByRole("button", { name: "Resume" })).toBeInTheDocument();
  expect(pausedMain.querySelector("small")).toHaveTextContent(/Daily/);
  expect(pausedMain.querySelector("small")).not.toHaveTextContent(/Next run|Paused/);

  const executingRow = screen.getByRole("button", { name: /Executing task/ }).closest("article")!;
  expect(within(executingRow).getByLabelText("Running")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /Executing task/ }).querySelector("small")).toHaveTextContent(/Running/);
  const completedRow = screen.getByRole("button", { name: /Completed task/ }).closest("article")!;
  expect(within(completedRow).getByLabelText("Completed")).toBeInTheDocument();
});

it("opens previous runs from the row and keeps the attached chat action in the footer", async () => {
  const attachedAutomation: AutomationDefinition = {
    ...automation,
    executionMode: "thread",
    targetThreadId: "target-thread",
  };
  const run: AutomationRun = {
    id: "run-1",
    automationId: attachedAutomation.id,
    definitionVersion: attachedAutomation.version,
    status: "succeeded",
    createdAt: new Date().toISOString(),
    threadId: "run-thread",
    turnId: "run-turn",
    deliveryStatus: "sent",
  };
  installDesktopApiMock({
    settings: { get: async () => ({ locale: "en" }) },
    appServer: { onNotification: () => () => {},
      sendRequest: vi.fn(async (method: string) => {
        if (method === "automation/list") return { automations: [attachedAutomation] };
        if (method === "automation/read") return { automation: attachedAutomation };
        if (method === "automation/presets/list") return { presets: [] };
        if (method === "automation/runs/list") return { runs: [run] };
        if (method === "thread/read") return { thread: { id: "run-thread", displayName: "Review changes", status: "active", turns: [] } };
        return {};
      }),
    },
  });

  render(
    <LocaleProvider>
      <AutomationsView />
    </LocaleProvider>,
  );
  fireEvent.click(await screen.findByRole("button", { name: /Review changes/ }));

  const runRow = await screen.findByRole("button", {
    name: /Review changes, Succeeded/
  });
  await waitFor(() => expect(runRow).toHaveAttribute('aria-disabled', 'false'));
  expect(screen.queryByText("Open run conversation")).not.toBeInTheDocument();
  fireEvent.click(runRow);
  expect(useThreadStore.getState().activeThreadId).toBe("run-thread");

  fireEvent.click(screen.getByRole("button", { name: "Open chat" }));
  expect(useThreadStore.getState().activeThreadId).toBe("target-thread");
  expect(useUIStore.getState().activeMainView).toBe("conversation");
});

it('synchronizes pause and resume between the list and details, preserving dirty drafts', async () => {
  let saved = { ...automation }
  const send = vi.fn(async (method: string, params: any) => {
    if (method === 'automation/list') return { automations: [saved] }
    if (method === 'automation/read') return { automation: saved }
    if (method === 'automation/update') { saved = { ...saved, ...params.automation, version: saved.version + 1 }; return { automation: saved } }
    return method === 'automation/presets/list' ? { presets: [] } : { runs: [] }
  })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, appServer: { sendRequest: send, onNotification: () => () => {} } })
  render(<LocaleProvider><AutomationsView /></LocaleProvider>)
  fireEvent.click(await screen.findByRole('button', { name: /Review changes/ }))
  const details = screen.getByRole('complementary', { name: 'Details' })
  fireEvent.click(within(details).getByRole('button', { name: 'Pause' }))
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'Resume' })).toHaveLength(2))
  expect(within(details).getByText('Paused')).toBeInTheDocument()
  const row = screen.getByRole('button', { name: /Review changes/ }).closest('article')!
  fireEvent.click(within(row).getByRole('button', { name: 'Resume' }))
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'Pause' })).toHaveLength(2))
  fireEvent.change(screen.getByLabelText('Automation name'), { target: { value: 'Unsaved title' } })
  for (const button of screen.getAllByRole('button', { name: 'Pause' })) expect(button).toBeDisabled()
  expect(screen.getByLabelText('Automation name')).toHaveValue('Unsaved title')
  expect(send.mock.calls.some(([method]) => method === 'automation/run')).toBe(false)
})
