//! Human-readable tool invocation and result summaries aligned with DotCraft.Core
//! `ToolRegistry` / `CoreToolDisplays` (CLI `SessionHistoryPrinter` / `StreamAdapter`).

use crate::app::state::PlanTodo;
use serde_json::Value;
use unicode_width::UnicodeWidthChar;

/// Names of built-in DotCraft tools (PascalCase) that TUI recognises and renders
/// with bespoke streaming copy. Tools outside this set (e.g. MCP / external
/// modules) fall back to a generic "Generating parameters..." placeholder and
/// never show raw argument JSON to the user while the call is in flight.
pub const BUILTIN_TOOLS: &[&str] = &[
    "ReadFile",
    "WriteFile",
    "EditFile",
    "GrepFiles",
    "FindFiles",
    "Exec",
    "WebSearch",
    "WebFetch",
    "SpawnAgent",
    "WaitAgent",
    "SendInput",
    "ResumeAgent",
    "CloseAgent",
    "LSP",
    "SearchTools",
    "tool_search",
    "Cron",
    "CommitSuggest",
    "CreatePlan",
    "UpdateTodos",
    "TodoWrite",
    "RequestUserInput",
];

/// Returns `true` when the tool name matches a built-in DotCraft tool.
pub fn is_builtin_tool(tool_name: &str) -> bool {
    BUILTIN_TOOLS.iter().any(|n| *n == tool_name)
}

/// Full-sentence invocations from `CoreToolDisplays` (e.g. `Searched "…"`). These must not be
/// combined with the TUI `Calling`/`Called` prefixes — see [`invocation_needs_calling_called_prefix`].
fn try_standalone_invocation_sentence(
    tool_name: &str,
    args: &str,
    plan_todos: Option<&[PlanTodo]>,
) -> Option<String> {
    match tool_name {
        "WebSearch" => parse_query_field(args).map(|q| {
            let t = truncate_chars(&q, 80);
            format!("Searched \"{t}\"")
        }),
        "WebFetch" => parse_string_field(args, "url").map(|u| {
            let t = truncate_chars(&u, 80);
            format!("Fetched {t}")
        }),
        name if is_tool_search_tool(name) => parse_tool_search_query_field(args).map(|q| {
            let t = truncate_chars(&q, 60);
            format!("Searched tools: \"{t}\"")
        }),
        "ReadFile" => format_read_file_label(args),
        "WriteFile" | "EditFile" => format_file_edit_label(args),
        "TodoWrite" => format_todo_write_label(args),
        "UpdateTodos" => format_update_todos_label(args, plan_todos),
        "RequestUserInput" => Some(format_request_user_input_label(args)),
        _ => None,
    }
}

/// Streaming-friendly sentence for in-flight tool calls.
///
/// For built-in tools we render bespoke, human-readable present-progress copy
/// derived from tolerantly parsed partial JSON. For unknown tools (e.g. MCP or
/// module-contributed tools) we render a generic `"Generating parameters for X..."`
/// placeholder so the user never sees a raw argument JSON dump mid-stream.
pub fn format_active_invocation_display(
    tool_name: &str,
    args: &str,
    plan_todos: Option<&[PlanTodo]>,
) -> String {
    match tool_name {
        "WriteFile" => format_file_running_label(args, "Writing to", "Writing file"),
        "EditFile" => format_file_running_label(args, "Editing", "Editing file"),
        "ReadFile" => format_read_file_running_label(args),
        "GrepFiles" => format_grep_running_label(args),
        "FindFiles" => format_find_running_label(args),
        "Exec" => format_exec_running_label(args),
        "WebSearch" => format_web_search_running_label(args),
        "WebFetch" => format_web_fetch_running_label(args),
        "SpawnAgent" => format_spawn_agent_running_label(args),
        "LSP" => format_lsp_running_label(args),
        name if is_tool_search_tool(name) => format_search_tools_running_label(args),
        "Cron" => format_cron_running_label(args),
        "CommitSuggest" => "Preparing commit message...".to_string(),
        "CreatePlan" => format_create_plan_running_label(args),
        "TodoWrite" | "UpdateTodos" => format_todos_running_label(args, plan_todos),
        "RequestUserInput" => format_request_user_input_running_label(args),
        _ if is_builtin_tool(tool_name) => format!("Calling {tool_name}..."),
        _ => format!("Generating parameters for {tool_name}..."),
    }
}

/// Format a tool invocation line like `ToolRegistry.FormatToolCall` for known tools;
/// falls back to the legacy TUI heuristic (single string field or truncated JSON).
pub fn format_invocation_display(tool_name: &str, args: &str) -> String {
    format_invocation_display_with_plan(tool_name, args, None)
}

/// Same as [`format_invocation_display`] but can use plan todos for UpdateTodos summaries.
pub fn format_invocation_display_with_plan(
    tool_name: &str,
    args: &str,
    plan_todos: Option<&[PlanTodo]>,
) -> String {
    try_standalone_invocation_sentence(tool_name, args, plan_todos)
        .unwrap_or_else(|| format_generic_invocation(tool_name, args))
}

/// Completed invocation labels that need tool results, such as `Searched 3 tools`.
pub fn format_completed_invocation_display_with_result(
    tool_name: &str,
    _args: &str,
    result: &str,
    _plan_todos: Option<&[PlanTodo]>,
) -> Option<String> {
    if !is_tool_search_tool(tool_name) {
        return None;
    }

    let count = count_tool_search_result(result)?;
    Some(format!(
        "Searched {count} tool{}",
        if count == 1 { "" } else { "s" }
    ))
}

/// When `true`, [`crate::ui::chat_view::ChatView`] should prefix the line with localized
/// `Calling` / `Called`. When `false`, [`format_invocation_display`] is already a full sentence
/// (standalone tools), so those prefixes are omitted to avoid double verbs (e.g. `Calling Searched "…"`).
pub fn invocation_needs_calling_called_prefix(tool_name: &str, args: &str) -> bool {
    invocation_needs_calling_called_prefix_with_plan(tool_name, args, None)
}

/// Same as [`invocation_needs_calling_called_prefix`] with optional plan context.
pub fn invocation_needs_calling_called_prefix_with_plan(
    tool_name: &str,
    args: &str,
    plan_todos: Option<&[PlanTodo]>,
) -> bool {
    try_standalone_invocation_sentence(tool_name, args, plan_todos).is_none()
}

fn parse_query_field(args: &str) -> Option<String> {
    parse_string_field(args, "query")
}

fn parse_tool_search_query_field(args: &str) -> Option<String> {
    parse_string_field(args, "query").or_else(|| parse_string_field(args, "q"))
}

fn is_tool_search_tool(tool_name: &str) -> bool {
    matches!(tool_name, "SearchTools" | "tool_search")
}

fn parse_string_field(args: &str, key: &str) -> Option<String> {
    let v: Value = serde_json::from_str(args).ok()?;
    v.get(key)?.as_str().map(str::to_string)
}

fn parse_json(args: &str) -> Option<Value> {
    serde_json::from_str(args).ok()
}

fn parse_question_count(args: &str) -> Option<usize> {
    let parsed = parse_json(args)?;
    let count = parsed.get("questions")?.as_array()?.len();
    (count > 0).then_some(count)
}

fn format_request_user_input_label(args: &str) -> String {
    match parse_question_count(args) {
        Some(1) => "Ask 1 question".to_string(),
        Some(count) => format!("Ask {count} questions"),
        None => "Ask questions".to_string(),
    }
}

fn format_request_user_input_running_label(args: &str) -> String {
    match parse_question_count(args) {
        Some(1) => "Asking 1 question...".to_string(),
        Some(count) => format!("Asking {count} questions..."),
        None => "Asking questions...".to_string(),
    }
}

struct RequestUserInputQuestionDisplay {
    id: String,
    label: String,
    is_secret: bool,
}

fn extract_filename(path: &str) -> &str {
    path.rsplit(['/', '\\']).next().unwrap_or(path)
}

fn normalize_status(value: &str) -> String {
    value.trim().to_ascii_lowercase()
}

fn parse_positive_int(value: &Value) -> Option<i64> {
    match value {
        Value::Number(n) => n.as_i64().filter(|n| *n > 0),
        Value::String(s) => s.trim().parse::<i64>().ok().filter(|n| *n > 0),
        _ => None,
    }
}

fn get_string(v: &Value, key: &str) -> Option<String> {
    v.get(key)?.as_str().map(str::to_string)
}

fn get_bool(v: &Value, key: &str) -> Option<bool> {
    v.get(key)?.as_bool()
}

fn format_read_file_label(args: &str) -> Option<String> {
    let parsed = parse_json(args)?;
    let path = get_string(&parsed, "path")?;
    let filename = extract_filename(&path);

    let start = parsed.get("offset").and_then(parse_positive_int);
    let limit = parsed.get("limit").and_then(parse_positive_int);

    if let (Some(s), Some(l)) = (start, limit) {
        return Some(format!("Read {filename} L{s}-{}", s + l - 1));
    }
    if let Some(s) = start {
        return Some(format!("Read {filename} from L{s}"));
    }
    Some(format!("Read {filename}"))
}

fn format_read_file_running_label(args: &str) -> String {
    let parsed = match parse_json(args) {
        Some(v) => v,
        None => return "Reading file...".to_string(),
    };
    let path = match get_string(&parsed, "path") {
        Some(p) => p,
        None => return "Reading file...".to_string(),
    };
    let filename = extract_filename(&path);
    let start = parsed.get("offset").and_then(parse_positive_int);
    let limit = parsed.get("limit").and_then(parse_positive_int);
    if let (Some(s), Some(l)) = (start, limit) {
        return format!("Reading {filename} L{s}-{}...", s + l - 1);
    }
    if let Some(s) = start {
        return format!("Reading {filename} from L{s}...");
    }
    format!("Reading {filename}...")
}

fn format_file_edit_label(args: &str) -> Option<String> {
    let parsed = parse_json(args)?;
    let path = get_string(&parsed, "path")?;
    let filename = extract_filename(&path);
    Some(format!("Edited {filename}"))
}

fn format_file_running_label(args: &str, action_with_name: &str, action_generic: &str) -> String {
    // Prefer tolerant partial-JSON extraction so running labels render on the
    // first chunk — waiting for a fully parseable JSON defeats the streaming UX.
    if let Some(path) = extract_partial_json_string_value(args, "path") {
        return format!("{action_with_name} {}...", extract_filename(&path));
    }
    format!("{action_generic}...")
}

fn format_grep_running_label(args: &str) -> String {
    let pattern = extract_partial_json_string_value(args, "pattern");
    let path = extract_partial_json_string_value(args, "path").filter(|p| !p.is_empty());
    match (pattern, path) {
        (Some(p), Some(dir)) => {
            let p = truncate_chars(&p, 40);
            format!("Searching \"{p}\" in {dir}...")
        }
        (Some(p), None) => {
            let p = truncate_chars(&p, 40);
            format!("Searching \"{p}\"...")
        }
        (None, Some(dir)) => format!("Searching files in {dir}..."),
        (None, None) => "Searching files...".to_string(),
    }
}

fn format_find_running_label(args: &str) -> String {
    let pattern = extract_partial_json_string_value(args, "pattern");
    let path = extract_partial_json_string_value(args, "path").filter(|p| !p.is_empty());
    match (pattern, path) {
        (Some(p), Some(dir)) => {
            let p = truncate_chars(&p, 40);
            format!("Finding \"{p}\" in {dir}...")
        }
        (Some(p), None) => {
            let p = truncate_chars(&p, 40);
            format!("Finding \"{p}\"...")
        }
        _ => "Finding files...".to_string(),
    }
}

fn format_exec_running_label(args: &str) -> String {
    match extract_partial_json_string_value(args, "command") {
        Some(cmd) => {
            let one_line = cmd.lines().next().unwrap_or(&cmd).to_string();
            let compact = truncate_chars(&one_line, 80);
            format!("Running: {compact}")
        }
        None => "Running command...".to_string(),
    }
}

fn format_web_search_running_label(args: &str) -> String {
    match extract_partial_json_string_value(args, "query") {
        Some(q) => {
            let t = truncate_chars(&q, 80);
            format!("Searching the web for \"{t}\"...")
        }
        None => "Searching the web...".to_string(),
    }
}

fn format_web_fetch_running_label(args: &str) -> String {
    match extract_partial_json_string_value(args, "url") {
        Some(u) => {
            let t = truncate_chars(&u, 80);
            format!("Fetching {t}...")
        }
        None => "Fetching URL...".to_string(),
    }
}

fn format_spawn_agent_running_label(args: &str) -> String {
    let label = extract_partial_json_string_value(args, "agentNickname").filter(|s| !s.is_empty());
    let task = extract_partial_json_string_value(args, "agentPrompt").filter(|s| !s.is_empty());
    let profile = extract_partial_json_string_value(args, "profile").filter(|s| !s.is_empty());
    match (label, task, profile) {
        (Some(l), _, _) => {
            let t = truncate_chars(&l, 60);
            format!("Spawning agent: {t}...")
        }
        (None, Some(t), _) => {
            let t = truncate_chars(&t, 60);
            format!("Spawning agent for: {t}...")
        }
        (None, None, Some(p)) => {
            let t = truncate_chars(&p, 40);
            format!("Spawning {t} agent...")
        }
        _ => "Spawning agent...".to_string(),
    }
}

fn format_lsp_running_label(args: &str) -> String {
    let op = extract_partial_json_string_value(args, "operation").filter(|s| !s.is_empty());
    let file = extract_partial_json_string_value(args, "filePath").filter(|s| !s.is_empty());
    match (op, file) {
        (Some(o), Some(f)) => format!("Running LSP {o} on {}...", extract_filename(&f)),
        (Some(o), None) => format!("Running LSP {o}..."),
        (None, Some(f)) => format!("Running LSP on {}...", extract_filename(&f)),
        _ => "Running LSP...".to_string(),
    }
}

fn format_search_tools_running_label(args: &str) -> String {
    match extract_partial_json_string_value(args, "query")
        .or_else(|| extract_partial_json_string_value(args, "q"))
    {
        Some(q) => {
            let t = truncate_chars(&q, 60);
            format!("Searching tools: \"{t}\"...")
        }
        None => "Searching tools...".to_string(),
    }
}

fn format_cron_running_label(args: &str) -> String {
    let action = extract_partial_json_string_value(args, "action").filter(|s| !s.is_empty());
    match action.as_deref() {
        Some("add") => "Scheduling cron job...".to_string(),
        Some("list") => "Listing cron jobs...".to_string(),
        Some("remove") => "Removing cron job...".to_string(),
        Some(other) => format!("Running cron {other}..."),
        None => "Configuring cron...".to_string(),
    }
}

fn format_create_plan_running_label(args: &str) -> String {
    match extract_partial_json_string_value(args, "title") {
        Some(t) if !t.is_empty() => {
            let compact = truncate_chars(&t, 60);
            format!("Drafting plan: {compact}...")
        }
        _ => "Drafting plan...".to_string(),
    }
}

fn format_todos_running_label(args: &str, _plan_todos: Option<&[PlanTodo]>) -> String {
    // During streaming the todos array may not be valid JSON yet; a short
    // placeholder is better than dumping raw JSON to the terminal.
    let _ = args;
    "Updating to-dos...".to_string()
}

fn truncate_summary(content: &str) -> String {
    let compact = content.split_whitespace().collect::<Vec<_>>().join(" ");
    if compact.is_empty() {
        return String::new();
    }
    truncate_chars(&compact, 28)
}

fn format_todo_write_label(args: &str) -> Option<String> {
    let parsed = parse_json(args)?;
    let todos = parsed.get("todos")?.as_array()?;
    if todos.is_empty() {
        return None;
    }
    let merge = get_bool(&parsed, "merge").unwrap_or(false);
    let has_in_progress = todos.iter().any(|todo| {
        todo.get("status")
            .and_then(|v| v.as_str())
            .map(normalize_status)
            .as_deref()
            == Some("in_progress")
    });
    let prefer_started = merge && has_in_progress;
    let chosen = if prefer_started {
        todos.iter().find(|todo| {
            todo.get("status")
                .and_then(|v| v.as_str())
                .map(normalize_status)
                .as_deref()
                == Some("in_progress")
        })
    } else {
        todos.first()
    }
    .or_else(|| todos.first())?;

    let summary = chosen
        .get("content")
        .and_then(|v| v.as_str())
        .map(truncate_summary)
        .filter(|s| !s.is_empty());

    if !merge {
        return Some(match summary {
            Some(s) => format!("Create to-do {s}"),
            None => "Create to-do".to_string(),
        });
    }
    if has_in_progress {
        return Some(match summary {
            Some(s) => format!("Started to-do {s}"),
            None => "Started to-do".to_string(),
        });
    }
    Some(match summary {
        Some(s) => format!("Updated to-do {s}"),
        None => "Updated to-do".to_string(),
    })
}

fn format_update_todos_label(args: &str, plan_todos: Option<&[PlanTodo]>) -> Option<String> {
    let parsed = parse_json(args)?;
    let updates = parsed.get("updates")?.as_array()?;
    if updates.is_empty() {
        return None;
    }
    let has_in_progress = updates.iter().any(|todo| {
        todo.get("status")
            .and_then(|v| v.as_str())
            .map(normalize_status)
            .as_deref()
            == Some("in_progress")
    });
    let chosen = if has_in_progress {
        updates.iter().find(|todo| {
            todo.get("status")
                .and_then(|v| v.as_str())
                .map(normalize_status)
                .as_deref()
                == Some("in_progress")
        })
    } else {
        updates.first()
    }
    .or_else(|| updates.first())?;

    let summary = chosen
        .get("id")
        .and_then(|v| v.as_str())
        .and_then(|id| {
            plan_todos.and_then(|todos| {
                todos
                    .iter()
                    .find(|todo| todo.id.trim() == id.trim())
                    .map(|todo| truncate_summary(&todo.content))
            })
        })
        .filter(|s| !s.is_empty());

    if has_in_progress {
        return Some(match summary {
            Some(s) => format!("Started to-do {s}"),
            None => "Started to-do".to_string(),
        });
    }
    Some(match summary {
        Some(s) => format!("Updated to-do {s}"),
        None => "Updated to-do".to_string(),
    })
}

/// Best-effort extraction of a string field from partial JSON deltas.
/// This supports streaming argument buffers that may not be valid JSON yet.
pub fn extract_partial_json_string_value(json: &str, key: &str) -> Option<String> {
    let key_pat = format!("\"{key}\"");
    let key_idx = json.find(&key_pat)?;
    let after_key = &json[key_idx + key_pat.len()..];
    let colon_idx = after_key.find(':')?;
    let after_colon = &after_key[colon_idx + 1..];
    let quote_start_rel = after_colon.find('"')?;
    let rest = &after_colon[quote_start_rel + 1..];

    let mut escaped = false;
    let mut out = String::new();
    for ch in rest.chars() {
        if escaped {
            match ch {
                'n' => out.push('\n'),
                'r' => out.push('\r'),
                't' => out.push('\t'),
                'b' => out.push('\u{0008}'),
                'f' => out.push('\u{000C}'),
                '\\' => out.push('\\'),
                '"' => out.push('"'),
                '/' => out.push('/'),
                other => {
                    out.push('\\');
                    out.push(other);
                }
            }
            escaped = false;
            continue;
        }
        if ch == '\\' {
            escaped = true;
            continue;
        }
        if ch == '"' {
            return Some(out);
        }
        out.push(ch);
    }
    Some(out)
}

fn format_generic_invocation(name: &str, args: &str) -> String {
    if args.is_empty() {
        return format!("{name}()");
    }
    // Non-built-in tools (MCP, external modules) never surface raw argument
    // JSON to the user — we only show the tool name.
    if !is_builtin_tool(name) {
        return name.to_string();
    }
    if let Ok(v) = serde_json::from_str::<Value>(args) {
        if let Some(obj) = v.as_object() {
            if obj.len() == 1 {
                let val = obj.values().next().unwrap();
                if let Some(s) = val.as_str() {
                    return format!("{name}(\"{s}\")");
                }
            }
        }
    }
    let compact = truncate_display_width(args, 60);
    format!("{name}({compact})")
}

/// Truncate to at most `max` Unicode scalar values; append `...` like C# `ToolDisplayHelpers.Truncate`.
fn truncate_chars(s: &str, max: usize) -> String {
    let it = s.chars();
    let count = it.clone().count();
    if count <= max {
        return s.to_string();
    }
    it.take(max).collect::<String>() + "..."
}

/// Match `chat_view::truncate`: display-width–aware truncation for raw JSON fallback.
fn truncate_display_width(s: &str, max_cols: usize) -> String {
    if max_cols == 0 {
        return String::new();
    }
    let total_width: usize = s
        .chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum();
    if total_width <= max_cols {
        return s.to_string();
    }
    let mut width: usize = 0;
    let mut out = String::new();
    for c in s.chars() {
        let cw = UnicodeWidthChar::width(c).unwrap_or(0);
        if width + cw > max_cols.saturating_sub(1) {
            out.push('…');
            return out;
        }
        out.push(c);
        width += cw;
    }
    out
}

/// Structured result lines like `ToolRegistry.FormatToolResult`; `None` means use raw text lines.
pub fn format_result_summary(tool_name: &str, result: &str) -> Option<Vec<String>> {
    format_result_summary_with_args(tool_name, None, result)
}

/// Structured result lines that may use invocation arguments to improve result labels.
pub fn format_result_summary_with_args(
    tool_name: &str,
    args: Option<&str>,
    result: &str,
) -> Option<Vec<String>> {
    let trimmed = result.trim();
    if trimmed.is_empty() {
        return None;
    }
    match tool_name {
        "WebSearch" => {
            let root: Value = serde_json::from_str(trimmed).ok()?;
            let root = peel_json_string_wrapper(root)?;
            parse_web_search_result(&root)
        }
        "WebFetch" => {
            let root: Value = serde_json::from_str(trimmed).ok()?;
            let root = peel_json_string_wrapper(root)?;
            parse_web_fetch_result(&root)
        }
        name if is_tool_search_tool(name) => parse_tool_search_result(trimmed),
        "RequestUserInput" => format_request_user_input_result_summary(args, trimmed),
        _ => None,
    }
}

fn format_request_user_input_result_summary(
    args: Option<&str>,
    result: &str,
) -> Option<Vec<String>> {
    let root: Value = serde_json::from_str(result).ok()?;
    let root = peel_json_string_wrapper(root)?;
    let answers = root.get("answers")?.as_object()?;

    let questions = parse_request_user_input_questions(args);
    let entries = if questions.is_empty() {
        answers
            .keys()
            .map(|id| RequestUserInputQuestionDisplay {
                id: id.to_string(),
                label: id.to_string(),
                is_secret: false,
            })
            .collect()
    } else {
        questions
    };

    if entries.is_empty() {
        return None;
    }

    Some(
        entries
            .into_iter()
            .map(|question| {
                let answer =
                    format_request_user_input_answer(answers.get(&question.id), question.is_secret);
                format!("{}: {answer}", question.label)
            })
            .collect(),
    )
}

fn parse_request_user_input_questions(args: Option<&str>) -> Vec<RequestUserInputQuestionDisplay> {
    let Some(args) = args else {
        return Vec::new();
    };
    let Some(parsed) = parse_json(args) else {
        return Vec::new();
    };
    let Some(questions) = parsed.get("questions").and_then(|v| v.as_array()) else {
        return Vec::new();
    };

    questions
        .iter()
        .filter_map(|question| {
            let id = question.get("id")?.as_str()?.trim();
            if id.is_empty() {
                return None;
            }
            let prompt = question
                .get("question")
                .and_then(|v| v.as_str())
                .map(str::trim)
                .filter(|s| !s.is_empty())
                .or_else(|| {
                    question
                        .get("header")
                        .and_then(|v| v.as_str())
                        .map(str::trim)
                        .filter(|s| !s.is_empty())
                })
                .unwrap_or(id);
            Some(RequestUserInputQuestionDisplay {
                id: id.to_string(),
                label: prompt.to_string(),
                is_secret: question
                    .get("isSecret")
                    .and_then(|v| v.as_bool())
                    .unwrap_or(false),
            })
        })
        .collect()
}

fn format_request_user_input_answer(answer: Option<&Value>, is_secret: bool) -> String {
    let values: Vec<String> = answer
        .and_then(|answer| answer.get("answers"))
        .and_then(|answers| answers.as_array())
        .map(|answers| {
            answers
                .iter()
                .filter_map(|value| value.as_str())
                .map(str::trim)
                .filter(|value| !value.is_empty())
                .map(|value| format_request_user_input_answer_value(value, is_secret))
                .filter(|value| !value.is_empty())
                .collect()
        })
        .unwrap_or_default();

    if values.is_empty() {
        "No answer".to_string()
    } else {
        values.join(", ")
    }
}

fn format_request_user_input_answer_value(value: &str, is_secret: bool) -> String {
    let lower = value.to_ascii_lowercase();
    if let Some(rest) = lower.strip_prefix("user_note:") {
        let note_start = value.len() - rest.len();
        let note = value[note_start..].trim();
        if is_secret && !note.is_empty() {
            return "Hidden".to_string();
        }
        return note.to_string();
    }
    value.to_string()
}

fn peel_json_string_wrapper(root: Value) -> Option<Value> {
    match root {
        Value::String(s) => serde_json::from_str(&s).ok(),
        o => Some(o),
    }
}

fn parse_web_search_result(root: &Value) -> Option<Vec<String>> {
    let obj = root.as_object()?;

    if let Some(err) = obj.get("error") {
        let msg = err.as_str()?.trim();
        if msg.is_empty() {
            return None;
        }
        return Some(vec![format!("Error: {msg}")]);
    }

    let results = obj.get("results")?.as_array()?;
    let count = results.len();

    if count == 0 {
        return Some(vec!["No results found.".to_string()]);
    }

    let mut lines = Vec::new();
    lines.push(format!(
        "{} result{}:",
        count,
        if count == 1 { "" } else { "s" }
    ));

    for (idx, item) in results.iter().enumerate() {
        let title = item.get("title").and_then(|v| v.as_str());
        let url = item.get("url").and_then(|v| v.as_str());
        let title_text = truncate_chars(title.or(url).unwrap_or("?"), 70);
        let line = if let Some(u) = url {
            if let Some(domain) = host_from_url(u) {
                if domain.is_empty() {
                    format!("{}. {}", idx + 1, title_text)
                } else {
                    format!("{}. {} — {}", idx + 1, title_text, domain)
                }
            } else {
                format!("{}. {}", idx + 1, title_text)
            }
        } else {
            format!("{}. {}", idx + 1, title_text)
        };
        lines.push(line);
    }

    Some(lines)
}

fn host_from_url(url: &str) -> Option<String> {
    let rest = url
        .strip_prefix("https://")
        .or_else(|| url.strip_prefix("http://"))
        .or_else(|| url.strip_prefix("ftp://"))?;
    let host_port = rest.split(|c| c == '/' || c == '?' || c == '#').next()?;
    let host = host_port.rsplit('@').next().unwrap_or(host_port);
    if host.is_empty() {
        None
    } else {
        Some(host.to_string())
    }
}

fn parse_web_fetch_result(root: &Value) -> Option<Vec<String>> {
    let obj = root.as_object()?;

    if let Some(err) = obj.get("error") {
        let msg = err.as_str()?.trim();
        if msg.is_empty() {
            return None;
        }
        return Some(vec![format!("Error: {msg}")]);
    }

    let mut parts: Vec<String> = Vec::new();

    if let Some(n) = obj.get("status").and_then(json_number_to_i32) {
        parts.push(n.to_string());
    }

    if let Some(len) = obj.get("length").and_then(json_number_to_i64) {
        parts.push(format!("{} chars", format_int_grouped(len)));
    }

    if let Some(ext) = obj.get("extractor").and_then(|v| v.as_str()) {
        let t = ext.trim();
        if !t.is_empty() {
            parts.push(t.to_string());
        }
    }

    if obj
        .get("truncated")
        .and_then(|v| v.as_bool())
        .unwrap_or(false)
    {
        parts.push("truncated".to_string());
    }

    if parts.is_empty() {
        None
    } else {
        Some(vec![parts.join(" · ")])
    }
}

struct ToolSearchDisplayTool {
    name: String,
    description: String,
}

fn parse_tool_search_result(result: &str) -> Option<Vec<String>> {
    if let Ok(root) = serde_json::from_str::<Value>(result) {
        if let Some(root) = peel_json_string_wrapper(root) {
            if let Some(lines) = parse_tool_search_json_result(&root) {
                return Some(lines);
            }
        }
    }

    parse_tool_search_text_result(result)
}

fn parse_tool_search_json_result(root: &Value) -> Option<Vec<String>> {
    let tools_value = root.get("tools")?.as_array()?;
    let mut tools = Vec::new();
    collect_tool_search_tools(tools_value, None, &mut tools);

    if tools.is_empty() {
        return Some(vec!["No matching tools found.".to_string()]);
    }

    let mut lines = Vec::with_capacity(tools.len() + 1);
    lines.push(format!("Found {} matching tool(s):", tools.len()));
    lines.extend(tools.into_iter().map(|tool| {
        if tool.description.trim().is_empty() {
            format!("- {}", tool.name)
        } else {
            format!("- {}: {}", tool.name, tool.description.trim())
        }
    }));
    Some(lines)
}

fn collect_tool_search_tools(
    tools: &[Value],
    namespace_name: Option<&str>,
    output: &mut Vec<ToolSearchDisplayTool>,
) {
    for tool in tools {
        let Some(obj) = tool.as_object() else {
            continue;
        };
        let tool_type = obj
            .get("type")
            .and_then(|v| v.as_str())
            .map(|s| s.trim().to_ascii_lowercase())
            .unwrap_or_default();
        let name = obj
            .get("name")
            .and_then(|v| v.as_str())
            .map(str::trim)
            .unwrap_or_default();

        if tool_type == "namespace" {
            if let Some(children) = obj.get("tools").and_then(|v| v.as_array()) {
                let child_namespace = if name.is_empty() {
                    namespace_name
                } else {
                    Some(name)
                };
                collect_tool_search_tools(children, child_namespace, output);
                continue;
            }
        }

        if name.is_empty() {
            continue;
        }

        let display_name = match namespace_name {
            Some(namespace) if !namespace.is_empty() => format!("{namespace}.{name}"),
            _ => name.to_string(),
        };
        let description = obj
            .get("description")
            .and_then(|v| v.as_str())
            .map(str::trim)
            .unwrap_or_default()
            .to_string();
        output.push(ToolSearchDisplayTool {
            name: display_name,
            description,
        });
    }
}

fn parse_tool_search_text_result(result: &str) -> Option<Vec<String>> {
    let lines: Vec<String> = result
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .filter(|line| !line.starts_with("You can call these tools directly"))
        .map(str::to_string)
        .collect();
    if lines.is_empty() {
        return None;
    }

    let first = lines[0].clone();
    let first_lower = first.to_ascii_lowercase();
    if (first_lower.starts_with("found ") && first_lower.contains(" matching tool"))
        || first_lower.starts_with("no matching tools found")
    {
        let mut output = vec![first];
        output.extend(lines.into_iter().skip(1));
        return Some(output);
    }

    Some(lines)
}

fn count_tool_search_result(result: &str) -> Option<usize> {
    let trimmed = result.trim();
    if trimmed.is_empty() {
        return None;
    }

    if let Ok(root) = serde_json::from_str::<Value>(trimmed) {
        if let Some(root) = peel_json_string_wrapper(root) {
            if let Some(count) = count_tool_search_json_result(&root) {
                return Some(count);
            }
        }
    }

    let lines: Vec<&str> = trimmed
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .collect();
    let first = lines.first().copied()?;
    let first_lower = first.to_ascii_lowercase();
    if first_lower.starts_with("no matching tools found") {
        return Some(0);
    }
    if first_lower.starts_with("found ") && first_lower.contains(" matching tool") {
        return first.split_whitespace().nth(1)?.parse::<usize>().ok();
    }

    let bullet_count = lines.iter().filter(|line| line.starts_with("- ")).count();
    (bullet_count > 0).then_some(bullet_count)
}

fn count_tool_search_json_result(root: &Value) -> Option<usize> {
    let tools_value = root.get("tools")?.as_array()?;
    let mut tools = Vec::new();
    collect_tool_search_tools(tools_value, None, &mut tools);
    Some(tools.len())
}

fn json_number_to_i32(v: &Value) -> Option<i32> {
    match v {
        Value::Number(n) => n.as_i64().and_then(|x| i32::try_from(x).ok()),
        _ => None,
    }
}

fn json_number_to_i64(v: &Value) -> Option<i64> {
    match v {
        Value::Number(n) => n.as_i64(),
        _ => None,
    }
}

/// Match C# numeric format `N0` (grouped thousands, no fraction).
/// Match C# numeric format `N0` (grouped thousands, no fraction).
fn format_int_grouped(n: i64) -> String {
    let (prefix, abs_s) = if n < 0 {
        ("-", n.unsigned_abs().to_string())
    } else {
        ("", n.to_string())
    };
    let rev: Vec<char> = abs_s.chars().rev().collect();
    let mut out = String::new();
    for (i, c) in rev.iter().enumerate() {
        if i > 0 && i % 3 == 0 {
            out.push(',');
        }
        out.push(*c);
    }
    let grouped: String = out.chars().rev().collect();
    format!("{prefix}{grouped}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invocation_websearch_query_and_max_results() {
        let args = r#"{"query":"rust async","maxResults":5}"#;
        assert_eq!(
            format_invocation_display("WebSearch", args),
            r#"Searched "rust async""#
        );
    }

    #[test]
    fn invocation_websearch_long_query_truncated() {
        let q: String = "a".repeat(100);
        let args = format!(r#"{{"query":"{q}"}}"#);
        let out = format_invocation_display("WebSearch", &args);
        assert!(out.starts_with("Searched \""));
        assert!(out.ends_with("...\""));
    }

    #[test]
    fn invocation_webfetch_url() {
        let args = r#"{"url":"https://example.com/path"}"#;
        assert_eq!(
            format_invocation_display("WebFetch", args),
            "Fetched https://example.com/path"
        );
    }

    #[test]
    fn invocation_search_tools() {
        let args = r#"{"query":"ReadFile"}"#;
        assert_eq!(
            format_invocation_display("SearchTools", args),
            r#"Searched tools: "ReadFile""#
        );
        assert_eq!(
            format_invocation_display("tool_search", r#"{"query":"board task"}"#),
            r#"Searched tools: "board task""#
        );
        assert_eq!(
            format_invocation_display("tool_search", r#"{"q":"board task"}"#),
            r#"Searched tools: "board task""#
        );
    }

    #[test]
    fn invocation_request_user_input_question_count() {
        let args = r#"{"questions":[{"id":"one"},{"id":"two"},{"id":"three"}]}"#;
        assert_eq!(
            format_invocation_display("RequestUserInput", args),
            "Ask 3 questions"
        );
        assert!(!invocation_needs_calling_called_prefix(
            "RequestUserInput",
            args
        ));
        assert_eq!(
            format_invocation_display("RequestUserInput", "{}"),
            "Ask questions"
        );
    }

    #[test]
    fn invocation_generic_single_string_field() {
        let args = r#"{"path":"src/main.rs"}"#;
        assert_eq!(
            format_invocation_display("ReadFile", args),
            r#"Read main.rs"#
        );
    }

    #[test]
    fn invocation_readfile_with_range() {
        let args = r#"{"path":"src/main.rs","offset":10,"limit":5}"#;
        assert_eq!(
            format_invocation_display("ReadFile", args),
            "Read main.rs L10-14"
        );
    }

    #[test]
    fn invocation_todowrite_started_with_summary() {
        let args = r#"{"merge":true,"todos":[{"id":"t1","content":"next step is ABCDEFGHIJKLMNOPQRSTUVWXYZ","status":"in_progress"}]}"#;
        assert_eq!(
            format_invocation_display("TodoWrite", args),
            "Started to-do next step is ABCDEFGHIJKLMNO..."
        );
    }

    #[test]
    fn result_websearch_results_and_domains() {
        let json = r#"{"query":"q","provider":"exa","results":[{"title":"T","url":"https://exa.ai/docs"},{"title":"U","url":"http://b.com/x"}]}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines.len(), 3);
        assert_eq!(lines[0], "2 results:");
        assert!(lines[1].contains("exa.ai"));
        assert!(lines[2].contains("b.com"));
    }

    #[test]
    fn result_websearch_error() {
        let json = r#"{"error":"rate limited"}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines, vec!["Error: rate limited"]);
    }

    #[test]
    fn result_websearch_empty_results_array() {
        let json = r#"{"query":"x","results":[]}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines, vec!["No results found."]);
    }

    #[test]
    fn result_websearch_double_encoded_string() {
        let inner = r#"{"results":[{"title":"Hi","url":"https://z.com"}]}"#;
        let outer = serde_json::to_string(&serde_json::Value::String(inner.to_string()))
            .expect("stringify");
        let lines = format_result_summary("WebSearch", &outer).expect("lines");
        assert_eq!(lines.len(), 2);
        assert_eq!(lines[0], "1 result:");
    }

    #[test]
    fn result_webfetch_summary() {
        let json = r#"{"status":200,"length":50000,"extractor":"readability","truncated":true}"#;
        let lines = format_result_summary("WebFetch", json).expect("lines");
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("200"));
        assert!(lines[0].contains("50,000"));
        assert!(lines[0].contains("readability"));
        assert!(lines[0].contains("truncated"));
    }

    #[test]
    fn result_search_tools_tool_list() {
        let text = "Found 2 matching tool(s):\n- ReadFile: Read a file\n- WriteFile: Write a file";
        let lines = format_result_summary("SearchTools", text).expect("lines");
        assert_eq!(
            lines,
            vec![
                "Found 2 matching tool(s):".to_string(),
                "- ReadFile: Read a file".to_string(),
                "- WriteFile: Write a file".to_string()
            ]
        );
    }

    #[test]
    fn result_native_tool_search_namespace_result() {
        let json = r#"{"tools":[{"type":"namespace","name":"oratorio","tools":[{"type":"function","name":"CreateBoardTask","description":"Create an Oratorio board task."}]}]}"#;
        let lines = format_result_summary("tool_search", json).expect("lines");
        assert_eq!(
            lines,
            vec![
                "Found 1 matching tool(s):".to_string(),
                "- oratorio.CreateBoardTask: Create an Oratorio board task.".to_string()
            ]
        );
        assert_eq!(
            format_completed_invocation_display_with_result("tool_search", "{}", json, None),
            Some("Searched 1 tool".to_string())
        );
        assert_eq!(
            format_completed_invocation_display_with_result(
                "tool_search",
                "{}",
                "Found 2 matching tool(s):\n- ReadFile\n- WriteFile",
                None
            ),
            Some("Searched 2 tools".to_string())
        );
    }

    #[test]
    fn result_request_user_input_maps_questions_to_answers() {
        let args = r#"{"questions":[{"id":"mode","question":"Which mode?"},{"id":"note","question":"Anything else?"}]}"#;
        let result = r#"{"answers":{"mode":{"answers":["Auto"]},"note":{"answers":["user_note: Prefer lighter UI"]}}}"#;
        let lines =
            format_result_summary_with_args("RequestUserInput", Some(args), result).expect("lines");
        assert_eq!(
            lines,
            vec![
                "Which mode?: Auto".to_string(),
                "Anything else?: Prefer lighter UI".to_string()
            ]
        );
    }

    #[test]
    fn result_request_user_input_masks_secret_freeform_answer() {
        let args = r#"{"questions":[{"id":"token","question":"Token?","isSecret":true}]}"#;
        let result = r#"{"answers":{"token":{"answers":["user_note: sk-secret"]}}}"#;
        let lines =
            format_result_summary_with_args("RequestUserInput", Some(args), result).expect("lines");
        assert_eq!(lines, vec!["Token?: Hidden".to_string()]);
    }

    #[test]
    fn result_unknown_tool_returns_none() {
        assert!(format_result_summary("ReadFile", "{}").is_none());
    }

    #[test]
    fn prefix_flag_false_for_standalone_tools_with_valid_args() {
        assert!(!invocation_needs_calling_called_prefix(
            "WebSearch",
            r#"{"query":"x","maxResults":5}"#
        ));
        assert!(!invocation_needs_calling_called_prefix(
            "WebFetch",
            r#"{"url":"https://a.com"}"#
        ));
        assert!(!invocation_needs_calling_called_prefix(
            "SearchTools",
            r#"{"query":"ReadFile"}"#
        ));
        assert!(!invocation_needs_calling_called_prefix(
            "tool_search",
            r#"{"query":"ReadFile"}"#
        ));
    }

    #[test]
    fn prefix_flag_true_when_standalone_parse_fails_or_generic_tool() {
        assert!(invocation_needs_calling_called_prefix("WebSearch", "{}"));
        assert!(invocation_needs_calling_called_prefix(
            "McpTool",
            r#"{"x":1}"#
        ));
    }

    #[test]
    fn prefix_flag_false_for_readfile_and_todos() {
        assert!(!invocation_needs_calling_called_prefix(
            "ReadFile",
            r#"{"path":"src/main.rs"}"#
        ));
        assert!(!invocation_needs_calling_called_prefix(
            "TodoWrite",
            r#"{"merge":false,"todos":[{"id":"t1","content":"first","status":"pending"}]}"#
        ));
    }

    #[test]
    fn partial_json_extractor_reads_path() {
        let partial = r#"{"path":"src\main.rs","content":"hel"#;
        let value = extract_partial_json_string_value(partial, "path").expect("path");
        assert_eq!(value, r#"src\main.rs"#);
    }

    #[test]
    fn active_invocation_writefile_uses_partial_path() {
        // Content body is still incomplete but path was already streamed —
        // the running label should already show the filename.
        let partial = r#"{"path":"src/demo.rs","content":"let x"#;
        assert_eq!(
            format_active_invocation_display("WriteFile", partial, None),
            "Writing to demo.rs..."
        );
    }

    #[test]
    fn active_invocation_exec_shows_command_first_line() {
        let partial = r#"{"command":"npm install"#;
        assert_eq!(
            format_active_invocation_display("Exec", partial, None),
            "Running: npm install"
        );
    }

    #[test]
    fn active_invocation_websearch_shows_query() {
        let partial = r#"{"query":"rust async streams"#;
        assert_eq!(
            format_active_invocation_display("WebSearch", partial, None),
            "Searching the web for \"rust async streams\"..."
        );
    }

    #[test]
    fn active_invocation_native_tool_search_shows_query() {
        let partial = r#"{"query":"board task"#;
        assert_eq!(
            format_active_invocation_display("tool_search", partial, None),
            "Searching tools: \"board task\"..."
        );
    }

    #[test]
    fn active_invocation_spawn_agent_prefers_nickname() {
        let partial = r#"{"agentPrompt":"Write tests","agentNickname":"tester","profile":"native""#;
        assert_eq!(
            format_active_invocation_display("SpawnAgent", partial, None),
            "Spawning agent: tester..."
        );
    }

    #[test]
    fn active_invocation_request_user_input_question_count() {
        let args = r#"{"questions":[{"id":"one"},{"id":"two"}]}"#;
        assert_eq!(
            format_active_invocation_display("RequestUserInput", args, None),
            "Asking 2 questions..."
        );
        assert_eq!(
            format_active_invocation_display("RequestUserInput", r#"{"questions":["#, None),
            "Asking questions..."
        );
    }

    #[test]
    fn active_invocation_spawn_agent_falls_back_to_prompt() {
        let partial = r#"{"agentPrompt":"Write the compatibility tests","profile":"codex""#;
        assert_eq!(
            format_active_invocation_display("SpawnAgent", partial, None),
            "Spawning agent for: Write the compatibility tests..."
        );
    }

    #[test]
    fn active_invocation_grep_shows_pattern_and_path() {
        let partial = r#"{"pattern":"TODO","path":"src"#;
        assert_eq!(
            format_active_invocation_display("GrepFiles", partial, None),
            "Searching \"TODO\" in src..."
        );
    }

    #[test]
    fn active_invocation_mcp_tool_uses_generic_placeholder() {
        // Unknown / external tool names never leak raw JSON args — they render
        // as "Generating parameters for X..." until the tool call completes.
        let partial = r#"{"url":"https://foo.example/secret"#;
        assert_eq!(
            format_active_invocation_display("acme_mcp_tool", partial, None),
            "Generating parameters for acme_mcp_tool..."
        );
    }

    #[test]
    fn active_invocation_create_plan_shows_title() {
        let partial = r#"{"title":"Ship feature X","overview":"Not yet"#;
        assert_eq!(
            format_active_invocation_display("CreatePlan", partial, None),
            "Drafting plan: Ship feature X..."
        );
    }

    #[test]
    fn active_invocation_create_plan_without_title() {
        let partial = r#"{"overview":"#;
        assert_eq!(
            format_active_invocation_display("CreatePlan", partial, None),
            "Drafting plan..."
        );
    }

    #[test]
    fn generic_invocation_hides_json_for_non_builtin_tool() {
        let args = r#"{"url":"https://foo.example/secret"}"#;
        // External tool: no argument JSON leaks into the rendered label.
        assert_eq!(
            format_invocation_display("acme_mcp_tool", args),
            "acme_mcp_tool"
        );
    }

    #[test]
    fn is_builtin_tool_matches_known_names() {
        assert!(is_builtin_tool("ReadFile"));
        assert!(is_builtin_tool("CreatePlan"));
        assert!(is_builtin_tool("RequestUserInput"));
        assert!(!is_builtin_tool("acme_mcp_tool"));
    }
}
