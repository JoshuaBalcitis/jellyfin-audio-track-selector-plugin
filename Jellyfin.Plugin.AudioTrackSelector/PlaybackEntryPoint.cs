using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Plugin.AudioTrackSelector.Configuration;
using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector;

/// <summary>
/// Background service that initializes playback event subscriptions after Jellyfin services are ready.
/// Uses a delayed initialization pattern to ensure ISessionManager is available.
/// </summary>
public class PlaybackEntryPoint
{
    private readonly ILogger<PlaybackEntryPoint> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDeviceManager _deviceManager;
    private readonly AudioTrackSelectionService _selectionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackEntryPoint"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="sessionManager">Session manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="deviceManager">Device manager.</param>
    /// <param name="selectionService">Audio track selection service.</param>
    public PlaybackEntryPoint(
        ILogger<PlaybackEntryPoint> logger,
        ISessionManager sessionManager,
        ILibraryManager libraryManager,
        IDeviceManager deviceManager,
        AudioTrackSelectionService selectionService)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _libraryManager = libraryManager;
        _deviceManager = deviceManager;
        _selectionService = selectionService;

        _logger.LogWarning("AudioTrackSelector: PlaybackEntryPoint CONSTRUCTOR called");
    }

    /// <inheritdoc />
    public Task RunAsync()
    {
        try
        {
            _logger.LogWarning("AudioTrackSelector: PlaybackEntryPoint RunAsync() CALLED - subscribing to events");

            // Subscribe to playback start events
            _sessionManager.PlaybackStart += OnPlaybackStart;

            _logger.LogWarning("AudioTrackSelector: SUCCESSFULLY subscribed to PlaybackStart events!");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTrackSelector: FAILED to subscribe to events in RunAsync");
            throw;
        }
    }

    /// <summary>
    /// Handles playback start events to analyze and recommend optimal audio track.
    /// </summary>
    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            _logger.LogWarning("AudioTrackSelector: ===== PLAYBACK START EVENT FIRED =====");

            if (Plugin.Instance?.Configuration?.Enabled == false)
            {
                _logger.LogDebug("AudioTrackSelector: Plugin is disabled, skipping");
                return;
            }

            if (e.Item == null || e.Session == null)
            {
                _logger.LogWarning("AudioTrackSelector: No item or session in event");
                return;
            }

            _logger.LogWarning(
                "AudioTrackSelector: Playback for '{ItemName}' on '{DeviceName}' ({Client})",
                e.Item.Name,
                e.Session.DeviceName,
                e.Session.Client);

            // Get the full item
            var item = _libraryManager.GetItemById(e.Item.Id);
            if (item == null)
            {
                _logger.LogWarning("AudioTrackSelector: Item not found");
                return;
            }

            // Get media sources
            var mediaSources = item.GetMediaSources(true);
            if (mediaSources == null || !mediaSources.Any())
            {
                _logger.LogDebug("AudioTrackSelector: No media sources");
                return;
            }

            // Get device profile
            var capabilities = _deviceManager.GetCapabilities(e.Session.DeviceId);
            var deviceProfile = capabilities?.DeviceProfile;

            _logger.LogWarning(
                "AudioTrackSelector: Device profile: {ProfileName}",
                deviceProfile?.Name ?? "None");

            // Analyze audio tracks
            foreach (var source in mediaSources)
            {
                var audioStreams = source.MediaStreams?.Where(s => s.Type == MediaStreamType.Audio).ToList();
                if (audioStreams == null || audioStreams.Count <= 1) continue;

                var optimalIndex = _selectionService.SelectOptimalAudioTrack(
                    source,
                    deviceProfile,
                    Plugin.Instance?.Configuration?.PreferredAudioLanguage ?? "eng");

                if (optimalIndex.HasValue)
                {
                    var optimal = audioStreams.FirstOrDefault(s => s.Index == optimalIndex.Value);
                    var current = audioStreams.FirstOrDefault(s => s.Index == source.DefaultAudioStreamIndex);

                    _logger.LogWarning(
                        "AudioTrackSelector: Current={Cur} ({CCodec} {CCh}ch), Optimal={Opt} ({OCodec} {OCh}ch)",
                        source.DefaultAudioStreamIndex ?? -1,
                        current?.Codec ?? "?",
                        current?.Channels ?? 0,
                        optimalIndex.Value,
                        optimal?.Codec ?? "?",
                        optimal?.Channels ?? 0);

                    _logger.LogWarning("AudioTrackSelector: Available tracks:");
                    foreach (var s in audioStreams.OrderBy(x => x.Index))
                    {
                        _logger.LogWarning(
                            "  [{Index}] {Codec} {Ch}ch {Br}kbps - {Lang}",
                            s.Index,
                            s.Codec,
                            s.Channels ?? 0,
                            (s.BitRate ?? 0) / 1000,
                            s.Language ?? "?");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTrackSelector: Error in playback handler");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources.
    /// </summary>
    /// <param name="disposing">True if disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.LogWarning("AudioTrackSelector: PlaybackEntryPoint disposing - unsubscribing from events");
            _sessionManager.PlaybackStart -= OnPlaybackStart;
        }
    }
}
