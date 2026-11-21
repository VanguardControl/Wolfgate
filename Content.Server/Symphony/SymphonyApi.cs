using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;

namespace Content.Server.Symphony;

/// <summary>
/// What the Symphony endpoints on the status host share: the token check /admin makes, and the hop from the status
/// host's thread onto the main thread, where game state may be read and written.
/// </summary>
public static class SymphonyApi
{
    private const string TokenScheme = "SS14Token";

    /// <summary>
    /// The same check /admin makes: an SS14Token header carrying admin.api_token. An unset token admits nobody.
    /// Answers 401 itself when the check fails, so the caller only has to stop.
    /// </summary>
    public static async Task<bool> AuthorisedAsync(IConfigurationManager cfg, IStatusHandlerContext context)
    {
        var token = cfg.GetCVar(CCVars.AdminApiToken);
        if (token != "" && context.RequestHeaders.TryGetValue("Authorization", out var header))
        {
            var value = header.ToString();
            var space = value.IndexOf(' ');
            if (space > 0 && value[..space] == TokenScheme)
            {
                var given = Encoding.UTF8.GetBytes(value[(space + 1)..].Trim());
                if (CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(token)))
                    return true;
            }
        }

        await context.RespondErrorAsync(HttpStatusCode.Unauthorized);
        return false;
    }

    /// <summary>
    /// Reads a JSON object body of at most <paramref name="limit"/> bytes. Too large stops reading at the limit and
    /// says so; anything that isn't a JSON object reads as null. Depth is capped by the parser's default (64).
    /// </summary>
    public static async Task<(System.Text.Json.Nodes.JsonObject? Body, bool TooLarge)> ReadJsonBodyAsync(IStatusHandlerContext context, int limit)
    {
        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.RequestBody.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                return (null, true);

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return (System.Text.Json.Nodes.JsonNode.Parse(buffer.ToArray()) as System.Text.Json.Nodes.JsonObject, false);
        }
        catch (Exception)
        {
            return (null, false);
        }
    }

    /// <summary>
    /// Runs the work on the main thread and hands its result, or its exception, back to the calling thread. The
    /// continuation is queued rather than run inline, so the caller's response writing never rides the game loop.
    /// </summary>
    public static Task<T> OnMainThread<T>(this ITaskManager tasks, Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        tasks.RunOnMainThread(() =>
        {
            try
            {
                done.TrySetResult(work());
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        });
        return done.Task;
    }
}
