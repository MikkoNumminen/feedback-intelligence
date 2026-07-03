using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace RetailFeedback.Llm;

internal sealed class OllamaLlmClientFactory(IOptions<LlmOptions> options) : ILlmClientFactory
{
    public IChatClient CreateForModel(string model) =>
        new OllamaApiClient(new Uri(options.Value.BaseUrl), model);
}
