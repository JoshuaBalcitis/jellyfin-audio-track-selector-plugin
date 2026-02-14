using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Services;

/// <summary>
/// Hosted service that subscribes to playback events and switches audio tracks.
/// </summary>
public class PlaybackInterceptorService : IHostedService, IDisposable
{
    private readonly ILogger<PlaybackInterceptorService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDeviceManager _deviceManager;
    private readonly AudioTrackSelectionService _selectionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackInterceptorService"/> class.
    /// </summary>
    public PlaybackInterceptorService(
        ILogger<PlaybackInterceptorService> logger,
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

        _logger.LogWarning("AudioTrackSelector: PlaybackInterceptorService CONSTRUCTOR called");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("AudioTrackSelector: PlaybackInterceptorService StartAsync CALLED");
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _logger.LogWarning("AudioTrackSelector: Successfully subscribed to PlaybackStart events!");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("AudioTrackSelector: PlaybackInterceptorService stopping");
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        return Task.CompletedTask;
    }

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

            // Get the full item from library manager
            var item = _libraryManager.GetItemById(e.Item.Id);
            if (item == null)
            {
                _logger.LogWarning("AudioTrackSelector: Item not found in library");
                return;
            }

            // Get media sources
            var mediaSources = item.GetMediaSources(true);
            if (mediaSources == null || !mediaSources.Any())
            {
                _logger.LogDebug("AudioTrackSelector: No media sources found");
                return;
            }

            // Get device profile
            var capabilities = _deviceManager.GetCapabilities(e.Session.DeviceId);
            var deviceProfile = capabilities?.DeviceProfile;

            _logger.LogWarning(
                "AudioTrackSelector: Device profile: {ProfileName}",
                deviceProfile?.Name ?? "None (will use conservative defaults)");

            // Process each media source
            foreach (var source in mediaSources)
            {
                var audioStreams = source.MediaStreams?.Where(s => s.Type == MediaStreamType.Audio).ToList();
                if (audioStreams == null || audioStreams.Count <= 1)
                {
                    continue;
                }

                var preferredLanguage = Plugin.Instance?.Configuration?.PreferredAudioLanguage ?? "eng";
                var optimalIndex = _selectionService.SelectOptimalAudioTrack(
                    source,
                    deviceProfile,
                    preferredLanguage);

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

                    // Log all available tracks
                    _logger.LogWarning("AudioTrackSelector: Available tracks:");
                    foreach (var s in audioStreams.OrderBy(x => x.Index))
                    {
                        var marker = s.Index == optimalIndex.Value ? " >>>" : "    ";
                        _logger.LogWarning(
                            "{Marker} [{Index}] {Codec} {Ch}ch {Br}kbps - {Lang}",
                            marker,
                            s.Index,
                            s.Codec,
                            s.Channels ?? 0,
                            (s.BitRate ?? 0) / 1000,
                            s.Language ?? "?");
                    }

                    // ACTIVELY SWITCH to the optimal audio track
                    if (optimalIndex.Value != source.DefaultAudioStreamIndex)
                    {
                        try
                        {
                            _logger.LogWarning(
                                "AudioTrackSelector: SWITCHING audio track from {From} to {To}",
                                source.DefaultAudioStreamIndex ?? -1,
                                optimalIndex.Value);

                            var command = new GeneralCommand
                            {
                                Name = GeneralCommandType.SetAudioStreamIndex
                            };
                            command.Arguments["Index"] = optimalIndex.Value.ToString();

                            _sessionManager.SendGeneralCommand(
                                e.Session.Id,
                                e.Session.Id,
                                command,
                                CancellationToken.None);

                            _logger.LogWarning(
                                "AudioTrackSelector: Successfully sent SetAudioStreamIndex command!");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "AudioTrackSelector: Failed to send audio track switch command");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("AudioTrackSelector: Optimal track is already the default, no switch needed");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AudioTrackSelector: Error in playback start handler");
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
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
        }
    }
}
