using FeedbackIntelligence.Llm;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>
/// Pins the CRLF→LF prompt normalization. A CRLF copy of the alert-verify prompt
/// flipped Poro's greedy safety judgment (kyllä→ei) and silently disabled the
/// no-keyword safety alert — see ADR-0018. Prompt bytes are LLM input, so line
/// endings must be normalized on load regardless of OS / git checkout.
/// </summary>
public class AppPathResolverTests
{
    [Fact]
    public void NormalizeNewlines_ConvertsCrlfAndLoneCr_ToLf()
    {
        Assert.Equal("a\nb\nc", AppPathResolver.NormalizeNewlines("a\r\nb\rc"));
        Assert.DoesNotContain('\r', AppPathResolver.NormalizeNewlines("x\r\ny\r\nz"));
        Assert.Equal("already\nlf", AppPathResolver.NormalizeNewlines("already\nlf")); // idempotent
    }

    [Fact]
    public async Task ReadPromptAsync_StripsCrlf_FromFileOnDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prompt-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "Arvioi\r\n\r\nVastaus: {{text}}\r\n");
        try
        {
            var text = await AppPathResolver.ReadPromptAsync(path);
            Assert.DoesNotContain('\r', text);
            Assert.Equal("Arvioi\n\nVastaus: {{text}}\n", text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
