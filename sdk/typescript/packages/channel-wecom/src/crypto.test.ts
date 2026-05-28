import assert from "node:assert/strict";
import test from "node:test";

import { WeComBizMsgCrypt } from "./wecom-crypto.js";

const token = "test-token";
const aesKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";

test("WeComBizMsgCrypt encrypts and decrypts callback XML", () => {
  const crypt = new WeComBizMsgCrypt(token, aesKey);
  const encrypted = crypt.encryptMsg("<xml><MsgType>text</MsgType></xml>", "123", "nonce");
  const encrypt = /<Encrypt><!\[CDATA\[(.*?)\]\]><\/Encrypt>/.exec(encrypted)?.[1] ?? "";
  const signature = crypt.computeSignature("123", "nonce", encrypt);
  const decrypted = crypt.decryptMsg(signature, "123", "nonce", `<xml><Encrypt><![CDATA[${encrypt}]]></Encrypt></xml>`);
  assert.equal(decrypted, "<xml><MsgType>text</MsgType></xml>");
});

test("WeComBizMsgCrypt verifies URL echo string", () => {
  const crypt = new WeComBizMsgCrypt(token, aesKey);
  const encrypted = crypt.encryptMsg("hello", "123", "nonce");
  const echo = /<Encrypt><!\[CDATA\[(.*?)\]\]><\/Encrypt>/.exec(encrypted)?.[1] ?? "";
  const signature = crypt.computeSignature("123", "nonce", echo);
  assert.equal(crypt.verifyUrl(signature, "123", "nonce", echo), "hello");
});

