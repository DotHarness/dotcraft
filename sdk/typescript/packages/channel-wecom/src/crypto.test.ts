import assert from "node:assert/strict";
import test from "node:test";

import { WeComBizMsgCrypt } from "./wecom-crypto.js";

test("WeComBizMsgCrypt round-trips callback XML and verifies the URL echo string", () => {
  const crypt = new WeComBizMsgCrypt("test-token", "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG");
  const encryptedOf = (plain: string) =>
    /<Encrypt><!\[CDATA\[(.*?)\]\]><\/Encrypt>/.exec(crypt.encryptMsg(plain, "123", "nonce"))?.[1] ?? "";

  const encrypt = encryptedOf("<xml><MsgType>text</MsgType></xml>");
  const signature = crypt.computeSignature("123", "nonce", encrypt);
  assert.equal(
    crypt.decryptMsg(signature, "123", "nonce", `<xml><Encrypt><![CDATA[${encrypt}]]></Encrypt></xml>`),
    "<xml><MsgType>text</MsgType></xml>",
  );

  const echo = encryptedOf("hello");
  assert.equal(crypt.verifyUrl(crypt.computeSignature("123", "nonce", echo), "123", "nonce", echo), "hello");
});
