// JSON-RPC 2.0 client.
//
// Architecture:
//   - A background tokio task runs `reader_loop`, reading lines from TransportReader
//     and forwarding them into an mpsc channel.
//   - The main event loop polls `recv()` from that channel inside `tokio::select!`.
//   - Outgoing requests are sent via `TransportWriter` (held directly on WireClient).
//   - Request/response correlation uses a `HashMap<id, oneshot::Sender>` so that
//     `request()` can await the response without blocking the event loop.
//   - Server-initiated requests (e.g. item/approval/request) are forwarded into
//     the same message_rx channel, distinguished by having both `id` and `method`.

use crate::wire::{
    error::WireError,
    transport::{Transport, TransportReader, TransportWriter},
    types::{
        ClientCapabilities, ClientInfo, InitializeParams, InitializeResult, JsonRpcMessage,
        JsonRpcNotification, JsonRpcRequest, ServerCapabilities, ServerInfo,
    },
};
use anyhow::Result;
use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use tokio::sync::{mpsc, oneshot};

static NEXT_ID: AtomicU64 = AtomicU64::new(1);

fn next_id() -> u64 {
    NEXT_ID.fetch_add(1, Ordering::Relaxed)
}

// ── WireClient ────────────────────────────────────────────────────────────

pub struct WireClient {
    /// Incoming messages from the background reader task.
    message_rx: mpsc::UnboundedReceiver<Result<JsonRpcMessage, WireError>>,
    /// Outgoing write handle.
    writer: TransportWriter,
    /// Pending request/response correlations: id → oneshot sender.
    pending: HashMap<u64, oneshot::Sender<Result<serde_json::Value>>>,
    /// Populated after initialize().
    pub server_info: Option<ServerInfo>,
    pub capabilities: ServerCapabilities,
}

impl WireClient {
    /// Spawn the background reader task and return a WireClient ready for use.
    pub fn spawn(transport: Transport) -> Self {
        let (reader, writer) = transport.split();
        let (tx, rx) = mpsc::unbounded_channel();
        tokio::spawn(reader_loop(reader, tx));
        Self {
            message_rx: rx,
            writer,
            pending: HashMap::new(),
            server_info: None,
            capabilities: ServerCapabilities::default(),
        }
    }

    /// Perform the initialize / initialized handshake.
    /// This must be called before entering the event loop.
    pub async fn initialize(&mut self) -> Result<()> {
        let params = InitializeParams {
            client_info: ClientInfo {
                name: "dotcraft-tui".to_string(),
                title: "DotCraft Terminal UI (Ratatui)".to_string(),
                version: env!("CARGO_PKG_VERSION").to_string(),
            },
            capabilities: ClientCapabilities {
                approval_support: true,
                request_user_input_support: true,
                streaming_support: true,
                command_execution_streaming: true,
                tool_execution_lifecycle: true,
                background_terminals: true,
                opt_out_notification_methods: vec![],
            },
        };

        let result: InitializeResult = self
            .request("initialize", serde_json::to_value(params)?)
            .await?;

        self.server_info = Some(result.server_info);
        self.capabilities = result.capabilities;

        // Signal readiness.
        self.notify("initialized", serde_json::json!({})).await?;
        Ok(())
    }

    /// Receive the next incoming message. Returns None only if the reader task died.
    /// The caller must call `resolve_response` first; if that returns false, the
    /// message is a notification or a server-initiated request.
    pub async fn recv(&mut self) -> Option<Result<JsonRpcMessage, WireError>> {
        self.message_rx.recv().await
    }

    /// If `msg` is a response to one of our pending requests, resolve the oneshot
    /// and return true. Otherwise return false (it's a notification or server request).
    pub fn resolve_response(&mut self, msg: &JsonRpcMessage) -> bool {
        // A response has an `id` and no `method`.
        let id = match (&msg.id, &msg.method) {
            (Some(id), None) => id,
            _ => return false,
        };

        let numeric_id = match id.as_u64() {
            Some(n) => n,
            None => return false,
        };

        if let Some(tx) = self.pending.remove(&numeric_id) {
            let result = if let Some(err) = &msg.error {
                Err(WireError::JsonRpc {
                    code: err.code,
                    message: err.message.clone(),
                }
                .into())
            } else {
                Ok(msg.result.clone().unwrap_or(serde_json::Value::Null))
            };
            let _ = tx.send(result);
            true
        } else {
            false
        }
    }

    /// Send a JSON-RPC request and await its response, draining and re-routing
    /// any notifications that arrive in the meantime.
    ///
    /// This is used only during the `initialize` handshake (before the event loop
    /// starts). Inside the event loop, requests are sent via `request_fire_and_forget`
    /// or the pending map is consulted by the loop itself.
    pub async fn request<T: serde::de::DeserializeOwned>(
        &mut self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<T> {
        let id = next_id();
        let (tx, rx) = oneshot::channel();
        self.pending.insert(id, tx);

        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id,
            method: method.to_string(),
            params,
        };
        self.writer.write_message(&req).await?;

        // Drain incoming messages until ours resolves.
        loop {
            match self.message_rx.recv().await {
                None => {
                    anyhow::bail!("wire connection closed while waiting for response to {method}")
                }
                Some(Err(e)) => return Err(e.into()),
                Some(Ok(msg)) => {
                    if self.resolve_response(&msg) {
                        // Our response was resolved; rx is ready.
                        break;
                    }
                    // It was a notification; drop it during handshake (pre-loop).
                }
            }
        }

        let value = rx.await.map_err(|_| anyhow::anyhow!("oneshot dropped"))??;
        Ok(serde_json::from_value(value)?)
    }

    /// Send a JSON-RPC request and register a pending correlation entry.
    /// The event loop resolves the response via `resolve_response`.
    /// Returns the request id so the caller can track it if needed.
    pub async fn send_request(
        &mut self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<(u64, oneshot::Receiver<Result<serde_json::Value>>)> {
        let id = next_id();
        let (tx, rx) = oneshot::channel();
        self.pending.insert(id, tx);

        let req = JsonRpcRequest {
            jsonrpc: "2.0",
            id,
            method: method.to_string(),
            params,
        };
        self.writer.write_message(&req).await?;
        Ok((id, rx))
    }

    /// Send a JSON-RPC notification (no response expected).
    pub async fn notify(
        &mut self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<(), WireError> {
        let notif = JsonRpcNotification {
            jsonrpc: "2.0",
            method: method.to_string(),
            params,
        };
        self.writer.write_message(&notif).await
    }

    /// Respond to a server-initiated request (e.g. item/approval/request).
    pub async fn respond(
        &mut self,
        id: serde_json::Value,
        result: serde_json::Value,
    ) -> Result<(), WireError> {
        #[derive(serde::Serialize)]
        struct Response {
            jsonrpc: &'static str,
            id: serde_json::Value,
            result: serde_json::Value,
        }
        self.writer
            .write_message(&Response {
                jsonrpc: "2.0",
                id,
                result,
            })
            .await
    }
}

// ── Background reader task ────────────────────────────────────────────────

/// Continuously reads from the transport and forwards messages into the channel.
/// Exits when the connection closes or an unrecoverable I/O error occurs.
async fn reader_loop(
    mut reader: TransportReader,
    tx: mpsc::UnboundedSender<Result<JsonRpcMessage, WireError>>,
) {
    loop {
        let result = reader.read_message().await;
        let is_closed = matches!(result, Err(WireError::ConnectionClosed));
        if tx.send(result).is_err() || is_closed {
            break;
        }
    }
}
