use crate::{app::state::SlashCommandDescriptor, wire::types::CommandInfo};
use std::collections::HashSet;

/// Parsed slash command text (case-normalized name + argument forms).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParsedSlashCommand {
    pub name: String,
    pub argument_text: String,
    pub arguments: Vec<String>,
}

/// Slash commands that must remain client-local in TUI.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum LocalSlashCommand {
    Sessions,
    Load { thread_id: String },
    Plan,
    Agent,
    Clear,
    Goal { argument_text: String },
    Model { model_name: Option<String> },
    Thinking { value: Option<String> },
    Provider { provider_id: Option<String> },
    Skills,
    Permissions,
    Quit,
}

/// Static local command metadata used by completion.
pub fn local_command_catalog() -> Vec<SlashCommandDescriptor> {
    vec![
        SlashCommandDescriptor::new(
            "/sessions",
            "Browse and resume previous threads",
            "local-ui",
        ),
        SlashCommandDescriptor::new("/load", "Resume a thread by ID (/load <id>)", "local-ui"),
        SlashCommandDescriptor::new("/plan", "Switch to Plan mode", "local-ui"),
        SlashCommandDescriptor::new("/agent", "Switch to Agent mode", "local-ui"),
        SlashCommandDescriptor::new(
            "/goal",
            "Show, set, pause, resume, or clear the thread goal",
            "local-ui",
        ),
        SlashCommandDescriptor::new("/clear", "Clear the chat display", "local-ui"),
        SlashCommandDescriptor::new(
            "/model",
            "Open model picker or set model directly (/model [name|default])",
            "local-ui",
        ),
        SlashCommandDescriptor::new(
            "/thinking",
            "Set thinking mode (/thinking [default|off|low|medium|high|extra-high])",
            "local-ui",
        ),
        SlashCommandDescriptor::new(
            "/provider",
            "List or select a model provider (/provider [id])",
            "local-ui",
        ),
        SlashCommandDescriptor::new("/skills", "Enable, disable, or inspect skills", "local-ui"),
        SlashCommandDescriptor::new(
            "/permissions",
            "Choose what DotCraft is allowed to do",
            "local-ui",
        ),
        SlashCommandDescriptor::new("/quit", "Exit dotcraft-tui", "local-ui"),
    ]
}

/// Merge local and server-provided commands with local names taking precedence.
pub fn merge_command_catalog(server_commands: &[CommandInfo]) -> Vec<SlashCommandDescriptor> {
    let mut merged = local_command_catalog();
    let mut known = HashSet::new();
    for cmd in &merged {
        known.insert(cmd.name.to_ascii_lowercase());
    }

    let mut server_sorted = server_commands.to_vec();
    server_sorted.sort_by_key(|c| c.name.to_ascii_lowercase());
    for cmd in server_sorted {
        let key = cmd.name.to_ascii_lowercase();
        if known.contains(&key) {
            continue;
        }
        let description = if cmd.fallback_description.trim().is_empty() {
            cmd.description.as_str()
        } else {
            cmd.fallback_description.as_str()
        };
        merged.push(SlashCommandDescriptor::new(
            cmd.name,
            if description.trim().is_empty() {
                "(no description)"
            } else {
                description
            },
            cmd.category,
        ));
        known.insert(key);
    }
    merged
}

/// Try to parse a slash command from user input.
/// Returns None if the input is not a slash command.
pub fn parse(input: &str) -> Option<ParsedSlashCommand> {
    let input = input.trim();
    if !input.starts_with('/') {
        return None;
    }

    let mut parts = input.splitn(2, ' ');
    let name = parts.next().unwrap_or("").to_lowercase();
    let argument_text = parts.next().map(str::trim).unwrap_or("").to_string();
    let arguments = argument_text
        .split_whitespace()
        .map(str::to_string)
        .collect::<Vec<_>>();

    Some(ParsedSlashCommand {
        name,
        argument_text,
        arguments,
    })
}

/// Map a parsed command to a local TUI command if applicable.
pub fn to_local_command(parsed: &ParsedSlashCommand) -> Option<LocalSlashCommand> {
    Some(match parsed.name.as_str() {
        "/sessions" => LocalSlashCommand::Sessions,
        "/load" => LocalSlashCommand::Load {
            thread_id: parsed.argument_text.clone(),
        },
        "/plan" => LocalSlashCommand::Plan,
        "/agent" => LocalSlashCommand::Agent,
        "/clear" => LocalSlashCommand::Clear,
        "/goal" => LocalSlashCommand::Goal {
            argument_text: parsed.argument_text.clone(),
        },
        "/model" => LocalSlashCommand::Model {
            model_name: if parsed.argument_text.is_empty() {
                None
            } else {
                Some(parsed.argument_text.clone())
            },
        },
        "/thinking" => LocalSlashCommand::Thinking {
            value: if parsed.argument_text.is_empty() {
                None
            } else {
                Some(parsed.argument_text.clone())
            },
        },
        "/provider" => LocalSlashCommand::Provider {
            provider_id: if parsed.argument_text.is_empty() {
                None
            } else {
                Some(parsed.argument_text.clone())
            },
        },
        "/skills" => LocalSlashCommand::Skills,
        "/permissions" | "/premissions" => LocalSlashCommand::Permissions,
        "/quit" | "/exit" => LocalSlashCommand::Quit,
        _ => return None,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_slash_command_extracts_name_and_arguments() {
        let parsed = parse("/Code-Review src/lib.rs --strict").expect("should parse");
        assert_eq!(parsed.name, "/code-review");
        assert_eq!(parsed.argument_text, "src/lib.rs --strict");
        assert_eq!(parsed.arguments, vec!["src/lib.rs", "--strict"]);
    }

    #[test]
    fn merge_catalog_prefers_local_command_metadata() {
        let merged = merge_command_catalog(&[CommandInfo {
            name: "/skills".to_string(),
            aliases: vec![],
            description: "Server skills".to_string(),
            description_key: "commands.skills.description".to_string(),
            fallback_description: "Server skills".to_string(),
            category: "builtin".to_string(),
            requires_admin: false,
        }]);
        let skills = merged
            .iter()
            .find(|c| c.name == "/skills")
            .expect("skills should exist");
        assert_eq!(skills.category, "local-ui");
        assert_eq!(skills.description, "Enable, disable, or inspect skills");
    }

    #[test]
    fn permissions_command_accepts_common_typo_alias() {
        let parsed = parse("/premissions").expect("should parse");

        assert_eq!(
            to_local_command(&parsed),
            Some(LocalSlashCommand::Permissions)
        );
    }

    #[test]
    fn provider_command_lists_or_selects_provider() {
        let list = parse("/provider").expect("should parse");
        assert_eq!(
            to_local_command(&list),
            Some(LocalSlashCommand::Provider { provider_id: None })
        );

        let select = parse("/provider anthropic-main").expect("should parse");
        assert_eq!(
            to_local_command(&select),
            Some(LocalSlashCommand::Provider {
                provider_id: Some("anthropic-main".to_string())
            })
        );
    }

    #[test]
    fn thinking_command_accepts_mode_argument() {
        let parsed = parse("/thinking extra-high").expect("should parse");

        assert_eq!(
            to_local_command(&parsed),
            Some(LocalSlashCommand::Thinking {
                value: Some("extra-high".to_string())
            })
        );
    }
}
