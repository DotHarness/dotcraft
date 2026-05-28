using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Core.Tests.Protocol.AppServer;

public sealed class WireAcpExtensionProxyMergeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void MergeThreadIdIntoParams_NullParams_ReturnsThreadIdOnly()
    {
        var o = WireAcpExtensionProxy.MergeThreadIdIntoParams("t1", null);
        var json = JsonSerializer.Serialize(o, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("t1", doc.RootElement.GetProperty("threadId").GetString());
        Assert.Single(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public void MergeThreadIdIntoParams_Object_AddsOrOverwritesThreadId()
    {
        var o = WireAcpExtensionProxy.MergeThreadIdIntoParams("thread-a", new { path = "/x", offset = 1 });
        var json = JsonSerializer.Serialize(o, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("thread-a", doc.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("/x", doc.RootElement.GetProperty("path").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public void MergeThreadIdIntoParams_NonObject_WrapsPayload()
    {
        var o = WireAcpExtensionProxy.MergeThreadIdIntoParams("tid", "plain");
        var json = JsonSerializer.Serialize(o, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("tid", doc.RootElement.GetProperty("threadId").GetString());
        Assert.Equal("plain", doc.RootElement.GetProperty("payload").GetString());
    }
}
