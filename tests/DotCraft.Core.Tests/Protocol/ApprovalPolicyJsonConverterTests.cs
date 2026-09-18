using System.Text.Json;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ApprovalPolicyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ApprovalPolicyJsonConverter() }
    };

    [Theory]
    [InlineData("\"deny\"")]
    [InlineData("\"interrupt\"")]
    [InlineData("\"Interrupt\"")]
    public void Read_DenyOrItsFormerName_ResolvesToDeny(string json) =>
        Assert.Equal(ApprovalPolicy.Deny, JsonSerializer.Deserialize<ApprovalPolicy>(json, Options));

    [Fact]
    public void Write_Deny_EmitsDeny() =>
        Assert.Equal("\"deny\"", JsonSerializer.Serialize(ApprovalPolicy.Deny, Options));
}
