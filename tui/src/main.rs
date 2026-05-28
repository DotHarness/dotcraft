use anyhow::Result;
use clap::Parser;
use dotcraft_tui::run;

#[derive(Parser, Debug)]
#[command(name = "dotcraft-tui", about = "DotCraft Terminal UI", version)]
struct Cli {
    /// Connect to a remote AppServer over WebSocket instead of spawning a subprocess.
    /// Example: ws://localhost:3000/ws or ws://localhost:3000/ws?token=<token>
    #[arg(long, value_name = "URL")]
    remote: Option<String>,

    /// Path to the dotcraft binary used to auto-start Hub in local mode.
    /// Defaults to "dotcraft" on PATH.
    #[arg(long, value_name = "PATH", env = "DOTCRAFT_BIN")]
    server_bin: Option<String>,

    /// Path to the workspace directory.
    #[arg(long, value_name = "PATH")]
    workspace: Option<String>,

    /// Path to a custom theme TOML file.
    #[arg(long, value_name = "PATH")]
    theme: Option<String>,

    /// Language preference: "en" for English, "zh" for Chinese.
    /// If omitted, auto-detected from .craft/config.json, then defaults to "en".
    #[arg(long, value_name = "LANG")]
    lang: Option<String>,
}

#[tokio::main]
async fn main() -> Result<()> {
    let cli = Cli::parse();
    run(
        cli.remote,
        cli.server_bin,
        cli.workspace,
        cli.theme,
        cli.lang,
    )
    .await
}
