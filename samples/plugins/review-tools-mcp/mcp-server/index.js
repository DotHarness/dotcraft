#!/usr/bin/env node

const readline = require("node:readline");

const rl = readline.createInterface({
  input: process.stdin,
  crlfDelay: Infinity
});

function send(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function result(id, value) {
  send({ jsonrpc: "2.0", id, result: value });
}

function error(id, code, message) {
  send({ jsonrpc: "2.0", id, error: { code, message } });
}

const reviewTool = {
  name: "submit_review_draft",
  description: "Accepts a structured review draft and returns a sample acknowledgement.",
  inputSchema: {
    type: "object",
    properties: {
      summary: { type: "string" },
      comments: {
        type: "array",
        items: { type: "string" }
      }
    },
    required: ["summary"]
  }
};

rl.on("line", line => {
  if (!line.trim()) return;

  let message;
  try {
    message = JSON.parse(line);
  } catch (err) {
    error(null, -32700, `Parse error: ${err.message}`);
    return;
  }

  if (message.id === undefined || message.id === null) return;

  switch (message.method) {
    case "initialize":
      result(message.id, {
        protocolVersion: message.params?.protocolVersion ?? "2024-11-05",
        capabilities: { tools: {} },
        serverInfo: {
          name: "review-tools-mcp-sample",
          version: "0.1.0"
        }
      });
      break;

    case "ping":
      result(message.id, {});
      break;

    case "tools/list":
      result(message.id, { tools: [reviewTool] });
      break;

    case "tools/call": {
      const args = message.params?.arguments ?? {};
      result(message.id, {
        content: [
          {
            type: "text",
            text: JSON.stringify({
              accepted: true,
              summary: args.summary ?? "",
              commentCount: Array.isArray(args.comments) ? args.comments.length : 0
            })
          }
        ]
      });
      break;
    }

    default:
      error(message.id, -32601, `Method not found: ${message.method}`);
      break;
  }
});

rl.on("close", () => {
  process.exit(0);
});
