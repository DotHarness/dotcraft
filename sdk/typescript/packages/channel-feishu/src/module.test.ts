import assert from "node:assert/strict";
import test from "node:test";

import { configDescriptors, configGroups } from "./config-descriptors.js";

test("Feishu exposes its grouped platform and reaction configuration", () => {
  assert.deepEqual(configGroups.map((group) => group.id), ["configuration", "advanced", "debug"]);

  const platform = configDescriptors.find((item) => item.key === "feishu.brand");
  assert.equal(platform?.group, "configuration");
  assert.equal(platform?.defaultValue, "feishu");
  assert.deepEqual(platform?.options?.map((option) => option.value), ["feishu", "lark"]);

  const reaction = configDescriptors.find((item) => item.key === "feishu.ackReactionEmoji");
  assert.equal(reaction?.group, "advanced");
  assert.equal(reaction?.defaultValue, "GLANCE");
  assert.equal(reaction?.allowCustomValue, true);
  assert.deepEqual(reaction?.options?.map((option) => option.value), [
    "GLANCE", "OK", "THUMBSUP", "OnIt", "DONE", "SMILE",
  ]);
});
