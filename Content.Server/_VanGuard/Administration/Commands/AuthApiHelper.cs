using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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

    public static async Task<DateTimeOffset?> GetAccountCreationDateAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        try
        {
            var requestUri = $"{ApiEndpoint}?userid={Uri.EscapeDataString(userId)}";

            using var response = await HttpClient.GetAsync(requestUri);

            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("createdTime", out var createdTime) ||
                createdTime.ValueKind != JsonValueKind.String)
            {
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
                return null;
            }

            return creationDate;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
