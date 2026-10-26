import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";
import { type ModuleManifest } from "@dotcraft/channel";

export const manifest: ModuleManifest = {
  moduleId: "weixin-standard",
  channelName: "weixin",
  displayName: "微信",
  localizedDisplayName: {
    en: "WeChat",
    "zh-Hans": "微信",
    ja: "WeChat",
    ko: "WeChat",
    es: "WeChat",
    fr: "WeChat",
    de: "WeChat",
  },
  interface: {
    shortDescription: "Send and receive messages through WeChat ClawBot.",
    localizedShortDescription: {
      en: "Send and receive messages through WeChat ClawBot.",
      "zh-Hans": "通过微信 ClawBot 收发消息。",
      ja: "WeChat ClawBot を通じてメッセージを送受信します。",
      ko: "WeChat ClawBot으로 메시지를 주고받아요.",
      es: "Envía y recibe mensajes a través de WeChat ClawBot.",
      fr: "Envoyez et recevez des messages via WeChat ClawBot.",
      de: "Senden und empfangen Sie Nachrichten über WeChat ClawBot.",
    },
    longDescription:
      "Message DotCraft in the WeChat ClawBot chat and it replies in the same chat. Replies are plain text and can include images and files.",
    localizedLongDescription: {
      en: "Message DotCraft in the WeChat ClawBot chat and it replies in the same chat. Replies are plain text and can include images and files.",
      "zh-Hans": "在微信的「微信 ClawBot」对话中给 DotCraft 发消息，它会在同一对话中回复。回复为纯文本，也可以包含图片和文件。",
      ja: "WeChat ClawBot のチャットで DotCraft にメッセージを送ると、同じチャットで返信します。返信はプレーンテキストで、画像やファイルを含めることもできます。",
      ko: "WeChat ClawBot 채팅에서 DotCraft에 메시지를 보내면 같은 채팅으로 답장해요. 답장은 일반 텍스트이며 이미지와 파일을 포함할 수 있어요.",
      es: "Escribe a DotCraft en el chat de WeChat ClawBot y te responderá en el mismo chat. Las respuestas son texto sin formato y pueden incluir imágenes y archivos.",
      fr: "Écrivez à DotCraft dans la discussion WeChat ClawBot et il vous répond dans la même discussion. Les réponses sont en texte brut et peuvent inclure des images et des fichiers.",
      de: "Schreiben Sie DotCraft im Chat mit WeChat ClawBot, und es antwortet im selben Chat. Antworten sind reiner Text und können Bilder und Dateien enthalten.",
    },
    previewPrompt: "Capture this Weixin request and draft a reply.",
    localizedPreviewPrompt: {
      en: "Capture this Weixin request and draft a reply.",
      "zh-Hans": "记录这条微信请求，并准备一段回复。",
      ja: "この WeChat のリクエストを記録して、返信の下書きを作成してください。",
      ko: "이 WeChat 요청을 기록하고 답장 초안을 작성하세요.",
      es: "Registra esta solicitud de WeChat y redacta una respuesta.",
      fr: "Enregistrez cette demande WeChat et rédigez une réponse.",
      de: "Erfassen Sie diese WeChat-Anfrage und entwerfen Sie eine Antwort.",
    },
    setupSteps: [
      "In WeChat on your phone, turn on WeChat ClawBot under Me › Settings › Plugins.",
      "Select Connect and scan the QR code with WeChat.",
      "Send a message in the WeChat ClawBot chat.",
    ],
    localizedSetupSteps: {
      en: [
        "In WeChat on your phone, turn on WeChat ClawBot under Me › Settings › Plugins.",
        "Select Connect and scan the QR code with WeChat.",
        "Send a message in the WeChat ClawBot chat.",
      ],
      "zh-Hans": [
        "在手机微信的「我 › 设置 › 插件」中启用「微信 ClawBot」。",
        "点击「连接」，使用微信扫描二维码。",
        "在「微信 ClawBot」对话中发送消息。",
      ],
      ja: [
        "スマートフォンの WeChat で「自分 › 設定 › プラグイン」を開き、「WeChat ClawBot」をオンにします。",
        "「接続」を選択し、WeChat で QR コードをスキャンします。",
        "「WeChat ClawBot」のチャットでメッセージを送信します。",
      ],
      ko: [
        "휴대폰의 WeChat에서 나 › 설정 › 플러그인으로 이동해 WeChat ClawBot을 켜세요.",
        "연결을 선택하고 WeChat으로 QR 코드를 스캔하세요.",
        "WeChat ClawBot 채팅에서 메시지를 보내세요.",
      ],
      es: [
        "En WeChat en tu teléfono, activa WeChat ClawBot en Yo › Configuración › Plugins.",
        "Selecciona Conectar y escanea el código QR con WeChat.",
        "Envía un mensaje en el chat de WeChat ClawBot.",
      ],
      fr: [
        "Dans WeChat sur votre téléphone, activez WeChat ClawBot sous Moi › Paramètres › Plugins.",
        "Sélectionnez Connecter et scannez le code QR avec WeChat.",
        "Envoyez un message dans la discussion WeChat ClawBot.",
      ],
      de: [
        "Aktivieren Sie in WeChat auf Ihrem Smartphone WeChat ClawBot unter Ich › Einstellungen › Plugins.",
        "Wählen Sie Verbinden und scannen Sie den QR-Code mit WeChat.",
        "Senden Sie eine Nachricht im Chat mit WeChat ClawBot.",
      ],
    },
  },
  packageName: "@dotcraft/channel-weixin",
  configFileName: "weixin.json",
  supportedTransports: ["websocket"],
  requiresInteractiveSetup: true,
  capabilitySummary: {
    hasChannelTools: false,
    requiresInteractiveSetup: true,
    capabilitySetMayVaryByEnvironment: false,
  },
  channelContractVersion,
  supportedChannelProtocolVersions: ["0.2"],
  variant: "standard",
  launcher: {
    bin: "dotcraft-channel-weixin",
    supportsWorkspaceFlag: true,
    supportsConfigOverrideFlag: true,
  },
};
