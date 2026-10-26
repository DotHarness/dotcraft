import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "telegram-standard",
  channelName: "telegram",
  displayName: "Telegram",
  interface: {
    shortDescription: "Send and receive messages through a BotFather bot.",
    localizedShortDescription: {
      en: "Send and receive messages through a BotFather bot.",
      "zh-Hans": "通过 BotFather 机器人收发消息。",
      ja: "BotFather のボットでメッセージを送受信します。",
      ko: "BotFather 봇으로 메시지를 주고받아요.",
      es: "Envía y recibe mensajes a través de un bot de BotFather.",
      fr: "Envoyez et recevez des messages via un bot BotFather.",
      de: "Senden und empfangen Sie Nachrichten über einen BotFather-Bot.",
    },
    longDescription:
      "Message the bot in Telegram and DotCraft replies in the same chat. Replies can include formatted text, images, files, audio, and video.",
    localizedLongDescription: {
      en: "Message the bot in Telegram and DotCraft replies in the same chat. Replies can include formatted text, images, files, audio, and video.",
      "zh-Hans": "在 Telegram 中给机器人发消息，DotCraft 会在同一会话中回复。回复可以包含格式文本、图片、文件、音频和视频。",
      ja: "Telegram でボットにメッセージを送ると、DotCraft が同じチャットで返信します。返信には、書式付きテキスト、画像、ファイル、音声、動画を含められます。",
      ko: "Telegram에서 봇에게 메시지를 보내면 DotCraft가 같은 채팅에서 답장해요. 답장에는 서식이 있는 텍스트, 이미지, 파일, 오디오, 동영상을 포함할 수 있어요.",
      es: "Escribe al bot en Telegram y DotCraft responde en el mismo chat. Las respuestas pueden incluir texto con formato, imágenes, archivos, audio y vídeo.",
      fr: "Écrivez au bot dans Telegram et DotCraft répond dans la même conversation. Les réponses peuvent inclure du texte mis en forme, des images, des fichiers, de l'audio et de la vidéo.",
      de: "Schreiben Sie dem Bot in Telegram, und DotCraft antwortet im selben Chat. Die Antworten können formatierten Text, Bilder, Dateien, Audio und Video enthalten.",
    },
    previewPrompt: "Turn this Telegram update into a workspace action item.",
    localizedPreviewPrompt: {
      en: "Turn this Telegram update into a workspace action item.",
      "zh-Hans": "把这条 Telegram 更新整理成工作区行动项。",
      ja: "この Telegram の更新を、ワークスペースのアクション項目にまとめてください。",
      ko: "이 Telegram 업데이트를 작업 공간의 할 일 항목으로 만들어 주세요.",
      es: "Convierte esta actualización de Telegram en un elemento de acción del espacio de trabajo.",
      fr: "Transformez cette mise à jour Telegram en élément d'action de l'espace de travail.",
      de: "Machen Sie aus dieser Telegram-Aktualisierung eine Aufgabe im Arbeitsbereich.",
    },
    setupSteps: [
      "In Telegram, send `/newbot` to [@BotFather](https://t.me/BotFather) and follow the prompts to get a bot token.",
      "In Manage, enter the bot token, then select Connect.",
      "Send the bot a message. To use it in a group, add the bot to the group.",
    ],
    localizedSetupSteps: {
      en: [
        "In Telegram, send `/newbot` to [@BotFather](https://t.me/BotFather) and follow the prompts to get a bot token.",
        "In Manage, enter the bot token, then select Connect.",
        "Send the bot a message. To use it in a group, add the bot to the group.",
      ],
      "zh-Hans": [
        "在 Telegram 中向 [@BotFather](https://t.me/BotFather) 发送 `/newbot`，按提示创建机器人并获取 token。",
        "在「管理」中填写 bot token，然后点击「连接」。",
        "向机器人发送消息。如需在群聊中使用，请将机器人加入群聊。",
      ],
      ja: [
        "Telegram で [@BotFather](https://t.me/BotFather) に `/newbot` を送信し、案内に従ってボットトークンを取得します。",
        "「管理」でボットトークンを入力し、「接続」を選択します。",
        "ボットにメッセージを送信します。グループで使うには、ボットをグループに追加します。",
      ],
      ko: [
        "Telegram에서 [@BotFather](https://t.me/BotFather)에 `/newbot`을 보내고, 안내에 따라 봇 토큰을 받으세요.",
        "관리에서 봇 토큰을 입력한 다음 연결을 선택하세요.",
        "봇에게 메시지를 보내세요. 그룹에서 사용하려면 봇을 그룹에 추가하세요.",
      ],
      es: [
        "En Telegram, envía `/newbot` a [@BotFather](https://t.me/BotFather) y sigue las indicaciones para obtener un token de bot.",
        "En Administrar, introduce el token del bot y selecciona Conectar.",
        "Envía un mensaje al bot. Para usarlo en un grupo, añade el bot al grupo.",
      ],
      fr: [
        "Dans Telegram, envoyez `/newbot` à [@BotFather](https://t.me/BotFather) et suivez les instructions pour obtenir un jeton de bot.",
        "Dans Gérer, saisissez le jeton du bot, puis sélectionnez Connecter.",
        "Envoyez un message au bot. Pour l'utiliser dans un groupe, ajoutez-le au groupe.",
      ],
      de: [
        "Senden Sie in Telegram `/newbot` an [@BotFather](https://t.me/BotFather) und folgen Sie den Anweisungen, um ein Bot-Token zu erhalten.",
        "Geben Sie unter Verwalten das Bot-Token ein und wählen Sie dann Verbinden.",
        "Senden Sie dem Bot eine Nachricht. Um ihn in einer Gruppe zu verwenden, fügen Sie den Bot zur Gruppe hinzu.",
      ],
    },
  },
  packageName: "@dotcraft/channel-telegram",
  configFileName: "telegram.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: false,
  capabilitySummary: {
    hasChannelTools: false,
    requiresInteractiveSetup: false,
    capabilitySetMayVaryByEnvironment: false,
  },
  channelContractVersion,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-telegram",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
