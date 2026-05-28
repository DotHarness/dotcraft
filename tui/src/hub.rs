use anyhow::{anyhow, bail, Result};
use serde::Deserialize;
use serde_json::json;
use std::net::IpAddr;
use std::{
    path::{Path, PathBuf},
    process::{Command, Stdio},
    time::{Duration, Instant},
};
use tokio::time;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct HubLockInfo {
    pid: u32,
    api_base_url: String,
    token: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HubAppServerResponse {
    pub endpoints: std::collections::HashMap<String, String>,
}

#[derive(Debug, Deserialize)]
struct HubErrorResponse {
    error: HubError,
}

#[derive(Debug, Deserialize)]
struct HubError {
    code: String,
    message: String,
}

const STARTUP_TIMEOUT: Duration = Duration::from_secs(15);
const POLL_INTERVAL: Duration = Duration::from_millis(200);
const STATUS_REQUEST_TIMEOUT: Duration = Duration::from_secs(2);
const ENSURE_REQUEST_TIMEOUT: Duration = Duration::from_secs(60);

pub fn resolve_dotcraft_binary(explicit: Option<&str>) -> String {
    resolve_dotcraft_binary_with_current_exe(explicit, std::env::current_exe().ok().as_deref())
}

fn resolve_dotcraft_binary_with_current_exe(
    explicit: Option<&str>,
    current_exe: Option<&Path>,
) -> String {
    if let Some(path) = explicit.map(str::trim).filter(|path| !path.is_empty()) {
        return path.to_string();
    }

    if let Some(parent) = current_exe.and_then(Path::parent) {
        let sibling = parent.join(dotcraft_binary_file_name());
        if sibling.is_file() {
            return sibling.to_string_lossy().into_owned();
        }
    }

    "dotcraft".to_string()
}

fn dotcraft_binary_file_name() -> &'static str {
    if cfg!(windows) {
        "dotcraft.exe"
    } else {
        "dotcraft"
    }
}

pub async fn ensure_appserver(
    workspace_path: &std::path::Path,
    dotcraft_bin: &str,
) -> Result<String> {
    let hub = ensure_hub(dotcraft_bin).await?;
    let body = json!({
        "workspacePath": workspace_path,
        "client": {
            "name": "dotcraft-tui",
            "version": env!("CARGO_PKG_VERSION")
        },
        "startIfMissing": true
    });
    let client = HubHttpClient::new(ENSURE_REQUEST_TIMEOUT)?;
    let response: HubAppServerResponse = client
        .post_json(&hub, "/v1/appservers/ensure", &body)
        .await?;

    response
        .endpoints
        .get("appServerWebSocket")
        .filter(|url| !url.trim().is_empty())
        .cloned()
        .ok_or_else(|| anyhow!("Hub did not return an AppServer WebSocket endpoint"))
}

async fn ensure_hub(dotcraft_bin: &str) -> Result<HubLockInfo> {
    let mut last_error: Option<String> = None;
    match probe_live_hub().await {
        HubProbe::Live(hub) => return Ok(hub),
        HubProbe::Unavailable => {}
        HubProbe::Unhealthy(error) => last_error = Some(error),
    }

    Command::new(dotcraft_bin)
        .arg("hub")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|e| anyhow!("DotCraft Hub failed to start: {e}"))?;

    let deadline = Instant::now() + STARTUP_TIMEOUT;
    while Instant::now() < deadline {
        match probe_live_hub().await {
            HubProbe::Live(hub) => return Ok(hub),
            HubProbe::Unavailable => {}
            HubProbe::Unhealthy(error) => last_error = Some(error),
        }
        time::sleep(POLL_INTERVAL).await;
    }

    if let Some(error) = last_error {
        bail!("DotCraft Hub status probe failed: {error}")
    }
    bail!("DotCraft Hub could not be started")
}

enum HubProbe {
    Live(HubLockInfo),
    Unavailable,
    Unhealthy(String),
}

async fn probe_live_hub() -> HubProbe {
    let Some(hub) = read_hub_lock() else {
        return HubProbe::Unavailable;
    };
    probe_hub_lock(hub).await
}

async fn probe_hub_lock(hub: HubLockInfo) -> HubProbe {
    let client = match HubHttpClient::new(STATUS_REQUEST_TIMEOUT) {
        Ok(client) => client,
        Err(error) => return HubProbe::Unhealthy(error.to_string()),
    };
    match client.status(&hub).await {
        Ok(()) => HubProbe::Live(hub),
        Err(error) => HubProbe::Unhealthy(error.to_string()),
    }
}

fn read_hub_lock() -> Option<HubLockInfo> {
    let path = hub_lock_path()?;
    read_hub_lock_from_path(&path)
}

fn read_hub_lock_from_path(path: &Path) -> Option<HubLockInfo> {
    let content = std::fs::read_to_string(path).ok()?;
    let info: HubLockInfo = serde_json::from_str(&content).ok()?;
    is_process_alive(info.pid).then_some(info)
}

fn hub_lock_path() -> Option<PathBuf> {
    dirs::home_dir().map(|home| home.join(".craft").join("hub").join("hub.lock"))
}

struct HubHttpClient {
    client: reqwest::Client,
    timeout: Duration,
}

impl HubHttpClient {
    fn new(timeout: Duration) -> Result<Self> {
        let client = reqwest::Client::builder()
            .timeout(timeout)
            .no_proxy()
            .build()?;
        Ok(Self { client, timeout })
    }

    async fn status(&self, hub: &HubLockInfo) -> Result<()> {
        let response = self
            .client
            .get(hub_url(hub, "/v1/status")?)
            .bearer_auth(&hub.token)
            .send()
            .await
            .map_err(|error| self.request_error("GET", "/v1/status", error))?;

        if response.status().is_success() {
            Ok(())
        } else {
            Err(hub_response_error(response).await)
        }
    }

    async fn post_json<T: serde::de::DeserializeOwned>(
        &self,
        hub: &HubLockInfo,
        path: &str,
        body: &serde_json::Value,
    ) -> Result<T> {
        let response = self
            .client
            .post(hub_url(hub, path)?)
            .bearer_auth(&hub.token)
            .json(body)
            .send()
            .await
            .map_err(|error| self.request_error("POST", path, error))?;

        if response.status().is_success() {
            response
                .json::<T>()
                .await
                .map_err(|error| anyhow!("Hub returned invalid JSON for POST {path}: {error}"))
        } else {
            Err(hub_response_error(response).await)
        }
    }

    fn request_error(&self, method: &str, path: &str, error: reqwest::Error) -> anyhow::Error {
        if error.is_timeout() {
            anyhow!(
                "Hub request {method} {path} timed out after {}s",
                self.timeout.as_secs()
            )
        } else {
            anyhow!("Hub request {method} {path} failed: {error}")
        }
    }
}

async fn hub_response_error(response: reqwest::Response) -> anyhow::Error {
    let status = response.status();
    if let Ok(error) = response.json::<HubErrorResponse>().await {
        anyhow!("Hub {}: {}", error.error.code, error.error.message)
    } else {
        anyhow!("Hub request failed with HTTP {}", status.as_u16())
    }
}

fn hub_url(hub: &HubLockInfo, path: &str) -> Result<reqwest::Url> {
    let base = parse_hub_base_url(&hub.api_base_url)?;
    base.join(path.trim_start_matches('/'))
        .map_err(|error| anyhow!("Invalid Hub request path {path}: {error}"))
}

fn is_process_alive(pid: u32) -> bool {
    if pid == 0 {
        return false;
    }

    #[cfg(windows)]
    {
        let output = Command::new("tasklist")
            .args(["/FI", &format!("PID eq {pid}"), "/FO", "CSV", "/NH"])
            .output();
        let Ok(output) = output else {
            return false;
        };
        if !output.status.success() {
            return false;
        }
        String::from_utf8_lossy(&output.stdout).contains(&pid.to_string())
    }

    #[cfg(unix)]
    {
        Command::new("kill")
            .args(["-0", &pid.to_string()])
            .status()
            .map(|status| status.success())
            .unwrap_or(false)
    }

    #[cfg(not(any(windows, unix)))]
    {
        true
    }
}

fn parse_hub_base_url(url: &str) -> Result<reqwest::Url> {
    let mut parsed =
        reqwest::Url::parse(url.trim()).map_err(|error| anyhow!("Invalid Hub URL: {error}"))?;
    if parsed.scheme() != "http" {
        bail!("Hub URL must use http://");
    }
    if parsed.port().is_none() {
        bail!("Hub URL is missing a port");
    }
    let host = parsed
        .host_str()
        .ok_or_else(|| anyhow!("Hub URL is missing a host"))?;
    if !is_loopback_host(host) {
        bail!("Hub URL must be loopback");
    }
    if parsed.path() != "/" && !parsed.path().is_empty() {
        bail!("Hub URL must not include a path");
    }
    if parsed.query().is_some() || parsed.fragment().is_some() {
        bail!("Hub URL must not include a query or fragment");
    }

    parsed.set_path("");
    Ok(parsed)
}

fn is_loopback_host(host: &str) -> bool {
    let host = host.trim_start_matches('[').trim_end_matches(']');
    host.eq_ignore_ascii_case("localhost")
        || host
            .parse::<IpAddr>()
            .map(|address| address.is_loopback())
            .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::{
        dotcraft_binary_file_name, parse_hub_base_url, probe_hub_lock, read_hub_lock_from_path,
        resolve_dotcraft_binary_with_current_exe, HubHttpClient, HubLockInfo, HubProbe,
        ENSURE_REQUEST_TIMEOUT, STATUS_REQUEST_TIMEOUT,
    };
    use serde_json::json;
    use std::{
        fs,
        path::PathBuf,
        time::{SystemTime, UNIX_EPOCH},
    };
    use tokio::{
        io::{AsyncReadExt, AsyncWriteExt},
        net::TcpListener,
    };

    #[test]
    fn resolves_explicit_dotcraft_binary_first() {
        assert_eq!(
            resolve_dotcraft_binary_with_current_exe(Some("C:/tools/dotcraft.exe"), None),
            "C:/tools/dotcraft.exe"
        );
    }

    #[test]
    fn resolves_sibling_dotcraft_binary_before_path_fallback() {
        let dir = make_temp_dir("sibling-bin");
        let tui = dir.join(if cfg!(windows) {
            "dotcraft-tui.exe"
        } else {
            "dotcraft-tui"
        });
        let dotcraft = dir.join(dotcraft_binary_file_name());
        fs::write(&tui, "").unwrap();
        fs::write(&dotcraft, "").unwrap();

        let resolved = resolve_dotcraft_binary_with_current_exe(None, Some(&tui));
        assert_eq!(PathBuf::from(resolved), dotcraft);

        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn falls_back_to_path_dotcraft_when_no_sibling_exists() {
        let dir = make_temp_dir("path-fallback");
        let tui = dir.join(if cfg!(windows) {
            "dotcraft-tui.exe"
        } else {
            "dotcraft-tui"
        });
        fs::write(&tui, "").unwrap();

        let resolved = resolve_dotcraft_binary_with_current_exe(None, Some(&tui));
        assert_eq!(resolved, "dotcraft");

        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn ignores_hub_lock_when_pid_is_not_alive() {
        let dir = make_temp_dir("dead-lock");
        let lock_path = dir.join("hub.lock");
        fs::write(
            &lock_path,
            r#"{
  "pid": 0,
  "apiBaseUrl": "http://127.0.0.1:12345",
  "token": "token",
  "startedAt": "2026-05-12T00:00:00Z",
  "version": "test"
}"#,
        )
        .unwrap();

        assert!(read_hub_lock_from_path(&lock_path).is_none());

        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn validates_loopback_hub_urls() {
        assert!(parse_hub_base_url("http://127.0.0.1:12345").is_ok());
        assert!(parse_hub_base_url("http://127.42.0.1:12345").is_ok());
        assert!(parse_hub_base_url("http://localhost:12345").is_ok());
        assert!(parse_hub_base_url("http://[::1]:12345").is_ok());

        assert!(parse_hub_base_url("https://127.0.0.1:12345").is_err());
        assert!(parse_hub_base_url("http://127.0.0.1").is_err());
        assert!(parse_hub_base_url("http://192.168.1.5:12345").is_err());
        assert!(parse_hub_base_url("http://127.0.0.1:12345/hub").is_err());
    }

    #[tokio::test]
    async fn status_probe_success_returns_live_hub() {
        let base_url = spawn_one_response_server(
            b"HTTP/1.1 200 OK\r\ncontent-type: application/json\r\ncontent-length: 2\r\nconnection: close\r\n\r\n{}",
        )
        .await;
        let hub = test_hub(base_url);

        match probe_hub_lock(hub).await {
            HubProbe::Live(_) => {}
            HubProbe::Unavailable => panic!("expected live hub, got unavailable"),
            HubProbe::Unhealthy(error) => panic!("expected live hub, got error: {error}"),
        }
    }

    #[tokio::test]
    async fn malformed_status_response_does_not_use_handwritten_http_error() {
        let base_url = spawn_one_response_server(b"not a valid http response").await;
        let hub = test_hub(base_url);
        let error = HubHttpClient::new(STATUS_REQUEST_TIMEOUT)
            .unwrap()
            .status(&hub)
            .await
            .unwrap_err()
            .to_string();

        assert!(error.contains("Hub request GET /v1/status failed"));
        assert!(!error.contains("Invalid Hub HTTP response"));
    }

    #[tokio::test]
    async fn ensure_response_parses_websocket_endpoint() {
        let body = r#"{"endpoints":{"appServerWebSocket":"ws://127.0.0.1:4567/ws"}}"#;
        let response = format!(
            "HTTP/1.1 200 OK\r\ncontent-type: application/json\r\ncontent-length: {}\r\nconnection: close\r\n\r\n{}",
            body.len(),
            body
        );
        let base_url = spawn_one_response_server(response.as_bytes()).await;
        let hub = test_hub(base_url);
        let result: super::HubAppServerResponse = HubHttpClient::new(ENSURE_REQUEST_TIMEOUT)
            .unwrap()
            .post_json(&hub, "/v1/appservers/ensure", &json!({}))
            .await
            .unwrap();

        assert_eq!(
            result.endpoints.get("appServerWebSocket"),
            Some(&"ws://127.0.0.1:4567/ws".to_string())
        );
    }

    #[tokio::test]
    async fn hub_error_response_is_preferred_for_non_success_status() {
        let body =
            r#"{"error":{"code":"invalidWorkspace","message":"Workspace path is required."}}"#;
        let response = format!(
            "HTTP/1.1 400 Bad Request\r\ncontent-type: application/json\r\ncontent-length: {}\r\nconnection: close\r\n\r\n{}",
            body.len(),
            body
        );
        let base_url = spawn_one_response_server(response.as_bytes()).await;
        let hub = test_hub(base_url);
        let error = HubHttpClient::new(ENSURE_REQUEST_TIMEOUT)
            .unwrap()
            .post_json::<serde_json::Value>(&hub, "/v1/appservers/ensure", &json!({}))
            .await
            .unwrap_err()
            .to_string();

        assert_eq!(error, "Hub invalidWorkspace: Workspace path is required.");
    }

    async fn spawn_one_response_server(response: &[u8]) -> String {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let response = response.to_vec();
        tokio::spawn(async move {
            let (mut socket, _) = listener.accept().await.unwrap();
            let mut request = [0_u8; 4096];
            let _ = socket.read(&mut request).await;
            socket.write_all(&response).await.unwrap();
        });
        format!("http://{address}")
    }

    fn test_hub(api_base_url: String) -> HubLockInfo {
        HubLockInfo {
            pid: std::process::id(),
            api_base_url,
            token: "test-token".to_string(),
        }
    }

    fn make_temp_dir(label: &str) -> PathBuf {
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir = std::env::temp_dir().join(format!(
            "dotcraft-tui-hub-test-{label}-{}-{now}",
            std::process::id()
        ));
        fs::create_dir_all(&dir).unwrap();
        dir
    }
}
