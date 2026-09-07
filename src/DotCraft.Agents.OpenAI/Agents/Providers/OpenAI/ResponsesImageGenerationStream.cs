using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001, MEAI001

namespace DotCraft.Agents;

/// <summary>Preserves authoritative image results across the SDK's streaming projection.</summary>
internal sealed class ResponsesImageGenerationStream
{
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly Queue<HostedImageGenerationContent> _pending = new();

    public async IAsyncEnumerable<StreamingResponseUpdate> CaptureAsync(
        IAsyncEnumerable<StreamingResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in updates.WithCancellation(ct).ConfigureAwait(false))
        {
            if (update is StreamingResponseOutputItemDoneUpdate done)
                Capture(done.Item);
            var response = update switch
            {
                StreamingResponseCompletedUpdate completed => completed.Response,
                StreamingResponseFailedUpdate failed => failed.Response,
                StreamingResponseIncompleteUpdate incomplete => incomplete.Response,
                _ => null
            };
            if (response is not null)
                foreach (var item in response.OutputItems)
                    Capture(item);
            yield return update;
        }
    }

    public void Apply(ChatResponseUpdate update)
    {
        for (var i = update.Contents.Count - 1; i >= 0; i--)
            if (update.Contents[i] is ImageGenerationToolResultContent)
                update.Contents.RemoveAt(i);
        foreach (var content in Drain())
            update.Contents.Add(content);
    }

    public IEnumerable<HostedImageGenerationContent> Drain()
    {
        while (_pending.TryDequeue(out var content))
            yield return content;
    }

    private void Capture(ResponseItem item)
    {
        if (ResponsesToolSearchMapper.TryCreateHostedImageGenerationContent(item, out var content))
            Enqueue(content);
    }

    private void Enqueue(HostedImageGenerationContent content)
    {
        if (_completed.Add(content.Id))
            _pending.Enqueue(content);
    }
}
