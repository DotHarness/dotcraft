using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class StructuredContextTests
{
    [Fact]
    public void Materialization_PreservesNativeSourcesAndEmitsOwnedScreenshotOnce()
    {
        SessionInputContext[] contexts = [
            new() { Id = "page", Kind = "pageReference", Url = "https://example.test", Title = "Page", SelectionKind = "region", Text = "Chart", Comment = "Explain", Image = new() { TempPath = "/attachments/chart.png", FileName = "chart.png", MimeType = "image/png" } },
            new() { Id = "reply", Kind = "responseAnnotation", ThreadId = "thread", TurnId = "turn", ItemId = "item", SelectedText = "Quote", Comment = "Clarify" },
            new() { Id = "diff", Kind = "diffAnnotation", Path = "a.cs", Side = "left", StartLine = 3, EndLine = 5, SelectedText = "old code", Comment = "Keep" },
            new() { Id = "paste", Kind = "pastedText", Path = "/attachments/paste.txt", FileName = "paste.txt", Preview = "Preview", CharacterCount = 10000 }
        ];
        var input = contexts.Select(context => new SessionInputPart { Type = "contextRef", Context = context }).ToArray();
        var service = new InputMaterializationService(new CommandRegistry(), null);
        var result = service.Materialize(input);
        var restored = JsonSerializer.Deserialize<SessionInputPart[]>(JsonSerializer.Serialize(result.NativeInputParts, SessionWireJsonOptions.Default), SessionWireJsonOptions.Default)!;
        Assert.Equal(contexts, restored.Select(part => part.Context));
        Assert.Equal("/attachments/chart.png", Assert.Single(result.MaterializedInputParts, part => part.Type == "localImage").Path);
        Assert.DoesNotContain(result.MaterializedInputParts, part => part.Type == "contextRef");
        Assert.Equal(string.Empty, result.DisplayText);
        Assert.Contains(result.MaterializedInputParts, part => part.Text?.Contains("Pasted text file:") == true && part.Text.Contains("/attachments/paste.txt"));
        var contract = TurnContractMapper.ToContract(new QueuedTurnInput { Id = "queue", ThreadId = "thread", ClientUserMessageId = "submission", NativeInputParts = input, MaterializedInputParts = result.MaterializedInputParts });
        Assert.Equal("submission", contract.ClientUserMessageId);
        Assert.Equal(contexts, TurnContractMapper.ToDomain(contract.NativeInputParts).Select(part => part.Context));
    }

    [Fact]
    public void ThreadReferences_MaterializeToTheirTextOutsideTheDisplayText()
    {
        const string block = "## Referenced chats with DotCraft:\n[{\"threadId\":\"thread_a\"}]";
        const string prompt = "[@Fix login](thread://thread_a) continue";
        SessionInputPart[] input = [
            new() { Type = "contextRef", Context = new() { Id = "refs", Kind = "threadReferences", Text = block } },
            new() { Type = "text", Text = prompt }
        ];

        var result = new InputMaterializationService(new CommandRegistry(), null).Materialize(input);

        Assert.Equal([block, prompt], result.MaterializedInputParts.Select(part => part.Text));
        Assert.Equal(prompt, result.DisplayText);
    }

    [Fact]
    public async Task TurnStart_InvalidContext_ReturnsInvalidParamsWithoutSubmission()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var request = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.TurnStart, new
        {
            threadId = thread.Id,
            input = new[]
            {
                new { type = "contextRef", context = new { id = "bad", kind = "diffAnnotation", startLine = 9, endLine = 2 } }
            }
        });

        await harness.ExecuteRequestAsync(request);

        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(thread.Turns);
        Assert.Empty(thread.QueuedInputs);
    }
}
