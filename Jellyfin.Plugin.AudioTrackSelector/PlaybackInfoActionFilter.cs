using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector;

/// <summary>
/// Supplies the selected audio index before Jellyfin's POST PlaybackInfo action
/// computes direct-play support and transcoding URLs. Never changes a completed plan.
/// </summary>
public sealed class PlaybackInfoActionFilter : IAsyncActionFilter
{
    private readonly ILogger<PlaybackInfoActionFilter> _logger;
    private readonly AudioTrackSelectionService _selectionService;
    private readonly IDeviceManager _deviceManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaSourceManager _mediaSourceManager;

    /// <summary>Initializes the playback filter.</summary>
    public PlaybackInfoActionFilter(
        ILogger<PlaybackInfoActionFilter> logger,
        AudioTrackSelectionService selectionService,
        IDeviceManager deviceManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IMediaSourceManager mediaSourceManager)
    {
        _logger = logger;
        _selectionService = selectionService;
        _deviceManager = deviceManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (Plugin.Instance?.Configuration.Enabled != false &&
            context.ModelState.IsValid &&
            context.HttpContext.User.Identity?.IsAuthenticated == true &&
            context.ActionDescriptor is ControllerActionDescriptor descriptor &&
            descriptor.ControllerTypeInfo.FullName == "Jellyfin.Api.Controllers.MediaInfoController" &&
            descriptor.MethodInfo.Name == "GetPostedPlaybackInfo" &&
            context.ActionArguments.ContainsKey("audioStreamIndex") &&
            context.ActionArguments.ContainsKey("mediaSourceId"))
        {
            try
            {
                await SelectBeforePlanning(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Failure must leave the controller's original request intact.
                _logger.LogWarning(ex, "AudioTrackSelector: Selection failed; retaining Jellyfin's audio selection");
            }
        }

        // Keep controller exceptions outside the selection catch and execute exactly once.
        await next().ConfigureAwait(false);
    }

    private static bool HasSupportedRequestShape(object body)
    {
        var type = body.GetType();
        if (type.FullName != "Jellyfin.Api.Models.MediaInfoDtos.PlaybackInfoDto")
        {
            return false;
        }

        // A future server DTO change must preserve its own request handling rather
        // than making an explicit choice invisible to the plugin.
        return type.GetProperty("AudioStreamIndex")?.PropertyType == typeof(int?) &&
            type.GetProperty("MediaSourceId")?.PropertyType == typeof(string) &&
            type.GetProperty("UserId")?.PropertyType == typeof(Guid?) &&
            type.GetProperty("DeviceProfile")?.PropertyType == typeof(DeviceProfile) &&
            type.GetProperty("LiveStreamId")?.PropertyType == typeof(string) &&
            type.GetProperty("MaxAudioChannels")?.PropertyType == typeof(int?) &&
            type.GetProperty("MaxStreamingBitrate")?.PropertyType == typeof(int?);
    }

    private async Task SelectBeforePlanning(ActionExecutingContext context)
    {
        var args = context.ActionArguments;
        args.TryGetValue("playbackInfoDto", out var body);
        if (body is not null && !HasSupportedRequestShape(body))
        {
            return;
        }

        // Jellyfin.Api is not a published plugin dependency. Read only its bound DTO's
        // known properties; query arguments retain the same precedence as the controller.
        object? BodyValue(string name) => body?.GetType().GetProperty(name)?.GetValue(body);
        T? Value<T>(string argument, string property) where T : struct =>
            args.TryGetValue(argument, out var query) && query is T q ? q : BodyValue(property) is T b ? b : null;
        string? Text(string argument, string property) =>
            args.TryGetValue(argument, out var query) && query is string q ? q : BodyValue(property) as string;

        if (Value<int>("audioStreamIndex", "AudioStreamIndex").HasValue ||
            !string.IsNullOrEmpty(Text("liveStreamId", "LiveStreamId")))
        {
            return;
        }

        var principal = context.HttpContext.User;
        string? Claim(string type) => principal.Claims.FirstOrDefault(c =>
            c.Type.Equals(type, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!Guid.TryParse(Claim("Jellyfin-UserId"), out var authenticatedUserId) || authenticatedUserId == Guid.Empty)
        {
            return;
        }

        var userId = Value<Guid>("userId", "UserId");
        if (!userId.HasValue || userId == Guid.Empty)
        {
            userId = authenticatedUserId;
        }

        if (userId != authenticatedUserId && !principal.IsInRole("Administrator"))
        {
            return; // Let the controller enforce its authorization response.
        }

        var user = _userManager.GetUserById(userId.Value);
        if (user is null || !args.TryGetValue("itemId", out var id) || id is not Guid itemId)
        {
            return;
        }

        var item = _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is not Video)
        {
            return;
        }

        var profile = BodyValue("DeviceProfile") as DeviceProfile;
        var deviceId = Claim("Jellyfin-DeviceId");
        if (profile is null && !string.IsNullOrEmpty(deviceId))
        {
            profile = _deviceManager.GetCapabilities(deviceId)?.DeviceProfile;
        }

        if (profile is null)
        {
            return;
        }

        var sources = await _mediaSourceManager.GetPlaybackMediaSources(
            item, user, true, true, context.HttpContext.RequestAborted).ConfigureAwait(false);
        var requestedSource = Text("mediaSourceId", "MediaSourceId");
        var source = !string.IsNullOrWhiteSpace(requestedSource)
            ? sources.FirstOrDefault(s => string.Equals(s.Id, requestedSource, StringComparison.OrdinalIgnoreCase))
            : sources.FirstOrDefault(s => string.Equals(s.Id, itemId.ToString("N"), StringComparison.OrdinalIgnoreCase));
        if (source is null && string.IsNullOrWhiteSpace(requestedSource) && sources.Count == 1)
        {
            source = sources[0];
        }

        if (source is null || string.IsNullOrEmpty(source.Id) || source.RequiresOpening ||
            source.DefaultAudioIndexSource.HasFlag(AudioIndexSource.User) ||
            !string.IsNullOrEmpty(source.LiveStreamId) || source.MediaStreams.Count(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio) < 2)
        {
            return;
        }

        var selected = _selectionService.SelectOptimalAudioTrack(
            source,
            profile,
            Plugin.Instance?.Configuration.PreferredAudioLanguage ?? "eng",
            Value<int>("maxAudioChannels", "MaxAudioChannels"),
            Value<int>("maxStreamingBitrate", "MaxStreamingBitrate") ?? profile.MaxStreamingBitrate);
        if (!selected.HasValue)
        {
            return;
        }

        _logger.LogInformation("AudioTrackSelector: Selecting audio stream {Index} for source {SourceId} before playback planning", selected.Value, source.Id);
        // Both must be supplied: Jellyfin applies an explicit audio index only when
        // MediaSourceId matches. Resolve one source rather than switching every version.
        args["mediaSourceId"] = source.Id;
        args["audioStreamIndex"] = selected.Value;
    }
}
