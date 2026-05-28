// TOML theme configuration deserialization (§11 of specs/clients/tui-client.md).

use serde::Deserialize;

#[derive(Debug, Deserialize, Default, Clone)]
pub struct ThemeConfig {
    pub colors: Option<ColorConfig>,
    /// Replaces old [status_bar] and [side_panel] sections.
    pub footer: Option<FooterConfig>,
    pub code: Option<CodeConfig>,
    // Keep old fields for backwards-compatible deserialization (silently ignored).
    pub status_bar: Option<serde_json::Value>,
    pub side_panel: Option<serde_json::Value>,
}

#[derive(Debug, Deserialize, Default, Clone)]
pub struct ColorConfig {
    pub brand: Option<String>,
    pub user_message: Option<String>,
    pub agent_message: Option<String>,
    pub reasoning: Option<String>,
    pub tool_active: Option<String>,
    pub tool_completed: Option<String>,
    pub error: Option<String>,
    pub success: Option<String>,
    pub dim: Option<String>,
    pub mode_agent: Option<String>,
    pub mode_plan: Option<String>,
    pub approval_border: Option<String>,
    pub status_indicator: Option<String>,
    pub heading1: Option<String>,
    pub heading2: Option<String>,
    pub heading3: Option<String>,
    pub inline_code_fg: Option<String>,
    pub inline_code_bg: Option<String>,
    pub code_block_border: Option<String>,
    pub table_border: Option<String>,
    pub blockquote: Option<String>,
    pub link_color: Option<String>,
}

#[derive(Debug, Deserialize, Default, Clone)]
pub struct FooterConfig {
    pub foreground: Option<String>,
    pub context_color: Option<String>,
}

#[derive(Debug, Deserialize, Default, Clone)]
pub struct CodeConfig {
    /// syntect theme name (e.g. "base16-ocean.dark", "Solarized (dark)")
    pub syntect_theme: Option<String>,
}

impl ThemeConfig {
    pub fn load_from_file(path: &std::path::Path) -> anyhow::Result<Self> {
        let content = std::fs::read_to_string(path)?;
        let config = toml::from_str(&content)?;
        Ok(config)
    }

    /// Merge another config on top of self (the other config's values take priority).
    pub fn merge_with(&mut self, other: ThemeConfig) {
        if let Some(other_colors) = other.colors {
            let mine = self.colors.get_or_insert_with(ColorConfig::default);
            macro_rules! override_field {
                ($field:ident) => {
                    if other_colors.$field.is_some() {
                        mine.$field = other_colors.$field;
                    }
                };
            }
            override_field!(brand);
            override_field!(user_message);
            override_field!(agent_message);
            override_field!(reasoning);
            override_field!(tool_active);
            override_field!(tool_completed);
            override_field!(error);
            override_field!(success);
            override_field!(dim);
            override_field!(mode_agent);
            override_field!(mode_plan);
            override_field!(approval_border);
            override_field!(status_indicator);
            override_field!(heading1);
            override_field!(heading2);
            override_field!(heading3);
            override_field!(inline_code_fg);
            override_field!(inline_code_bg);
            override_field!(code_block_border);
            override_field!(table_border);
            override_field!(blockquote);
            override_field!(link_color);
        }
        if let Some(footer) = other.footer {
            self.footer = Some(footer);
        }
        if let Some(code) = other.code {
            self.code = Some(code);
        }
    }
}
