// Transport abstraction: StdioTransport (subprocess) and WebSocketTransport (remote).
// Supports split() into independent reader/writer halves for concurrent use.

use crate::wire::{error::WireError, types::JsonRpcMessage};
use anyhow::Result;
use std::process::Stdio;
use tokio::{
    io::{AsyncBufReadExt, AsyncWriteExt, BufReader},
    process::{Child, Command},
};

// ── Public split halves ───────────────────────────────────────────────────

/// Read half: receives JSON-RPC messages from the server.
pub enum TransportReader {
    Stdio(StdioReader),
    #[cfg(feature = "websocket")]
    WebSocket(WsReader),
}

impl TransportReader {
    pub async fn read_message(&mut self) -> Result<JsonRpcMessage, WireError> {
        match self {
            TransportReader::Stdio(r) => r.read_message().await,
            #[cfg(feature = "websocket")]
            TransportReader::WebSocket(r) => r.read_message().await,
        }
    }
}

/// Write half: sends JSON-RPC messages to the server.
pub enum TransportWriter {
    Stdio(StdioWriter),
    #[cfg(feature = "websocket")]
    WebSocket(WsWriter),
}

impl TransportWriter {
    pub async fn write_message<T: serde::Serialize>(&mut self, msg: &T) -> Result<(), WireError> {
        match self {
            TransportWriter::Stdio(w) => w.write_message(msg).await,
            #[cfg(feature = "websocket")]
            TransportWriter::WebSocket(w) => w.write_message(msg).await,
        }
    }
}

// ── Unsplit Transport (used before split) ─────────────────────────────────

/// Owning transport used only until `split()` is called. After splitting,
/// `TransportReader` and `TransportWriter` are used independently.
pub enum Transport {
    Stdio(StdioTransport),
    #[cfg(feature = "websocket")]
    WebSocket(WebSocketTransport),
}

impl Transport {
    /// Spawn the AppServer as a subprocess and return a StdioTransport.
    pub async fn spawn(server_bin: &str) -> Result<Self> {
        let transport = StdioTransport::spawn(server_bin).await?;
        Ok(Transport::Stdio(transport))
    }

    /// Connect to an existing AppServer over WebSocket.
    #[cfg(feature = "websocket")]
    pub async fn connect_ws(url: &str) -> Result<Self> {
        let ws = WebSocketTransport::connect(url).await?;
        Ok(Transport::WebSocket(ws))
    }

    /// Split into independent reader and writer halves.
    /// The underlying child process (if any) is kept alive by the writer half.
    pub fn split(self) -> (TransportReader, TransportWriter) {
        match self {
            Transport::Stdio(t) => {
                let (reader, writer) = t.split();
                (
                    TransportReader::Stdio(reader),
                    TransportWriter::Stdio(writer),
                )
            }
            #[cfg(feature = "websocket")]
            Transport::WebSocket(t) => {
                let (reader, writer) = t.split();
                (
                    TransportReader::WebSocket(reader),
                    TransportWriter::WebSocket(writer),
                )
            }
        }
    }
}

// ── Stdio ─────────────────────────────────────────────────────────────────

pub struct StdioTransport {
    child: Child,
    reader: BufReader<tokio::process::ChildStdout>,
    writer: tokio::process::ChildStdin,
}

impl StdioTransport {
    pub async fn spawn(server_bin: &str) -> Result<Self> {
        let mut child = Command::new(server_bin)
            .arg("app-server")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?;

        let stdout = child.stdout.take().expect("stdout must be piped");
        let stdin = child.stdin.take().expect("stdin must be piped");
        let stderr = child.stderr.take().expect("stderr must be piped");

        tokio::spawn(async move {
            let mut stderr_reader = BufReader::new(stderr);
            let mut line = String::new();
            loop {
                line.clear();
                match stderr_reader.read_line(&mut line).await {
                    Ok(0) => break,
                    Ok(_) => {
                        let text = line.trim();
                        if !text.is_empty() {
                            tracing::debug!(target: "dotcraft_tui::server_stderr", "{text}");
                        }
                    }
                    Err(error) => {
                        tracing::debug!(target: "dotcraft_tui::server_stderr", "stderr drain failed: {error}");
                        break;
                    }
                }
            }
        });

        Ok(Self {
            child,
            reader: BufReader::new(stdout),
            writer: stdin,
        })
    }

    pub fn split(self) -> (StdioReader, StdioWriter) {
        let reader = StdioReader {
            reader: self.reader,
        };
        // The writer holds the child so the process stays alive as long as we write.
        let writer = StdioWriter {
            _child: self.child,
            writer: self.writer,
        };
        (reader, writer)
    }
}

pub struct StdioReader {
    reader: BufReader<tokio::process::ChildStdout>,
}

impl StdioReader {
    pub async fn read_message(&mut self) -> Result<JsonRpcMessage, WireError> {
        let mut line = String::new();
        let n = self.reader.read_line(&mut line).await?;
        if n == 0 {
            return Err(WireError::ConnectionClosed);
        }
        let trimmed = line.trim();
        if trimmed.is_empty() {
            // Skip blank lines (the server may emit them).
            return Box::pin(self.read_message()).await;
        }
        let msg = serde_json::from_str(trimmed)?;
        Ok(msg)
    }
}

pub struct StdioWriter {
    _child: Child,
    writer: tokio::process::ChildStdin,
}

impl StdioWriter {
    pub async fn write_message<T: serde::Serialize>(&mut self, msg: &T) -> Result<(), WireError> {
        let mut json = serde_json::to_string(msg)?;
        json.push('\n');
        self.writer.write_all(json.as_bytes()).await?;
        self.writer.flush().await?;
        Ok(())
    }
}

// ── WebSocket ─────────────────────────────────────────────────────────────

#[cfg(feature = "websocket")]
mod ws {
    use super::*;
    use futures::{SinkExt, StreamExt};
    use tokio::net::TcpStream;
    use tokio_tungstenite::{connect_async, tungstenite::Message, MaybeTlsStream, WebSocketStream};

    type WsStream = WebSocketStream<MaybeTlsStream<TcpStream>>;

    pub struct WebSocketTransport {
        stream: WsStream,
    }

    impl WebSocketTransport {
        pub async fn connect(url: &str) -> Result<Self> {
            let (stream, _response) = connect_async(url)
                .await
                .map_err(|e| anyhow::anyhow!("WebSocket connection failed: {e}"))?;
            Ok(Self { stream })
        }

        pub fn split(self) -> (WsReader, WsWriter) {
            let (sink, stream) = self.stream.split();
            (WsReader { stream }, WsWriter { sink })
        }
    }

    pub struct WsReader {
        stream: futures::stream::SplitStream<WsStream>,
    }

    impl WsReader {
        pub async fn read_message(&mut self) -> Result<JsonRpcMessage, WireError> {
            loop {
                match self.stream.next().await {
                    Some(Ok(Message::Text(text))) => {
                        let msg = serde_json::from_str(&text)?;
                        return Ok(msg);
                    }
                    Some(Ok(Message::Close(_))) | None => {
                        return Err(WireError::ConnectionClosed);
                    }
                    Some(Ok(Message::Ping(_) | Message::Pong(_) | Message::Binary(_))) => {
                        // Ping/Pong handled by tungstenite; binary frames ignored per spec.
                        continue;
                    }
                    Some(Ok(Message::Frame(_))) => continue,
                    Some(Err(e)) => {
                        return Err(WireError::Protocol(format!("WebSocket error: {e}")));
                    }
                }
            }
        }
    }

    pub struct WsWriter {
        sink: futures::stream::SplitSink<WsStream, Message>,
    }

    impl WsWriter {
        pub async fn write_message<T: serde::Serialize>(
            &mut self,
            msg: &T,
        ) -> Result<(), WireError> {
            let json = serde_json::to_string(msg)?;
            self.sink
                .send(Message::Text(json.into()))
                .await
                .map_err(|e| WireError::Protocol(format!("WebSocket send error: {e}")))?;
            Ok(())
        }
    }
}

#[cfg(feature = "websocket")]
pub use ws::{WebSocketTransport, WsReader, WsWriter};
