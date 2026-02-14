using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector;

/// <summary>
/// ASP.NET Core result filter that modifies PlaybackInfo responses
/// to set the optimal DefaultAudioStreamIndex before the client receives it.
/// </summary>
public class PlaybackInfoResultFilter : IAsyncResultFilter
{
    private readonly ILogger<PlaybackInfoResultFilter> _logger;
    private readonly AudioTrackSelectionService _selectionService;
    private readonly IDeviceManager _deviceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackInfoResultFilter"/> class.
    /// </summary>
    public PlaybackInfoResultFilter(
        ILogger<PlaybackInfoResultFilter> logger,
        AudioTrackSelectionService selectionService,
        IDeviceManager deviceManager)
    {
        _logger = logger;
        _selectionService = selectionService;
        _deviceManager = deviceManager;
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        try
        {
            if (Plugin.Instance?.Configuration?.Enabled == false)
            {
                await next();
                return;
            }

            // Check if this is a PlaybackInfo response
            if (context.Result is ObjectResult objectResult &&
                objectResult.Value is PlaybackInfoResponse playbackInfo &&
                playbackInfo.MediaSources != null)
            {
                // Get device profile from the request
                var deviceId = context.HttpContext.Request.Query["DeviceId"].FirstOrDefault()
                    ?? context.HttpContext.Request.Headers["X-Emby-Device-Id"].FirstOrDefault();

                MediaBrowser.Model.Dlna.DeviceProfile? deviceProfile = null;
                if (!string.IsNullOrEmpty(deviceId))
                {
                    var capabilities = _deviceManager.GetCapabilities(deviceId);
                    deviceProfile = capabilities?.DeviceProfile;
                }

                var preferredLanguage = Plugin.Instance?.Configuration?.PreferredAudioLanguage ?? "eng";

                foreach (var source in playbackInfo.MediaSources)
                {
                    var audioStreams = source.MediaStreams?
                        .Where(s => s.Type == MediaStreamType.Audio)
                        .ToList();

                    if (audioStreams == null || audioStreams.Count <= 1)
                    {
                        continue;
                    }

                    var optimalIndex = _selectionService.SelectOptimalAudioTrack(
                        source,
                        deviceProfile,
                        preferredLanguage);

                    if (optimalIndex.HasValue)
                    {
                        var oldDefault = source.DefaultAudioStreamIndex;
                        source.DefaultAudioStreamIndex = optimalIndex.Value;

                        _logger.LogWarning(
                            "AudioTrackSelector: [PlaybackInfo Filter] Changed DefaultAudioStreamIndex from {Old} to {New} for source '{Source}'",
                            oldDefault ?? -1,
                            optimalIndex.Value,
                            source.Name ?? source.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTrackSelector: Error in PlaybackInfo result filter");
        }

        await next();
    }
}
