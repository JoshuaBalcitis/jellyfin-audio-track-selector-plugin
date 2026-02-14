using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioTrackSelector.Configuration;
using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Providers;

/// <summary>
/// Media source provider that intercepts playback to select optimal audio tracks.
/// </summary>
public class AudioTrackMediaSourceProvider : IMediaSourceProvider
{
    private readonly ILogger<AudioTrackMediaSourceProvider> _logger;
    private readonly AudioTrackSelectionService _selectionService;
    private readonly ISessionManager _sessionManager;
    private readonly IDeviceManager _deviceManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioTrackMediaSourceProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="selectionService">Audio track selection service.</param>
    /// <param name="sessionManager">Session manager for accessing device profiles.</param>
    /// <param name="deviceManager">Device manager for getting device capabilities.</param>
    /// <param name="httpContextAccessor">HTTP context accessor for getting session info.</param>
    public AudioTrackMediaSourceProvider(
        ILogger<AudioTrackMediaSourceProvider> logger,
        AudioTrackSelectionService selectionService,
        ISessionManager sessionManager,
        IDeviceManager deviceManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _selectionService = selectionService;
        _sessionManager = sessionManager;
        _deviceManager = deviceManager;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            // Check if plugin is enabled
            if (Plugin.Instance?.Configuration?.Enabled == false)
            {
                _logger.LogDebug("AudioTrackSelector: Plugin is disabled, skipping");
                return Enumerable.Empty<MediaSourceInfo>();
            }

            // Get device profile from current session
            var deviceProfile = GetDeviceProfileFromHttpContext();

            // Get default media sources from the item (pass true for enablePathSubstitution)
            var mediaSources = item.GetMediaSources(true);

            if (mediaSources == null || !mediaSources.Any())
            {
                _logger.LogDebug(
                    "AudioTrackSelector: No media sources found for item {ItemName}",
                    item.Name);
                return Enumerable.Empty<MediaSourceInfo>();
            }

            _logger.LogInformation(
                "AudioTrackSelector: Processing {Count} media source(s) for item '{ItemName}' (Device: {DeviceName})",
                mediaSources.Count,
                item.Name,
                deviceProfile?.Name ?? "Unknown");

            await Task.CompletedTask; // Make method async-compliant

            // Modify each media source to select optimal audio track
            var modifiedSources = new List<MediaSourceInfo>();
            foreach (var source in mediaSources)
            {
                var audioStreams = source.MediaStreams?.Where(s => s.Type == MediaStreamType.Audio).ToList();

                if (audioStreams == null || !audioStreams.Any())
                {
                    _logger.LogDebug(
                        "AudioTrackSelector: No audio streams found in source {SourceId}",
                        source.Id);
                    modifiedSources.Add(source);
                    continue;
                }

                if (audioStreams.Count == 1)
                {
                    _logger.LogDebug(
                        "AudioTrackSelector: Only one audio stream available, using default");
                    modifiedSources.Add(source);
                    continue;
                }

                // Select optimal audio track
                var preferredLanguage = Plugin.Instance?.Configuration?.PreferredAudioLanguage ?? "eng";
                var optimalTrackIndex = _selectionService.SelectOptimalAudioTrack(
                    source,
                    deviceProfile,
                    preferredLanguage);

                if (optimalTrackIndex.HasValue)
                {
                    _logger.LogInformation(
                        "AudioTrackSelector: Setting default audio track to index {Index} for source '{SourceName}'",
                        optimalTrackIndex.Value,
                        source.Name ?? source.Id);

                    source.DefaultAudioStreamIndex = optimalTrackIndex.Value;
                }
                else
                {
                    _logger.LogWarning(
                        "AudioTrackSelector: No optimal audio track selected for source '{SourceName}', using default",
                        source.Name ?? source.Id);
                }

                modifiedSources.Add(source);
            }

            // Return modified sources
            return modifiedSources;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AudioTrackSelector: Error processing media sources for item {ItemName}",
                item.Name);

            // Return empty on error to fallback to default behavior
            return Enumerable.Empty<MediaSourceInfo>();
        }
    }

    /// <inheritdoc />
    public Task<ILiveStream> OpenMediaSource(
        string openToken,
        List<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
    {
        // This method would be used for live streams or custom media sources
        // Not needed for audio track selection, so we throw NotImplementedException
        throw new NotImplementedException("OpenMediaSource is not implemented for audio track selection");
    }

    /// <summary>
    /// Gets the device profile from the current HTTP context.
    /// </summary>
    /// <returns>Device profile or null if not found.</returns>
    private MediaBrowser.Model.Dlna.DeviceProfile? GetDeviceProfileFromHttpContext()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                _logger.LogDebug("AudioTrackSelector: No HTTP context available");
                return null;
            }

            // Try to get device ID from query string or headers
            var deviceId = httpContext.Request.Query["DeviceId"].FirstOrDefault()
                          ?? httpContext.Request.Headers["X-Emby-Device-Id"].FirstOrDefault()
                          ?? httpContext.Request.Headers["X-Emby-Client-Name"].FirstOrDefault();

            if (string.IsNullOrEmpty(deviceId))
            {
                _logger.LogDebug("AudioTrackSelector: No device ID found in HTTP request");
                return null;
            }

            // Get client capabilities from device manager
            var clientCapabilities = _deviceManager.GetCapabilities(deviceId);
            if (clientCapabilities == null)
            {
                _logger.LogDebug(
                    "AudioTrackSelector: No client capabilities found for device ID '{DeviceId}'",
                    deviceId);
                return null;
            }

            var deviceProfile = clientCapabilities.DeviceProfile;
            if (deviceProfile != null)
            {
                _logger.LogDebug(
                    "AudioTrackSelector: Found device profile '{ProfileName}' for device '{DeviceId}'",
                    deviceProfile.Name ?? "Unknown",
                    deviceId);
            }
            else
            {
                _logger.LogDebug(
                    "AudioTrackSelector: No device profile available for device '{DeviceId}'",
                    deviceId);
            }

            return deviceProfile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTrackSelector: Error getting device profile from HTTP context");
            return null;
        }
    }
}
