import { InlineKeyboard } from "grammy";
import type { CommandChoice, CommandInfo } from "@dotcraft/channel";

const TELEGRAM_COMMAND_NAME = /^[a-z0-9_]{1,32}$/;
const MAX_MENU_COMMANDS = 100;
const MAX_PENDING_CHOICE_SETS = 200;

export type TelegramMenuScope =
  | { type: "default" }
  | { type: "all_private_chats" }
  | { type: "all_group_chats" }
  | { type: "chat"; chat_id: number };

export interface TelegramCommandMenu {
  scope: TelegramMenuScope;
  commands: Array<{ command: string; description: string }>;
}

export function buildTelegramCommandMenus(commands: CommandInfo[], adminUserIds: Iterable<string>): TelegramCommandMenu[] {
  const everyone = new Map<string, string>();
  const admins = new Map<string, string>();
  for (const item of commands) {
    const name = String(item.name ?? "").trim().replace(/^\//, "").toLowerCase();
    if (!TELEGRAM_COMMAND_NAME.test(name) || admins.has(name)) continue;
    const description = String(item.description || item.fallbackDescription || name).trim().slice(0, 256) || name;
    admins.set(name, description);
    if (!item.requiresAdmin) everyone.set(name, description);
  }
  const toMenu = (entries: Map<string, string>) =>
    [...entries].slice(0, MAX_MENU_COMMANDS).map(([command, description]) => ({ command, description }));

  const menus: TelegramCommandMenu[] = [
    { scope: { type: "default" }, commands: toMenu(everyone) },
    { scope: { type: "all_private_chats" }, commands: toMenu(everyone) },
    { scope: { type: "all_group_chats" }, commands: toMenu(everyone) },
  ];
  for (const id of adminUserIds) {
    const chatId = Number(id);
    if (Number.isSafeInteger(chatId) && chatId > 0) {
      menus.push({ scope: { type: "chat", chat_id: chatId }, commands: toMenu(admins) });
    }
  }
  return menus;
}

export function stripBotMention(text: string, botUsername: string | undefined): string {
  if (!botUsername) return text;
  const match = /^(\s*\/[^\s@]+)@(\S+)(.*)$/s.exec(text);
  if (!match || match[2]!.toLowerCase() !== botUsername.toLowerCase()) return text;
  return `${match[1]}${match[3]}`;
}

export interface PendingCommandChoices {
  chatId: number;
  choices: CommandChoice[];
}

export class TelegramCommandChoices {
  private readonly pending = new Map<string, PendingCommandChoices>();
  private nextToken = 1;

  keyboard(chatId: number, choices: CommandChoice[]): InlineKeyboard {
    const token = `c${this.nextToken++}`;
    this.pending.set(token, { chatId, choices });
    while (this.pending.size > MAX_PENDING_CHOICE_SETS) {
      this.pending.delete(this.pending.keys().next().value!);
    }
    const keyboard = new InlineKeyboard();
    choices.forEach((choice, index) => {
      keyboard.text((choice.label || choice.command || "").slice(0, 64), `cmd:${token}:${index}`).row();
    });
    return keyboard;
  }

  take(token: string, chatId: number, index: number): CommandChoice | undefined {
    const entry = this.pending.get(token);
    const choice = entry?.chatId === chatId ? entry.choices[index] : undefined;
    if (choice) this.pending.delete(token);
    return choice;
  }
}
