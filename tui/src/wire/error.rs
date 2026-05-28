use thiserror::Error;

#[derive(Debug, Error)]
pub enum WireError {
    #[error("transport error: {0}")]
    Transport(#[from] std::io::Error),
    #[error("JSON serialization error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("server returned JSON-RPC error {code}: {message}")]
    JsonRpc { code: i64, message: String },
    #[error("connection closed unexpectedly")]
    ConnectionClosed,
    #[error("protocol error: {0}")]
    Protocol(String),
}
