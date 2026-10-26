import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "qq-standard",
  channelName: "qq",
  displayName: "QQ",
  interface: {
    shortDescription: "Send and receive messages through NapCat.",
    localizedShortDescription: {
      en: "Send and receive messages through NapCat.",
      "zh-Hans": "通过 NapCat 收发消息。",
      ja: "NapCat 経由でメッセージを送受信します。",
      ko: "NapCat으로 메시지를 주고받아요.",
      es: "Envía y recibe mensajes a través de NapCat.",
      fr: "Envoyez et recevez des messages via NapCat.",
      de: "Nachrichten über NapCat senden und empfangen.",
    },
    longDescription:
      "Message the bot in QQ and DotCraft replies in the same chat. In groups, mention the bot. Replies can include text, images, files, voice, and video.",
    localizedLongDescription: {
      en: "Message the bot in QQ and DotCraft replies in the same chat. In groups, mention the bot. Replies can include text, images, files, voice, and video.",
      "zh-Hans": "在 QQ 中给机器人发消息，DotCraft 会在同一会话中回复，群聊中需要 @ 机器人。回复可以包含文本、图片、文件、语音和视频。",
      ja: "QQ でボットにメッセージを送ると、DotCraft が同じチャットで返信します。グループではボットをメンションしてください。返信にはテキスト、画像、ファイル、音声、動画を含められます。",
      ko: "QQ에서 봇에게 메시지를 보내면 DotCraft가 같은 대화에서 답장해요. 그룹에서는 봇을 멘션하세요. 답장에는 텍스트, 이미지, 파일, 음성, 동영상을 포함할 수 있어요.",
      es: "Escribe al bot en QQ y DotCraft responde en el mismo chat. En los grupos, menciona al bot. Las respuestas pueden incluir texto, imágenes, archivos, voz y vídeo.",
      fr: "Écrivez au bot dans QQ et DotCraft répond dans la même conversation. Dans les groupes, mentionnez le bot. Les réponses peuvent contenir du texte, des images, des fichiers, de la voix et de la vidéo.",
      de: "Schreiben Sie dem Bot in QQ, und DotCraft antwortet im selben Chat. Erwähnen Sie den Bot in Gruppen. Antworten können Text, Bilder, Dateien, Sprachnachrichten und Videos enthalten.",
    },
    previewPrompt: "Track this QQ group task and record the blockers.",
    localizedPreviewPrompt: {
      en: "Track this QQ group task and record the blockers.",
      "zh-Hans": "跟进这个 QQ 群任务，并记录阻塞点。",
      ja: "この QQ グループのタスクを追跡して、ブロッカーを記録してください。",
      ko: "이 QQ 그룹 작업을 추적하고 막힌 부분을 기록해 주세요.",
      es: "Haz seguimiento de esta tarea del grupo de QQ y registra los bloqueos.",
      fr: "Suivez cette tâche du groupe QQ et consignez les blocages.",
      de: "Verfolgen Sie diese QQ-Gruppenaufgabe und halten Sie die Blocker fest.",
    },
    setupSteps: [
      "Sign in to [NapCat](https://napneko.github.io/) with the QQ account for the bot.",
      "In NapCat's network settings, add a WebSocket client with the URL `ws://127.0.0.1:6700/` and the message format `array`. If you set an access token in Manage, use the same token.",
      "In Manage, add admin users, whitelisted users, or whitelisted groups, then select Connect.",
      "Send the bot a direct message, or mention it in a whitelisted group.",
    ],
    localizedSetupSteps: {
      en: [
        "Sign in to [NapCat](https://napneko.github.io/) with the QQ account for the bot.",
        "In NapCat's network settings, add a WebSocket client with the URL `ws://127.0.0.1:6700/` and the message format `array`. If you set an access token in Manage, use the same token.",
        "In Manage, add admin users, whitelisted users, or whitelisted groups, then select Connect.",
        "Send the bot a direct message, or mention it in a whitelisted group.",
      ],
      "zh-Hans": [
        "在 [NapCat](https://napneko.github.io/) 中登录机器人使用的 QQ 账号。",
        "在 NapCat 网络配置中添加 WebSocket 客户端，地址为 `ws://127.0.0.1:6700/`，消息格式为 `array`。如在「管理」中设置了访问令牌，请填写相同的令牌。",
        "在「管理」中添加管理员用户、白名单用户或白名单群，然后点击「连接」。",
        "向机器人发送私聊消息，或在白名单群中 @ 机器人。",
      ],
      ja: [
        "ボット用の QQ アカウントで [NapCat](https://napneko.github.io/) にサインインします。",
        "NapCat のネットワーク設定で、URL `ws://127.0.0.1:6700/`、メッセージ形式 `array` の WebSocket クライアントを追加します。「管理」でアクセストークンを設定した場合は、同じトークンを使用してください。",
        "「管理」で管理者ユーザー、許可ユーザー、許可グループのいずれかを追加し、「接続」を選択します。",
        "ボットにダイレクトメッセージを送るか、許可グループでボットをメンションします。",
      ],
      ko: [
        "봇에 사용할 QQ 계정으로 [NapCat](https://napneko.github.io/)에 로그인하세요.",
        "NapCat 네트워크 설정에서 URL이 `ws://127.0.0.1:6700/`이고 메시지 형식이 `array`인 WebSocket 클라이언트를 추가하세요. 관리에서 액세스 토큰을 설정했다면 같은 토큰을 사용하세요.",
        "관리에서 관리자 사용자, 허용된 사용자 또는 허용된 그룹을 추가한 다음 연결을 선택하세요.",
        "봇에게 다이렉트 메시지를 보내거나, 허용된 그룹에서 봇을 멘션하세요.",
      ],
      es: [
        "Inicia sesión en [NapCat](https://napneko.github.io/) con la cuenta de QQ del bot.",
        "En los ajustes de red de NapCat, añade un cliente WebSocket con la URL `ws://127.0.0.1:6700/` y el formato de mensaje `array`. Si estableces un token de acceso en Administrar, usa el mismo token.",
        "En Administrar, añade usuarios administradores, usuarios permitidos o grupos permitidos y selecciona Conectar.",
        "Envía un mensaje directo al bot o menciónalo en un grupo permitido.",
      ],
      fr: [
        "Connectez-vous à [NapCat](https://napneko.github.io/) avec le compte QQ du bot.",
        "Dans les paramètres réseau de NapCat, ajoutez un client WebSocket avec l'URL `ws://127.0.0.1:6700/` et le format de message `array`. Si vous définissez un jeton d'accès dans Gérer, utilisez le même jeton.",
        "Dans Gérer, ajoutez des utilisateurs administrateurs, des utilisateurs autorisés ou des groupes autorisés, puis sélectionnez Connecter.",
        "Envoyez un message direct au bot ou mentionnez-le dans un groupe autorisé.",
      ],
      de: [
        "Melden Sie sich mit dem QQ-Konto des Bots bei [NapCat](https://napneko.github.io/) an.",
        "Fügen Sie in den Netzwerkeinstellungen von NapCat einen WebSocket-Client mit der URL `ws://127.0.0.1:6700/` und dem Nachrichtenformat `array` hinzu. Wenn Sie unter Verwalten ein Zugriffstoken festlegen, verwenden Sie dasselbe Token.",
        "Fügen Sie unter Verwalten Admin-Benutzer, zugelassene Benutzer oder zugelassene Gruppen hinzu und wählen Sie dann Verbinden.",
        "Senden Sie dem Bot eine Direktnachricht oder erwähnen Sie ihn in einer zugelassenen Gruppe.",
      ],
    },
  },
  packageName: "@dotcraft/channel-qq",
  configFileName: "qq.json",
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
    bin: "dotcraft-channel-qq",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
