using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace RetailFeedback.Llm;

internal sealed class OllamaLlmClientFactory(IOptions<LlmOptions> options) : ILlmClientFactory
{
    public IChatClient CreateForModel(string model, bool disableReasoning = false)
    {
        var client = new OllamaApiClient(new Uri(options.Value.BaseUrl), model);
        return disableReasoning ? new ReasoningOffChatClient(client) : client;
    }

    /// <summary>
    /// Seeds every request with the native think=false via the provider's
    /// RawRepresentationFactory escape hatch (honored by OllamaSharp's
    /// AbstractionMapper, verified against 5.4.25 source). Kept inside the Llm
    /// project so no caller ever touches OllamaSharp types.
    /// </summary>
    private sealed class ReasoningOffChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetResponseAsync(messages, WithThinkOff(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(messages, WithThinkOff(options), cancellationToken);

        private static ChatOptions WithThinkOff(ChatOptions? options)
        {
            var patched = options?.Clone() ?? new ChatOptions();
            patched.RawRepresentationFactory = _ => new ChatRequest { Think = false };
            return patched;
        }
    }
}
