# Computer use

With computer use, DotCraft works the apps on your Windows computer the way you do: it reads what a window shows, clicks, types, and presses keyboard shortcuts. Reach for it when a task has to happen inside a desktop app, such as tidying a sheet in Excel, filling in a form in an internal tool, or replying in Outlook.

<LiveTour feature="computer-use" :steps="[
  { example: 'cu-sheet', chapter: 'approval', title: 'Allow an app', body: 'The first time DotCraft needs an app, it asks you. Pick an option to let it go on.' },
  { example: 'cu-form', chapter: 'operate', title: 'Watch it work', body: 'It looks at the window, takes one action, and checks the result before the next one.' },
  { example: 'cu-sheet', chapter: 'stopped', title: 'Stop it', body: 'Press Esc and it stops at once, then tells you what it finished and what is left.' },
  { example: 'cu-mail', chapter: 'confirm', title: 'Confirm first', body: 'Before it sends, deletes, or pays for anything, it asks you.' }
]" />

## Turn on computer use

Computer use is available in DotCraft Desktop on Windows.

1. Open **Settings → Computer use**.
2. Turn on **Any app**.

## Ask DotCraft to use an app

Describe the task in a conversation. DotCraft works on the desktop only when the task needs an app's own interface. When a file, a command, or the browser can do the job, it uses those instead. To point it at the desktop explicitly, mention `$computer` in your message:

```text
$computer Open Notepad and write a short to-do list for today.
```

DotCraft goes one step at a time: it looks at the window, acts once, then looks again to confirm the result before the next step. It operates only the apps the task needs, leaves unrelated windows alone, and doesn't close your apps on its own.

## Allow an app

The first time DotCraft wants to use an app, it comes to the front, opens the conversation, and asks you, for example **Allow DotCraft to use Excel?**

| Choice | Result |
|---|---|
| **Always allow** | DotCraft stops asking before it uses this app. |
| **Allow for this conversation** | DotCraft can use the app in this conversation and asks again in others. |
| **Reject** | DotCraft doesn't use the app. It won't try that app again unless you ask. |

Apps you always allow are listed under **Settings → Computer use → Always-allowed apps**. Remove one there, and DotCraft asks again the next time it wants that app.

## Stop it at any time

While DotCraft works, a bar at the top of the screen reads **DotCraft is using your computer · Esc to cancel**, and a glow outlines the screen. Press **Esc** to stop it immediately. Locking your computer stops it too.

After a stop, DotCraft doesn't touch the computer again in that turn. It tells you what it finished and what's left, so you can decide how to continue. If locking the computer stopped it, unlock and ask it to carry on.

## When DotCraft checks with you

Some actions are hard to undo or act on your behalf. DotCraft asks right before the final step, even when you asked for the task earlier:

- Sending, posting, or replying, including emails, messages, comments, and meeting invitations
- Deleting or overwriting data that isn't easy to restore
- Purchases, payments, subscriptions, or any money transfer
- Installing or running newly downloaded software, extensions, or add-ins
- Changing sharing, permissions, or account settings
- Submitting forms that contain personal, financial, or health information

DotCraft hands you the steps meant for a person: entering or changing passwords, one-time codes, and other credentials, and getting past security warnings or CAPTCHAs. Opening and reading apps and documents, editing what you asked it to edit, and saving to the location you chose need no extra question.

Text that appears in apps, documents, and web pages is information to DotCraft, not instructions. Only you can authorize an action.

## Apps and actions it stays away from

DotCraft doesn't operate password managers, security or antivirus software, sign-in and credential dialogs, or DotCraft itself. It doesn't type commands into terminals or the Run dialog, change security, privacy, account, or system protection settings, or answer system permission prompts for you. When a sign-in screen or a permission prompt blocks the task, it stops and asks you to handle it.

## Websites and Chrome

For websites, DotCraft uses its browser tools rather than computer use. To let it work in your own Chrome, with the accounts you're signed in to and the extensions you've installed, turn on **Chrome** on the same Settings page, then select **Manage** to check the connection.
