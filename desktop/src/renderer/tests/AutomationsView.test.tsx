import { beforeEach, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { AutomationsView } from "../components/automations/AutomationsView";
import { LocaleProvider } from "../contexts/LocaleContext";
import { useAutomationsStore } from "../stores/automationsStore";
import { useConnectionStore } from "../stores/connectionStore";
import { AUTOMATION_TASK_DRAG_MIME } from "../utils/automationDrag";
import { installDesktopApiMock } from "./desktopApiMock";
import type { AutomationDefinition } from "../types/automation";

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
  useConnectionStore.setState({ status: "connected" });
  useAutomationsStore.setState({
    automations: [],
    runs: {},
    presets: [],
    loading: false,
    error: null,
    selectedAutomationId: null,
  });
  installDesktopApiMock({
    settings: { get: async () => ({ locale: "en" }) },
    appServer: {
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
    appServer: {
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
