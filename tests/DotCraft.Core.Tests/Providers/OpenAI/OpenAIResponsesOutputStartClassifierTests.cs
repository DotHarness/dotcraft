using System.ClientModel.Primitives;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using Xunit;

#pragma warning disable OPENAI001

namespace DotCraft.Tests.Providers.OpenAI;

public sealed class OpenAIResponsesOutputStartClassifierTests
{
    [Fact]
    public async Task FirstOutputIsTheFirstOutputItemNotTheRequestAcknowledgements()
    {
        StreamingResponseUpdate[] events =
        [
            Event("""{"type":"response.created","sequence_number":0,"response":{"id":"resp_1","object":"response","created_at":1,"status":"in_progress","model":"m","output":[]}}"""),
            Event("""{"type":"response.in_progress","sequence_number":1,"response":{"id":"resp_1","object":"response","created_at":1,"status":"in_progress","model":"m","output":[]}}"""),
            Event("""{"type":"response.output_item.added","sequence_number":2,"output_index":0,"item":{"type":"reasoning","id":"rs_1","summary":[]}}""")
        ];

        var classified = new List<bool>();
        await foreach (var update in Stream(events).AsChatResponseUpdatesAsync())
            classified.Add(OpenAIResponsesOutputStartClassifier.Instance.IsOutputStart(update));

        Assert.Equal([false, false, true], classified);
    }

    private static StreamingResponseUpdate Event(string json) =>
        ModelReaderWriter.Read<StreamingResponseUpdate>(BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;

    private static async IAsyncEnumerable<StreamingResponseUpdate> Stream(IEnumerable<StreamingResponseUpdate> events)
    {
        foreach (var update in events)
        {
            await Task.Yield();
            yield return update;
        }
    }
}
