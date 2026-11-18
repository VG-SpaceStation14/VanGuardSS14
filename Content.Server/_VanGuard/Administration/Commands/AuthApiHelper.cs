using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Robust.Shared.Log;

namespace Content.Server._VanGuard.Administration;

/// <summary>
/// Retrieves account registration metadata from the SS14 authentication service.
/// Failures are intentionally non-fatal: account metadata must never block a player connection.
/// </summary>
public static class AuthApiHelper
{
    private const string ApiEndpoint =
        "https://auth.spacestation14.com/api/query/userid";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ISawmill Sawmill =
        Logger.GetSawmill("vanguard.auth");

    public static async Task<DateTimeOffset?> GetAccountCreationDateAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        try
        {
            var requestUri = $"{ApiEndpoint}?userid={Uri.EscapeDataString(userId)}";

            using var response = await HttpClient.GetAsync(requestUri);

            if (!response.IsSuccessStatusCode)
            {
                Sawmill.Warning(
                    $"Account lookup failed for {userId}: HTTP {(int) response.StatusCode}.");
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("createdTime", out var createdTime) ||
                createdTime.ValueKind != JsonValueKind.String)
            {
                Sawmill.Warning($"Account creation timestamp missing for {userId}.");
                return null;
            }

            var timestamp = createdTime.GetString();
            if (string.IsNullOrWhiteSpace(timestamp) ||
                !DateTimeOffset.TryParse(
                    timestamp,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var creationDate))
            {
                Sawmill.Warning($"Invalid account creation timestamp for {userId}.");
                return null;
            }

            return creationDate;
        }
        catch (OperationCanceledException e)
        {
            Sawmill.Warning($"Account lookup timed out or was canceled for {userId}: {e.Message}");
        }
        catch (HttpRequestException e)
        {
            Sawmill.Warning($"Account lookup HTTP error for {userId}: {e.Message}");
        }
        catch (JsonException e)
        {
            Sawmill.Warning($"Account lookup returned invalid JSON for {userId}: {e.Message}");
        }
        catch (Exception e)
        {
            Sawmill.Error($"Unexpected account lookup failure for {userId}: {e}");
        }

        return null;
    }
}
