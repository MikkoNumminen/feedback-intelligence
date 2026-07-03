using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailFeedback.Llm.Structuring;

namespace RetailFeedback.Llm.Tests;

public class LlmStructuringServiceTests : IDisposable
{
    private const string ValidJson =
        """{"department": "maito_kylma", "theme": "tuotteiden tuoreus", "severity": "high", "type": "complaint", "language": "fi"}""";

    private const string InvalidJson =
        """{"department": "kylmäosasto", "theme": "tuoreus", "severity": "high", "type": "complaint", "language": "fi"}""";

    private readonly string _promptPath = Path.Combine(Path.GetTempPath(), $"structuring-test-{Guid.NewGuid():N}.txt");

    public LlmStructuringServiceTests() =>
        File.WriteAllText(_promptPath, "Classify this feedback as JSON:\n{{text}}");

    public void Dispose() => File.Delete(_promptPath);

    private LlmStructuringService CreateService(ScriptedChatClient client) => new(
        client,
        Options.Create(new StructuringOptions { PromptPath = _promptPath }),
        NullLogger<LlmStructuringService>.Instance);

    [Fact]
    public async Task ValidFirstResponse_Succeeds_WithoutRetry()
    {
        var client = new ScriptedChatClient(ValidJson);

        var result = await CreateService(client).StructureAsync("maito oli vanhaa");

        Assert.False(result.Failed);
        Assert.False(result.Retried);
        Assert.Equal("maito_kylma", result.Structure!.Department);
        Assert.Single(client.Prompts);
        Assert.Contains("maito oli vanhaa", client.Prompts[0]);
    }

    [Fact]
    public async Task InvalidThenValid_RetriesOnce_AndSucceeds()
    {
        var client = new ScriptedChatClient(InvalidJson, ValidJson);

        var result = await CreateService(client).StructureAsync("maito oli vanhaa");

        Assert.False(result.Failed);
        Assert.True(result.Retried);
        Assert.Equal(2, client.Prompts.Count);
        Assert.Contains("kylmäosasto", client.Prompts[1]); // retry prompt names the violation
    }

    [Fact]
    public async Task InvalidTwice_FailsWithRawPreserved_NeverThrows()
    {
        var client = new ScriptedChatClient(InvalidJson, "En osaa sanoa.");

        var result = await CreateService(client).StructureAsync("maito oli vanhaa");

        Assert.True(result.Failed);
        Assert.Null(result.Structure);
        Assert.True(result.Retried);
        Assert.Equal("En osaa sanoa.", result.RawResponse);
        Assert.Contains(result.Notes, n => n.StartsWith("first attempt:"));
        Assert.Contains(result.Notes, n => n.StartsWith("retry:"));
    }

    [Fact]
    public async Task FencedFirstResponse_SucceedsAsSalvaged_WithoutRetry()
    {
        var client = new ScriptedChatClient("```json\n" + ValidJson + "\n```");

        var result = await CreateService(client).StructureAsync("maito oli vanhaa");

        Assert.False(result.Failed);
        Assert.True(result.Salvaged);
        Assert.False(result.Retried);
        Assert.Single(client.Prompts);
    }

    /// <summary>Returns scripted responses in order; captures each prompt sent.</summary>
    private sealed class ScriptedChatClient(params string[] responses) : IChatClient
    {
        private int _next;

        public List<string> Prompts { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Prompts.Add(string.Concat(messages.Select(m => m.Text)));
            var text = responses[Math.Min(_next++, responses.Length - 1)];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
