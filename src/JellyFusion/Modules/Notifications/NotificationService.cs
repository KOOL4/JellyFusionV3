using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Notifications;

/// <summary>
/// Public API for sending ad-hoc notifications from other modules.
/// Also exposes a "send test" helper used by the config page.
/// </summary>
public class NotificationService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IHttpClientFactory http, ILogger<NotificationService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Sends a plain test message to the specified channel ("discord" | "telegram").
    /// Reads the current plugin configuration for webhook / bot credentials.
    /// </summary>
    public async Task SendTestAsync(string channel, CancellationToken ct = default)
    {
        var cfg = Plugin.Instance?.Configuration?.Notifications;
        if (cfg is null)
        {
            _logger.LogWarning("No notifications config available");
            return;
        }

        // Discord embed titles are plain text; descriptions support **bold**.
        const string discordTitle = "✅ JellyFusion — Test notification";
        const string discordBody  = "If you're reading this, notifications are working correctly.";

        // Telegram uses Markdown (MarkdownV1): *bold*, _italic_.
        const string telegramMsg  = "✅ *JellyFusion — Test notification*\nIf you're reading this, notifications are working correctly.";

        switch ((channel ?? "").ToLowerInvariant())
        {
            case "discord":
                if (cfg.Discord.Enabled && !string.IsNullOrEmpty(cfg.Discord.WebhookUrl))
                    await SendDiscordAsync(cfg.Discord.WebhookUrl!, discordTitle, discordBody, ct);
                else
                    _logger.LogWarning("Discord notifications are disabled or webhook is empty");
                break;

            case "telegram":
                if (cfg.Telegram.Enabled &&
                    !string.IsNullOrEmpty(cfg.Telegram.BotToken) &&
                    !string.IsNullOrEmpty(cfg.Telegram.ChatId))
                    await SendTelegramAsync(cfg.Telegram.BotToken!, cfg.Telegram.ChatId!, telegramMsg, ct);
                else
                    _logger.LogWarning("Telegram notifications are disabled or credentials missing");
                break;

            default:
                _logger.LogWarning("Unknown notification channel: {Channel}", channel);
                break;
        }
    }

    private async Task SendDiscordAsync(string webhookUrl, string title, string body, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                    new { title, description = body, color = 0x00a4dc }
                }
            });
            var client  = _http.CreateClient("JellyFusion");
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp    = await client.PostAsync(webhookUrl, content, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord test notification failed");
            throw;
        }
    }

    private async Task SendTelegramAsync(string token, string chatId, string message, CancellationToken ct)
    {
        try
        {
            var url     = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = JsonSerializer.Serialize(new
            {
                chat_id    = chatId,
                text       = message,
                parse_mode = "Markdown"
            });
            var client  = _http.CreateClient("JellyFusion");
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp    = await client.PostAsync(url, content, ct);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram test notification failed");
            throw;
        }
    }
}
