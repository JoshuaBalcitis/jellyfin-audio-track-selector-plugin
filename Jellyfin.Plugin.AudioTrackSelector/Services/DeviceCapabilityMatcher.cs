using System;
using System.Linq;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Services;

/// <summary>
/// Checks native audio support using the client's declared playback capabilities.
/// </summary>
public class DeviceCapabilityMatcher
{
    private readonly ILogger<DeviceCapabilityMatcher> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceCapabilityMatcher"/> class.</summary>
    /// <param name="logger">Logger instance.</param>
    public DeviceCapabilityMatcher(ILogger<DeviceCapabilityMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>Checks whether a stream is explicitly supported for its media source.</summary>
    /// <param name="audioStream">Candidate audio stream.</param>
    /// <param name="deviceProfile">Client's declared capabilities.</param>
    /// <param name="mediaSource">Source container and media context.</param>
    /// <param name="maxAudioChannels">Request channel limit.</param>
    /// <param name="maxStreamingBitrate">Request streaming bitrate limit.</param>
    /// <returns>Whether the stream can be selected without relying on audio transcoding.</returns>
    public bool CanPlayAudioStream(
        MediaStream audioStream,
        DeviceProfile? deviceProfile,
        MediaSourceInfo? mediaSource = null,
        int? maxAudioChannels = null,
        long? maxStreamingBitrate = null)
    {
        if (audioStream == null || audioStream.Type != MediaStreamType.Audio ||
            audioStream.IsExternal || string.IsNullOrWhiteSpace(audioStream.Codec) || deviceProfile == null)
        {
            return false;
        }

        if (maxAudioChannels.HasValue &&
            (!audioStream.Channels.HasValue || audioStream.Channels > maxAudioChannels.Value))
        {
            return false;
        }

        var isVideo = mediaSource == null ||
            mediaSource.MediaStreams.Any(stream => stream.Type == MediaStreamType.Video);
        var videoCodec = mediaSource?.MediaStreams.FirstOrDefault(stream => stream.Type == MediaStreamType.Video)?.Codec;
        var container = mediaSource?.Container ?? string.Empty;
        var mediaType = isVideo ? DlnaProfileType.Video : DlnaProfileType.Audio;

        // A transcoding profile describes an output format, not native input support.
        // An empty codec declaration gives us no evidence to override Jellyfin's default.
        var supported = deviceProfile.DirectPlayProfiles.Any(profile =>
            profile.Type == mediaType &&
            !string.IsNullOrWhiteSpace(profile.AudioCodec) &&
            profile.SupportsAudioCodec(audioStream.Codec) &&
            (!isVideo || (!string.IsNullOrWhiteSpace(videoCodec) && profile.SupportsVideoCodec(videoCodec))) &&
            profile.SupportsContainer(container));
        if (!supported)
        {
            _logger.LogDebug("Audio codec {Codec} is not declared for {Container} by {Profile}",
                audioStream.Codec, container, deviceProfile.Name);
            return false;
        }

        var isSecondaryAudio = mediaSource?.IsSecondaryAudio(audioStream) ?? false;
        var codecType = isVideo ? CodecType.VideoAudio : CodecType.Audio;
        var codecProfiles = deviceProfile.CodecProfiles.Where(profile =>
            profile.Type == codecType && profile.ContainsAnyCodec(audioStream.Codec, container));

        foreach (var profile in codecProfiles)
        {
            // ApplyConditions determine whether a restriction applies, exactly as in Jellyfin.
            if (profile.ApplyConditions.All(condition => Satisfies(condition, audioStream, isVideo, isSecondaryAudio)) &&
                !profile.Conditions.All(condition => Satisfies(condition, audioStream, isVideo, isSecondaryAudio)))
            {
                return false;
            }
        }

        long? bitrateLimit = deviceProfile.MaxStreamingBitrate;
        if (maxStreamingBitrate.HasValue)
        {
            bitrateLimit = bitrateLimit.HasValue
                ? Math.Min(bitrateLimit.Value, maxStreamingBitrate.Value)
                : maxStreamingBitrate.Value;
        }

        if (bitrateLimit.HasValue)
        {
            // Account for the candidate track rather than applying a music limit to video audio.
            long? bitrate = audioStream.BitRate;
            if (isVideo)
            {
                var videoStreams = mediaSource?.MediaStreams.Where(stream => stream.Type == MediaStreamType.Video).ToArray();
                bitrate = videoStreams != null && videoStreams.Length > 0 &&
                    videoStreams.All(stream => stream.BitRate.HasValue) && audioStream.BitRate.HasValue
                    ? videoStreams.Sum(stream => (long)stream.BitRate!.Value) + audioStream.BitRate.Value
                    : null;
            }

            if (!bitrate.HasValue || bitrate.Value > bitrateLimit.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Satisfies(ProfileCondition condition, MediaStream stream, bool isVideo, bool isSecondaryAudio)
    {
        return isVideo
            ? ConditionProcessor.IsVideoAudioConditionSatisfied(condition, stream.Channels, stream.BitRate,
                stream.SampleRate, stream.BitDepth, stream.Profile, isSecondaryAudio)
            : ConditionProcessor.IsAudioConditionSatisfied(condition, stream.Channels, stream.BitRate,
                stream.SampleRate, stream.BitDepth);
    }
}
