using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Badges;

/// <summary>Thin wrapper around ILibraryManager for badge-related item lookups.</summary>
public class BadgeService
{
    private readonly ILibraryManager        _library;
    private readonly ILogger<BadgeService>  _logger;

    public BadgeService(ILibraryManager library, ILogger<BadgeService> logger)
    {
        _library = library;
        _logger  = logger;
    }

    public BaseItem? GetItem(Guid itemId)
    {
        try { return _library.GetItemById(itemId); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve item {ItemId}", itemId);
            return null;
        }
    }
}
