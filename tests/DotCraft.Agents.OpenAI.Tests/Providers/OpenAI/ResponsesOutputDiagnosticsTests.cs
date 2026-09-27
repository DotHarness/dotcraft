using System.ClientModel.Primitives;
using DotCraft.Agents;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Agents;

public sealed class ResponsesOutputDiagnosticsTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("run the tests", 13)]
    public void CompletedResponse_ReportsTextLengthWithoutContent(string text, int length)
    {
        var output = System.Text.Json.JsonSerializer.Serialize(text);
        var response = ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString($$$"""
            {"id":"resp_test","object":"response","created_at":0,"status":"completed","model":"test-model",
             "output":[{"type":"message","id":"msg_test","role":"assistant","status":"completed",
                        "content":[{"type":"output_text","text":{{{output}}},"annotations":[]}]}]}
            """))!;
        var summary = new ResponsesOutputDiagnostics().Summarize(response);
        Assert.NotNull(summary);
        Assert.Equal(["message"], summary.Types);
        Assert.Equal(length, summary.TextLength);
    }

    [Fact]
    public void CompletedResponse_WithNoOutput_ReportsZero()
    {
        var response = ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString("""
            {"id":"resp_test","object":"response","created_at":0,"status":"completed","model":"test-model","output":[]}
            """))!;
        var summary = new ResponsesOutputDiagnostics().Summarize(response);
        Assert.NotNull(summary);
        Assert.Empty(summary.Types);
        Assert.Equal(0, summary.TextLength);
    }

    [Fact]
    public async Task StreamingItems_AreCountedWhenTerminalOmitsOutput()
    {
        var payload = """
            data: {"type":"response.output_item.done","sequence_number":1,"output_index":0,"item":{"type":"message","id":"msg_1","role":"assistant","status":"completed","content":[{"type":"output_text","text":"go ahead","annotations":[]}]}}

            data: {"type":"response.completed","sequence_number":2,"response":{"id":"resp_1","object":"response","created_at":0,"status":"completed","model":"test-model","output":[]}}

            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
        var diagnostics = new ResponsesOutputDiagnostics();
        ResponsesOutputDiagnostics.Summary? summary = null;
        await foreach (var update in OpenAIResponsesLiteTransport.ParseSseUpdatesAsync(stream))
        {
            diagnostics.Observe(update);
            if (update is StreamingResponseCompletedUpdate completed)
                summary = diagnostics.Summarize(completed.Response);
        }
        Assert.NotNull(summary);
        Assert.Equal(["message"], summary.Types);
        Assert.Equal(8, summary.TextLength);
    }
}
