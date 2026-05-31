// Wire protocol DTO types.
// These mirror the JSON shapes defined in specs/protocols/appserver-protocol.md
// with camelCase field names as required by §2.3.

use serde::{Deserialize, Serialize};

// ── JSON-RPC 2.0 envelopes ────────────────────────────────────────────────

#[derive(Debug, Serialize)]
pub struct JsonRpcRequest {
    pub jsonrpc: &'static str,
    pub id: u64,
    pub method: String,
    pub params: serde_json::Value,
}

#[derive(Debug, Serialize)]
pub struct JsonRpcNotification {
    pub jsonrpc: &'static str,
    pub method: String,
    pub params: serde_json::Value,
}

#[derive(Debug, Deserialize)]
pub struct JsonRpcMessage {
    pub jsonrpc: Option<String>,
    pub id: Option<serde_json::Value>,
    pub method: Option<String>,
    pub params: Option<serde_json::Value>,
    pub result: Option<serde_json::Value>,
    pub error: Option<JsonRpcError>,
}

#[derive(Debug, Deserialize)]
pub struct JsonRpcError {
    pub code: i64,
    pub message: String,
    pub data: Option<serde_json::Value>,
}

// ── initialize ────────────────────────────────────────────────────────────

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InitializeParams {
    pub client_info: ClientInfo,
    pub capabilities: ClientCapabilities,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ClientInfo {
    pub name: String,
    pub title: String,
    pub version: String,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ClientCapabilities {
    pub approval_support: bool,
    pub request_user_input_support: bool,
    pub streaming_support: bool,
    pub command_execution_streaming: bool,
    pub tool_execution_lifecycle: bool,
    pub opt_out_notification_methods: Vec<String>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InitializeResult {
    pub server_info: ServerInfo,
    pub capabilities: ServerCapabilities,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ServerInfo {
    pub name: String,
    pub version: String,
    pub protocol_version: String,
    pub extensions: Option<Vec<String>>,
}

#[derive(Debug, Deserialize, Clone, Default)]
#[serde(rename_all = "camelCase")]
pub struct ServerCapabilities {
    pub thread_management: Option<bool>,
    pub thread_subscriptions: Option<bool>,
    pub thread_goals: Option<bool>,
    pub approval_flow: Option<bool>,
    pub mode_switch: Option<bool>,
    pub config_override: Option<bool>,
    pub cron_management: Option<bool>,
    pub heartbeat_management: Option<bool>,
    pub command_management: Option<bool>,
    pub provider_management: Option<bool>,
    pub model_catalog_management: Option<bool>,
    pub workspace_config_management: Option<bool>,
    pub skills_management: Option<bool>,
    pub skill_variants: Option<bool>,
}

// ── thread/goal/* ─────────────────────────────────────────────────────────

#[derive(Debug, Deserialize, Clone, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ThreadGoal {
    pub thread_id: String,
    pub goal_id: String,
    pub objective: String,
    pub status: String,
    pub token_budget: Option<i64>,
    pub tokens_used: TokenUsage,
    pub time_used_seconds: i64,
    pub created_at: String,
    pub updated_at: String,
}

#[derive(Debug, Deserialize, Clone, Default, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TokenUsage {
    pub input_tokens: i64,
    pub output_tokens: i64,
    pub cached_input_tokens: i64,
    pub cache_write_input_tokens: i64,
    pub reasoning_output_tokens: i64,
    pub total_tokens: i64,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ThreadGoalGetResult {
    pub goal: Option<ThreadGoal>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ThreadGoalSetResult {
    pub goal: ThreadGoal,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ThreadGoalClearResult {
    pub cleared: bool,
}

// ── command/* ──────────────────────────────────────────────────────────────

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct CommandInfo {
    pub name: String,
    pub aliases: Vec<String>,
    pub description: String,
    #[serde(default)]
    pub description_key: String,
    #[serde(default)]
    pub fallback_description: String,
    pub category: String,
    pub requires_admin: bool,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CommandListResult {
    pub commands: Vec<CommandInfo>,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct CommandExecuteResult {
    pub handled: bool,
    pub message: Option<String>,
    pub is_markdown: bool,
    pub expanded_prompt: Option<String>,
    pub session_reset: Option<bool>,
    pub thread: Option<CommandExecuteThread>,
    pub archived_thread_ids: Option<Vec<String>>,
    pub created_lazily: Option<bool>,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct CommandExecuteThread {
    pub id: String,
    pub display_name: Option<String>,
}

// ── skills/* ──────────────────────────────────────────────────────────────

#[derive(Debug, Deserialize, Clone, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct SkillInfo {
    pub name: String,
    pub description: String,
    pub display_name: Option<String>,
    pub short_description: Option<String>,
    pub source: String,
    pub plugin_id: Option<String>,
    pub plugin_display_name: Option<String>,
    pub available: bool,
    pub unavailable_reason: Option<String>,
    pub enabled: bool,
    pub path: String,
    pub has_variant: Option<bool>,
    pub default_prompt: Option<String>,
    pub metadata: Option<std::collections::HashMap<String, String>>,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct SkillsListResult {
    pub skills: Vec<SkillInfo>,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct SkillsSetEnabledResult {
    pub skill: SkillInfo,
}

#[derive(Debug, Deserialize, Clone, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ProviderInfo {
    pub id: String,
    pub display_name: String,
    pub protocol: String,
    pub has_api_key: bool,
    pub end_point: String,
    pub is_implicit: bool,
}

#[derive(Debug, Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct ProviderListResult {
    pub providers: Vec<ProviderInfo>,
}

#[cfg(test)]
mod tests {
    use super::{ClientCapabilities, InitializeResult};

    #[test]
    fn client_capabilities_serialize_command_execution_streaming() {
        let caps = ClientCapabilities {
            approval_support: true,
            request_user_input_support: true,
            streaming_support: true,
            command_execution_streaming: true,
            tool_execution_lifecycle: true,
            opt_out_notification_methods: vec![],
        };

        let json = serde_json::to_value(caps).expect("serialize");
        assert_eq!(
            json.get("commandExecutionStreaming")
                .and_then(|v| v.as_bool()),
            Some(true)
        );
        assert_eq!(
            json.get("toolExecutionLifecycle").and_then(|v| v.as_bool()),
            Some(true)
        );
    }

    #[test]
    fn initialize_result_parses_provider_management_capability() {
        let result: InitializeResult = serde_json::from_value(serde_json::json!({
            "serverInfo": {
                "name": "DotCraft",
                "version": "0.1.0",
                "protocolVersion": "0.1.0"
            },
            "capabilities": {
                "providerManagement": true,
                "modelCatalogManagement": true
            }
        }))
        .expect("deserialize");

        assert_eq!(result.capabilities.provider_management, Some(true));
        assert_eq!(result.capabilities.model_catalog_management, Some(true));
    }
}
