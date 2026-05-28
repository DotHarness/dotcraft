using DotCraft.Configuration;
using DotCraft.Mcp;

namespace DotCraft.Tests.Configuration;

public class ConfigSchemaBuilderReloadTests
{
    [Fact]
    public void UnannotatedField_DefaultsToProcessRestart()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(TestSectionConfig)]);
        var section = Assert.Single(schema);
        var field = Assert.Single(section.Fields);

        Assert.Equal("Value", field.Key);
        Assert.Equal(ReloadBehavior.ProcessRestart, field.Reload);
        Assert.Null(field.SubsystemKey);
    }

    [Fact]
    public void SectionDefault_AppliesToField_WhenFieldHasNoOverride()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(SectionDefaultHotConfig)]);
        var section = Assert.Single(schema);
        var field = Assert.Single(section.Fields);

        Assert.Equal(ReloadBehavior.Hot, field.Reload);
        Assert.Null(field.SubsystemKey);
    }

    [Fact]
    public void FieldOverride_WinsOverSectionDefault()
    {
        var schema = ConfigSchemaBuilder.BuildAll([typeof(SectionDefaultHotWithOverrideConfig)]);
        var section = Assert.Single(schema);
        var field = Assert.Single(section.Fields);

        Assert.Equal(ReloadBehavior.ProcessRestart, field.Reload);
        Assert.Null(field.SubsystemKey);
    }

    [Fact]
    public void AppConfig_AndMcp_HaveExpectedReloadAnnotations()
    {
        var schema = ConfigSchemaBuilder.BuildAll([
            typeof(AppConfig),
            typeof(AppConfig.SkillsConfig),
            typeof(McpServerConfig)
        ]);

        var core = Assert.Single(schema, s => s.Section == "Core");
        var fields = core.Fields.ToDictionary(f => f.Key, f => f);
        Assert.Equal(ReloadBehavior.ProcessRestart, fields["ProviderId"].Reload);
        Assert.Equal(ReloadBehavior.ProcessRestart, fields["Model"].Reload);
        Assert.False(fields.ContainsKey("ApiKey"));
        Assert.False(fields.ContainsKey("EndPoint"));

        var skills = Assert.Single(schema, s => s.Path is ["Skills"]);
        var disabledSkills = Assert.Single(skills.Fields, f => f.Key == "DisabledSkills");
        Assert.Equal(ReloadBehavior.Hot, disabledSkills.Reload);

        var mcp = Assert.Single(schema, s => s.RootKey == "McpServers");
        Assert.NotNull(mcp.ItemFields);
        Assert.All(mcp.ItemFields!, field =>
        {
            Assert.Equal(ReloadBehavior.Hot, field.Reload);
            Assert.Null(field.SubsystemKey);
        });
    }

    [Fact]
    public void SubsystemRestart_WithoutSubsystemKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigSchemaBuilder.BuildAll([typeof(InvalidSubsystemConfig)]));

        Assert.Contains("SubsystemKey is required", ex.Message);
    }

    [ConfigSection("TestSection")]
    private sealed class TestSectionConfig
    {
        public string Value { get; set; } = string.Empty;
    }

    [ConfigSection("SectionDefaultHot", DefaultReload = ReloadBehavior.Hot, HasDefaultReload = true)]
    private sealed class SectionDefaultHotConfig
    {
        public bool Enabled { get; set; }
    }

    [ConfigSection("SectionDefaultHotWithOverride", DefaultReload = ReloadBehavior.Hot, HasDefaultReload = true)]
    private sealed class SectionDefaultHotWithOverrideConfig
    {
        [ConfigField(Reload = ReloadBehavior.ProcessRestart, HasReload = true)]
        public bool Enabled { get; set; }
    }

    [ConfigSection("InvalidSubsystem")]
    private sealed class InvalidSubsystemConfig
    {
        [ConfigField(Reload = ReloadBehavior.SubsystemRestart, HasReload = true)]
        public bool Enabled { get; set; }
    }
}
