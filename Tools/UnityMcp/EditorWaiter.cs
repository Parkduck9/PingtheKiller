using System.Text.Json;

namespace UnityMcp;

/// <summary>
/// Waits for the editor to reach a state, polling the bridge's <c>status</c> command. Disconnects are
/// expected while Unity compiles and reloads its domain, so they reset the settle timer instead of failing.
/// </summary>
public sealed class EditorWaiter(BridgeClient bridge)
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan StatusTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>True when nothing is compiling, importing, reloading or switching play mode.</summary>
    public static bool IsIdle(JsonElement status) =>
        !GetBool(status, "isCompiling") && !GetBool(status, "isUpdating")
        && GetBool(status, "isPlaying") == GetBool(status, "isPlayingOrWillChangePlaymode");

    /// <summary>
    /// Returns the first status for which <paramref name="condition"/> and <see cref="IsIdle"/> have held
    /// continuously for <paramref name="settle"/>. Throws <see cref="TimeoutException"/> otherwise.
    /// </summary>
    public async Task<JsonElement> WaitForStableAsync(Func<JsonElement, bool> condition, TimeSpan timeout, TimeSpan settle, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        DateTime? stableSince = null;
        string lastState = "never connected";
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var status = await bridge.CallAsync("status", null, StatusTimeout, ct).ConfigureAwait(false);
                lastState = Describe(status);
                if (IsIdle(status) && condition(status))
                {
                    stableSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - stableSince >= settle) return status;
                }
                else
                {
                    stableSince = null;
                }
            }
            catch (Exception ex) when (ex is BridgeUnavailableException or BridgeTimeoutException)
            {
                stableSince = null;
                lastState = "disconnected (domain reload in progress or editor closed)";
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0} s waiting for the Unity editor. Last state: {lastState}.");
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    public static string Describe(JsonElement status)
    {
        if (GetBool(status, "isCompiling")) return "compiling";
        if (GetBool(status, "isUpdating")) return "importing assets";
        if (GetBool(status, "isPlaying") != GetBool(status, "isPlayingOrWillChangePlaymode")) return "switching play mode";
        if (GetBool(status, "isPlaying")) return GetBool(status, "isPaused") ? "playing (paused)" : "playing";
        return "idle";
    }

    public static bool GetBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
