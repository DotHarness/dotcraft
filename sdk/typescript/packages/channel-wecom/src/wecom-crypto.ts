import { createCipheriv, createDecipheriv, createHash, randomBytes } from "node:crypto";

const Pkcs7BlockSize = 32;

export class WeComBizMsgCrypt {
  private readonly token: string;
  private readonly aesKey: Buffer;

  constructor(token: string, encodingAesKey: string) {
    if (!token) throw new Error("Token must not be empty.");
    if (!encodingAesKey) throw new Error("EncodingAESKey must not be empty.");
    this.token = token;
    this.aesKey = Buffer.from(padBase64(encodingAesKey), "base64");
    if (this.aesKey.length !== 32) {
      throw new Error("EncodingAESKey must decode to 32 bytes.");
    }
  }

  verifyUrl(msgSignature: string, timestamp: string, nonce: string, echoStr: string): string {
    const signature = this.computeSignature(timestamp, nonce, echoStr);
    if (signature !== msgSignature) {
      throw new Error(`Signature validation failed: expected=${msgSignature}, actual=${signature}`);
    }

    const plaintext = this.aesDecrypt(echoStr);
    const { message } = parsePlainText(plaintext);
    return message.toString("utf-8");
  }

  decryptMsg(msgSignature: string, timestamp: string, nonce: string, postData: string): string {
    const encryptData = extractXmlTag(postData, "Encrypt");
    if (!encryptData) throw new Error("Could not extract Encrypt from callback XML.");

    const signature = this.computeSignature(timestamp, nonce, encryptData);
    if (signature !== msgSignature) {
      throw new Error(`Signature validation failed: expected=${msgSignature}, actual=${signature}`);
    }

    const plaintext = this.aesDecrypt(encryptData);
    const { message } = parsePlainText(plaintext);
    return message.toString("utf-8");
  }

  encryptMsg(replyMsg: string, timestamp: string, nonce: string): string {
    const msgBytes = Buffer.from(replyMsg, "utf-8");
    const msgLen = Buffer.alloc(4);
    msgLen.writeUInt32BE(msgBytes.length, 0);
    const plaintext = Buffer.concat([generateRandomBytes(16), msgLen, msgBytes]);
    const encrypted = this.aesEncrypt(plaintext);
    const signature = this.computeSignature(timestamp, nonce, encrypted);
    return `<xml><Encrypt><![CDATA[${encrypted}]]></Encrypt><MsgSignature><![CDATA[${signature}]]></MsgSignature><TimeStamp>${timestamp}</TimeStamp><Nonce><![CDATA[${nonce}]]></Nonce></xml>`;
  }

  computeSignature(timestamp: string, nonce: string, encrypt: string): string {
    const raw = [this.token, timestamp, nonce, encrypt].sort().join("");
    return createHash("sha1").update(raw, "utf-8").digest("hex");
  }

  private aesEncrypt(plaintext: Buffer): string {
    const cipher = createCipheriv("aes-256-cbc", this.aesKey, this.aesKey.subarray(0, 16));
    cipher.setAutoPadding(false);
    const padded = pkcs7Pad(plaintext, Pkcs7BlockSize);
    return Buffer.concat([cipher.update(padded), cipher.final()]).toString("base64");
  }

  private aesDecrypt(base64EncryptedText: string): Buffer {
    const encrypted = Buffer.from(base64EncryptedText, "base64");
    const decipher = createDecipheriv("aes-256-cbc", this.aesKey, this.aesKey.subarray(0, 16));
    decipher.setAutoPadding(false);
    const decrypted = Buffer.concat([decipher.update(encrypted), decipher.final()]);
    return pkcs7Unpad(decrypted, Pkcs7BlockSize);
  }
}

export function extractXmlTag(xml: string, tagName: string): string | null {
  const pattern = new RegExp(`<${tagName}>\\s*(?:<!\\[CDATA\\[)?([\\s\\S]*?)(?:\\]\\]>)?\\s*</${tagName}>`, "i");
  const match = pattern.exec(xml);
  return match?.[1]?.trim() ?? null;
}

function parsePlainText(plaintext: Buffer): { random: Buffer; message: Buffer; receiverId: Buffer } {
  if (plaintext.length < 20) throw new Error("Decrypted payload is too short.");
  const random = plaintext.subarray(0, 16);
  const msgLen = plaintext.readUInt32BE(16);
  if (plaintext.length < 20 + msgLen) throw new Error("Decrypted message length mismatch.");
  const message = plaintext.subarray(20, 20 + msgLen);
  const receiverId = plaintext.subarray(20 + msgLen);
  return { random, message, receiverId };
}

function pkcs7Pad(data: Buffer, blockSize: number): Buffer {
  const padding = blockSize - (data.length % blockSize);
  return Buffer.concat([data, Buffer.alloc(padding, padding)]);
}

function pkcs7Unpad(data: Buffer, blockSize: number): Buffer {
  if (data.length === 0) throw new Error("PKCS7 unpad failed: empty data.");
  if (data.length % blockSize !== 0) throw new Error("PKCS7 unpad failed: data length is not block aligned.");
  const padding = data[data.length - 1] ?? 0;
  if (padding < 1 || padding > blockSize) throw new Error(`PKCS7 unpad failed: invalid padding length ${padding}.`);
  return data.subarray(0, data.length - padding);
}

function padBase64(value: string): string {
  const remainder = value.length % 4;
  if (remainder === 2) return `${value}==`;
  if (remainder === 3) return `${value}=`;
  return value;
}

function generateRandomBytes(length: number): Buffer {
  return randomBytes(length);
}

