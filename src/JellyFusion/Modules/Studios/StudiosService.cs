using JellyFusion.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace JellyFusion.Modules.Studios;

public class StudiosService
{
    private readonly ILibraryManager        _library;
    private readonly ILogger<StudiosService> _logger;

    public StudiosService(ILibraryManager library, ILogger<StudiosService> logger)
    {
        _library = library;
        _logger  = logger;
    }

    /// <summary>
    /// Returns the count of items in the library that belong to <paramref name="studioName"/>.
    /// Used to validate that a studio has content before showing it in the UI.
    /// </summary>
    public int GetItemCountForStudio(string studioName)
    {
        try
        {
            // Jellyfin 10.10: InternalItemsQuery uses StudioIds (Guid[]) instead of Studios (string[]).
            // Resolve the studio entity by name first, then query by its Id.
            var studio = _library.GetStudio(studioName);
            if (studio is null || studio.Id == Guid.Empty) return 0;

            var query = new InternalItemsQuery
            {
                StudioIds = new[] { studio.Id },
                Recursive = true,
                Limit     = 1
            };
            return (int)_library.GetItemsResult(query).TotalRecordCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not count items for studio {Studio}", studioName);
            return 0;
        }
    }

    /// <summary>Returns the Jellyfin browse URL for a studio by name.</summary>
    public static string GetStudioBrowseUrl(StudioItem studio)
    {
        if (studio.LinkMode == "CustomUrl" && !string.IsNullOrEmpty(studio.CustomUrl))
            return studio.CustomUrl;

        // Auto mode — link to filtered library view
        var encoded = Uri.EscapeDataString(studio.Name);
        return $"/web/index.html#!/list?studios={encoded}&type=Movie,Series";
    }
}
