using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Slider;

/// <summary>Background service stub for slider pre-warming and refresh tasks.</summary>
public class SliderHostedService : IHostedService
{
    private readonly ILogger<SliderHostedService> _logger;

    public SliderHostedService(ILogger<SliderHostedService> logger)
        => _logger = logger;

    public Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("JellyFusion Slider module started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
