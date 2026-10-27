# Phone app

Keep up with DotCraft from your phone while you're away from your desk. The agent keeps running on your computer, and the phone app shows what's running, puts approvals and questions in front of you, and starts new chats in your projects.

<LiveTour feature="phone" :steps="[
  { example: 'unpaired', chapter: 'pair', title: 'Pair your phone', body: 'Scan the QR code from Settings on your computer, then allow the phone on the computer. In the demo, tap the viewfinder to scan.' },
  { example: 'home', chapter: 'home', title: 'See what is running', body: 'The home screen lists your projects and recent chats. Chats waiting on you are marked.' },
  { example: 'home', chapter: 'approval', title: 'Approve from anywhere', body: 'Open a chat waiting on approval and choose Allow once, Allow for session, or Reject.' },
  { example: 'home', chapter: 'question', title: 'Answer its questions', body: 'When the agent asks something, pick an option or type a reply.' }
]" />

## Install the app

On your phone, download `DotCraft-v<version>-android.apk` from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) and open it to install. The first time, Android asks you to allow your browser or file manager to install apps.

The app is available for Android only for now.

## Pair your phone

1. On your computer, open DotCraft Desktop and go to **Settings → Connections → Phones**.
2. Turn on **Allow phones to control this computer**.
3. Select **Add phone**. A QR code appears. It works once and expires in 10 minutes.
4. Open DotCraft on your phone and scan the code.
5. Back on the computer, check that it names your phone and choose **Allow**.

The phone then appears under **Paired phones**. A paired phone can do anything you can do in DotCraft on that computer, so pair only your own phones. Remove a phone from the same list when you stop using it.

The phone needs to be on the same network as the computer, or on the same private network, such as Tailscale. To use it on other networks, see [Access from anywhere](#access-from-anywhere).

## Follow and answer your chats

The app's home screen lists your projects and recent chats across all running projects. A chat waiting on an approval or a question stays in the list, marked **Needs approval** or **Needs answer**.

- **Follow** a running chat and see its progress live.
- **Approve** a command or file change with **Allow once**, **Allow for session**, or **Reject**.
- **Answer** the agent's questions by picking an option or typing a reply.
- **Add a message** while a chat is running, or **Stop** it.
- **Start a new chat** in any project on the computer. A project that isn't running starts when you send.

The phone and the computer show the same chats, so a chat started on one continues on the other. Answer an approval on one, and it clears on the other. Models, settings, and plugins stay on the computer, and the phone doesn't change them.

## Keep going in the background

When you leave the app while a chat is running or waiting on you, DotCraft keeps a live session and shows one ongoing notification, such as "2 running · 1 needs you". A new approval arrives as a notification with **Allow once** and **Reject**, so you can answer without opening the app. New questions and finished chats notify you too.

The live session ends on its own after two minutes with nothing running or waiting, or when you choose **Disconnect** on the ongoing notification. The app asks for notification permission the first time a chat runs while it's open. Without it, the app disconnects in the background.

## Access from anywhere

At home or in the office, the phone connects to the computer directly over your network. To use it on other networks, run a relay on a server that both can reach, then enter the relay address and token on the computer under **Settings → Connections → Phones → Access from anywhere**. The relay only passes encrypted data along and never sees your chats.

Phones paired earlier pick up the relay the next time they're on the same network as the computer. To run the relay, see [Access from anywhere](../contribute/lifecycle/hub#access-from-anywhere) in the Hub guide.
