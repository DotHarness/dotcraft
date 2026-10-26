import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "wecom-standard",
  channelName: "wecom",
  displayName: "WeCom",
  localizedDisplayName: {
    en: "WeCom",
    "zh-Hans": "企业微信",
    ja: "WeCom",
    ko: "WeCom",
    es: "WeCom",
    fr: "WeCom",
    de: "WeCom",
  },
  interface: {
    shortDescription: "Send and receive messages through a group bot.",
    localizedShortDescription: {
      en: "Send and receive messages through a group bot.",
      "zh-Hans": "通过群机器人收发消息。",
      ja: "グループボットでメッセージを送受信します。",
      ko: "그룹 봇으로 메시지를 주고받아요.",
      es: "Envía y recibe mensajes mediante un bot de grupo.",
      fr: "Envoyez et recevez des messages via un bot de groupe.",
      de: "Senden und empfangen Sie Nachrichten über einen Gruppen-Bot.",
    },
    longDescription:
      "Message the bot in WeCom and DotCraft replies in the same chat. Only whitelisted users and chats get replies. Replies can include text, images, files, and voice.",
    localizedLongDescription: {
      en: "Message the bot in WeCom and DotCraft replies in the same chat. Only whitelisted users and chats get replies. Replies can include text, images, files, and voice.",
      "zh-Hans": "在企业微信中给机器人发消息，DotCraft 会在同一会话中回复，仅回复白名单内的用户和会话。回复可以包含文本、图片、文件和语音。",
      ja: "WeCom でボットにメッセージを送ると、DotCraft が同じチャットで返信します。返信されるのは、許可リストに登録されたユーザーとチャットのみです。返信にはテキスト、画像、ファイル、音声を含められます。",
      ko: "WeCom에서 봇에게 메시지를 보내면 DotCraft가 같은 채팅에서 답장해요. 허용 목록에 있는 사용자와 채팅에만 답장해요. 답장에는 텍스트, 이미지, 파일, 음성을 포함할 수 있어요.",
      es: "Escribe al bot en WeCom y DotCraft responde en el mismo chat. Solo responde a los usuarios y chats permitidos. Las respuestas pueden incluir texto, imágenes, archivos y voz.",
      fr: "Écrivez au bot dans WeCom et DotCraft répond dans la même conversation. Seuls les utilisateurs et conversations autorisés reçoivent des réponses. Les réponses peuvent contenir du texte, des images, des fichiers et de la voix.",
      de: "Schreiben Sie dem Bot in WeCom, und DotCraft antwortet im selben Chat. Nur zugelassene Benutzer und Chats erhalten Antworten. Antworten können Text, Bilder, Dateien und Sprache enthalten.",
    },
    previewPrompt: "Sync this WeCom thread into project memory.",
    localizedPreviewPrompt: {
      en: "Sync this WeCom thread into project memory.",
      "zh-Hans": "把这段企业微信讨论同步到项目记忆中。",
      ja: "この WeCom のスレッドをプロジェクトのメモリに同期してください。",
      ko: "이 WeCom 스레드를 프로젝트 메모리에 동기화해 주세요.",
      es: "Sincroniza este hilo de WeCom con la memoria del proyecto.",
      fr: "Synchronisez ce fil WeCom dans la mémoire du projet.",
      de: "Synchronisieren Sie diesen WeCom-Thread mit dem Projektspeicher.",
    },
    setupSteps: [
      "In WeCom, create a group bot, turn on callback mode, and note its Token and EncodingAESKey.",
      "In Manage, add the bot with its callback path, Token, and EncodingAESKey, add admin users, whitelisted users, or whitelisted chats, then select Connect.",
      "Set the bot's callback URL to a public HTTPS address that forwards to DotCraft's callback listen port (`9000` by default).",
    ],
    localizedSetupSteps: {
      en: [
        "In WeCom, create a group bot, turn on callback mode, and note its Token and EncodingAESKey.",
        "In Manage, add the bot with its callback path, Token, and EncodingAESKey, add admin users, whitelisted users, or whitelisted chats, then select Connect.",
        "Set the bot's callback URL to a public HTTPS address that forwards to DotCraft's callback listen port (`9000` by default).",
      ],
      "zh-Hans": [
        "在企业微信中创建群机器人，启用回调模式，并记录 Token 和 EncodingAESKey。",
        "在「管理」中添加机器人配置，填写回调路径、Token 和 EncodingAESKey，添加管理员用户、白名单用户或白名单会话，然后点击「连接」。",
        "将机器人的回调 URL 设置为可从公网访问的 HTTPS 地址，并转发到 DotCraft 的回调监听端口（默认为 `9000`）。",
      ],
      ja: [
        "WeCom でグループボットを作成し、コールバックモードをオンにして、Token と EncodingAESKey を控えておきます。",
        "「管理」でボットを追加し、「コールバックパス」、Token、EncodingAESKey を入力します。「管理者ユーザー」、「許可リストのユーザー」、「許可リストのチャット」のいずれかを追加して、「接続」を選択します。",
        "ボットのコールバック URL を、DotCraft のコールバック待ち受けポート（デフォルトは `9000`）に転送する公開 HTTPS アドレスに設定します。",
      ],
      ko: [
        "WeCom에서 그룹 봇을 만들고 콜백 모드를 켠 다음 Token과 EncodingAESKey를 메모해 두세요.",
        "관리에서 봇을 추가하고 콜백 경로, Token, EncodingAESKey를 입력한 뒤 관리자 사용자, 허용 목록 사용자 또는 허용 목록 채팅을 추가하고 연결을 선택하세요.",
        "봇의 콜백 URL을 DotCraft의 콜백 수신 포트(기본값 `9000`)로 전달하는 공개 HTTPS 주소로 설정하세요.",
      ],
      es: [
        "En WeCom, crea un bot de grupo, activa el modo callback y anota su Token y EncodingAESKey.",
        "En Administrar, añade el bot con su ruta de callback, Token y EncodingAESKey, añade usuarios administradores, usuarios permitidos o chats permitidos y selecciona Conectar.",
        "Configura la URL de callback del bot con una dirección HTTPS pública que reenvíe al puerto de escucha de callback de DotCraft (`9000` por defecto).",
      ],
      fr: [
        "Dans WeCom, créez un bot de groupe, activez le mode rappel et notez son Token et son EncodingAESKey.",
        "Dans Gérer, ajoutez le bot avec son chemin de rappel, son Token et son EncodingAESKey, ajoutez des utilisateurs administrateurs, des utilisateurs autorisés ou des conversations autorisées, puis sélectionnez Connecter.",
        "Définissez l'URL de rappel du bot sur une adresse HTTPS publique qui redirige vers le port d'écoute de rappel de DotCraft (`9000` par défaut).",
      ],
      de: [
        "Erstellen Sie in WeCom einen Gruppen-Bot, aktivieren Sie den Callback-Modus und notieren Sie Token und EncodingAESKey.",
        "Fügen Sie unter Verwalten den Bot mit Callback-Pfad, Token und EncodingAESKey hinzu, ergänzen Sie Admin-Benutzer, zugelassene Benutzer oder zugelassene Chats und wählen Sie dann Verbinden.",
        "Legen Sie als Callback-URL des Bots eine öffentliche HTTPS-Adresse fest, die an den Callback-Listen-Port von DotCraft weiterleitet (standardmäßig `9000`).",
      ],
    },
  },
  packageName: "@dotcraft/channel-wecom",
  configFileName: "wecom.json",
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
    bin: "dotcraft-channel-wecom",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
