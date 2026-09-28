namespace DotCraft.SessionImport;

internal static class ImportDiagnostics
{
    public static string Fallback(string code) => code switch
    {
        "import_source_changed" => "The source changed after detection. Check available imports again.",
        "import_source_invalid" => "The source could not be read as a supported import item.",
        "import_hooks_need_trust" => "Review and trust the imported hooks before they can run.",
        "import_environment_missing" => "One or more required environment variables are missing on this host.",
        "import_command_semantics_unsupported" => "This command uses execution or permission features that DotCraft cannot preserve.",
        "import_plugin_review" => "Review the plugin's hooks and unsupported contributions.",
        "import_plugin_content_missing" => "The selected plugin's installed content is missing.",
        "import_plugin_no_supported_content" => "The plugin does not contain a supported contribution.",
        "import_mcp_policy_unsupported" => "This server has policies that cannot be represented in DotCraft.",
        "import_mcp_transport_unsupported" => "This MCP transport is not supported.",
        "import_mcp_variable_unsupported" => "This MCP declaration contains an unsupported variable expression.",
        "import_write_failed" => "The item could not be installed. Existing configuration was preserved.",
        "import_history_write_failed" => "The import results could not be saved to history.",
        "import_pass_failed" => "The import pass could not finish.",
        _ => "This source item uses an unsupported format or contribution."
    };
}
