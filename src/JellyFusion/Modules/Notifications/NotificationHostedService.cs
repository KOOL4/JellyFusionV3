using JellyFusion.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace JellyFusion.Modules.Notifications;

/// <summary>
/// Background service that listens for new library items and sends
/// notifications to Discord / Telegram when configured.
/// </summary>
public class NotificationHostedService : IHostedService, IDisposable
{
    private readonly ILibraryManager   _library;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<NotificationHostedService> _logger;

    public NotificationHostedService(
        ILibraryManager library,
        IHttpClientFactory http,
        ILogger<NotificationHostedService> logger)
    {
        _library = library;
        _http    = http;
        _logger  = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _library.ItemAdded += OnItemAdded;
        _logger.LogInformation("JellyFusion notification listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _library.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        _ = HandleNewItemAsync(e.Item);
    }

    private async Task HandleNewItemAsync(BaseItem item)
    {
        var cfg = Plugin.Instance?.Configuration?.Notifications;
        if (cfg is null) return;

        bool isKid = IsKidContent(item, Plugin.Instance?.Configuration?.Badges?.Status);

        if (cfg.NotifyNewContent || (cfg.NotifyKidContent && isKid))
        {
            string emoji = isKid ? "🧒" : "🎬";

            // Discord embed title does NOT render Markdown — use plain text.
            // Discord embed description DOES render **bold**, so keep it there.
            string discordTitle = $"{emoji} Nuevo contenido agregado";
            string discordBody  = $"**{item.Name}** ({item.ProductionYear})\n" +
                                  $"Tipo: {item.GetType().Name}\n" +
                                  (isKid ? "🎠 Contenido infantil" : "");

            // Telegram supports Markdown in the full message text.
            string telegramMsg  = $"{emoji} *Nuevo contenido agregado*\n" +
                                  $"*{item.Name}* ({item.ProductionYear})\n" +
                                  $"Tipo: {item.GetType().Name}\n" +
                                  (isKid ? "🎠 Contenido infantil" : "");

            if (cfg.Discord.Enabled && !string.IsNullOrEmpty(cfg.Discord.WebhookUrl))
                await SendDiscordAsync(cfg.Discord.WebhookUrl, discordTitle, discordBody);

            if (cfg.Telegram.Enabled &&
                !string.IsNullOrEmpty(cfg.Telegram.BotToken) &&
                !string.IsNullOrEmpty(cfg.Telegram.ChatId))
                await SendTelegramAsync(cfg.Telegram.BotToken, cfg.Telegram.ChatId, telegramMsg);
        }
    }

    private async Task SendDiscordAsync(string webhookUrl, string title, string body)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                    new
                    {
                        title       = title,
                        description = body,
                        color       = 0x00a4dc  // Jellyfin blue
                    }
                }
            });

            var client  = _http.CreateClient("JellyFusion");
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp    = await client.PostAsync(webhookUrl, content);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord notification failed");
        }
    }

    private async Task SendTelegramAsync(string token, string chatId, string message)
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
            var resp    = await client.PostAsync(url, content);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram notification failed");
        }
    }

    private static bool IsKidContent(BaseItem item, StatusBadgeConfig? cfg)
    {
        if (cfg is null || !cfg.KidEnabled) return false;
        var kidRatings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "G", "TV-Y", "TV-Y7", "TV-G", "PG" };
        return !string.IsNullOrEmpty(item.OfficialRating) && kidRatings.Contains(item.OfficialRating);
    }

    public void Dispose() { }
}
