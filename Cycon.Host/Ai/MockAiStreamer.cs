using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cycon.Host.Ai;

public sealed class MockAiStreamer
{
    private readonly int _chunkChars;
    private readonly int _delayMs;
    private readonly int _phaseMs;

    public MockAiStreamer(int chunkChars = 24, int delayMs = 35, int phaseMs = 2500)
    {
        _chunkChars = Math.Clamp(chunkChars, 1, 256);
        _delayMs = Math.Clamp(delayMs, 0, 250);
        _phaseMs = Math.Clamp(phaseMs, 400, 4000);
    }

    public async Task StreamOnceAsync(
        IReadOnlyList<(string role, string text)> messages,
        IAiStreamSink sink,
        CancellationToken ct)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        sink.Status("Thinking...");
        await Task.Yield();

        var prompt = ExtractLastUserPrompt(messages);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            sink.Error("Missing prompt.");
            return;
        }

        // Prelude: brief thinking phase before streaming output.
        await Task.Delay(_phaseMs, ct).ConfigureAwait(false);

        // Deterministic, cheap mock output: short explanation + an echo of the request.
        var response =
            "Mock AI (no network):\n\n" +
            "I can't call OpenAI right now, so this is a local placeholder response.\n" +
            "Prompt:\n" +
            prompt.Trim() + "\n";

        var clearedStatus = false;
        var index = 0;
        while (index < response.Length)
        {
            ct.ThrowIfCancellationRequested();

            var take = Math.Min(_chunkChars, response.Length - index);
            if (!clearedStatus)
            {
                clearedStatus = true;
                sink.Status(string.Empty);
            }
            sink.TextDelta(response.Substring(index, take));
            index += take;

            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, ct).ConfigureAwait(false);
            }
            else
            {
                await Task.Yield();
            }
        }

        sink.Completed();
    }

    private static string ExtractLastUserPrompt(IReadOnlyList<(string role, string text)> messages)
    {
        if (messages is null) return string.Empty;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var (role, text) = messages[i];
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                return text ?? string.Empty;
            }
        }

        return messages.Count > 0 ? (messages[^1].text ?? string.Empty) : string.Empty;
    }
}
