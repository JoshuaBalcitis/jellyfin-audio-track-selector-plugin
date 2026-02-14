using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.AudioTrackSelector.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Services;

/// <summary>
/// Core service for ranking and selecting optimal audio tracks.
/// </summary>
public class AudioTrackSelectionService
{
    private readonly ILogger<AudioTrackSelectionService> _logger;
    private readonly DeviceCapabilityMatcher _capabilityMatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioTrackSelectionService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="capabilityMatcher">Device capability matcher.</param>
    public AudioTrackSelectionService(
        ILogger<AudioTrackSelectionService> logger,
        DeviceCapabilityMatcher capabilityMatcher)
    {
        _logger = logger;
        _capabilityMatcher = capabilityMatcher;
    }

    /// <summary>
    /// Selects the optimal audio track from available streams.
    /// </summary>
    /// <param name="mediaSource">The media source containing audio streams.</param>
    /// <param name="deviceProfile">The device profile to match against.</param>
    /// <param name="preferredLanguage">Preferred audio language (ISO 639 code).</param>
    /// <returns>The index of the optimal audio stream, or null if no selection made.</returns>
    public int? SelectOptimalAudioTrack(
        MediaSourceInfo mediaSource,
        DeviceProfile? deviceProfile,
        string? preferredLanguage = null)
    {
        if (mediaSource == null || mediaSource.MediaStreams == null)
        {
            _logger.LogDebug("MediaSource or MediaStreams is null, skipping audio track selection");
            return null;
        }

        // Get all audio streams
        var audioStreams = mediaSource.MediaStreams
            .Where(s => s.Type == MediaStreamType.Audio)
            .ToList();

        if (!audioStreams.Any())
        {
            _logger.LogDebug("No audio streams found");
            return null;
        }

        if (audioStreams.Count == 1)
        {
            _logger.LogDebug("Only one audio stream available, using it");
            return audioStreams[0].Index;
        }

        _logger.LogInformation(
            "Selecting audio track from {Count} available streams for device profile: {ProfileName}",
            audioStreams.Count,
            deviceProfile?.Name ?? "Unknown");

        // Filter compatible streams
        var compatibleStreams = FilterCompatibleStreams(audioStreams, deviceProfile);

        if (!compatibleStreams.Any())
        {
            _logger.LogWarning(
                "No compatible audio streams found for device profile {ProfileName}, trying fallback",
                deviceProfile?.Name ?? "Unknown");

            // Fallback: Try to find universally compatible stream (AAC/AC3 stereo)
            var fallbackStream = FindFallbackStream(audioStreams);
            if (fallbackStream != null)
            {
                _logger.LogInformation(
                    "Selected fallback audio track {Index}: {Codec} {Channels}ch",
                    fallbackStream.Index,
                    fallbackStream.Codec,
                    fallbackStream.Channels);
                return fallbackStream.Index;
            }

            _logger.LogWarning("No fallback stream found, using default selection");
            return null;
        }

        // Rank compatible streams
        var rankedStreams = RankAudioStreams(compatibleStreams, deviceProfile, preferredLanguage);

        var selectedStream = rankedStreams.FirstOrDefault();
        if (selectedStream.stream != null)
        {
            _logger.LogWarning(
                "✓ SELECTED audio track {Index}: \"{Codec}\" {Channels}ch {Bitrate}kbps (Score: {Score:F2}) - ORIGINAL DEFAULT WAS: {OriginalDefault}",
                selectedStream.stream.Index,
                selectedStream.stream.Codec,
                selectedStream.stream.Channels ?? 0,
                (selectedStream.stream.BitRate ?? 0) / 1000,
                selectedStream.score,
                mediaSource.DefaultAudioStreamIndex ?? -1);

            // Log all available tracks for comparison
            _logger.LogWarning("  All available audio tracks:");
            foreach (var stream in audioStreams.OrderBy(s => s.Index))
            {
                var marker = stream.Index == selectedStream.stream.Index ? "→ " : "  ";
                _logger.LogWarning(
                    "{Marker}Track {Index}: {Codec} {Channels}ch {Bitrate}kbps - Lang:{Lang} Title:{Title}",
                    marker,
                    stream.Index,
                    stream.Codec ?? "?",
                    stream.Channels ?? 0,
                    (stream.BitRate ?? 0) / 1000,
                    stream.Language ?? "?",
                    string.IsNullOrEmpty(stream.Title) ? "none" : stream.Title);
            }

            LogAudioStreamDetails(selectedStream.stream);

            return selectedStream.stream.Index;
        }

        _logger.LogWarning("No audio stream selected after ranking");
        return null;
    }

    /// <summary>
    /// Filters audio streams to only include those compatible with the device profile.
    /// </summary>
    private List<MediaStream> FilterCompatibleStreams(
        List<MediaStream> audioStreams,
        DeviceProfile? deviceProfile)
    {
        return audioStreams
            .Where(stream => _capabilityMatcher.CanPlayAudioStream(stream, deviceProfile))
            .ToList();
    }

    /// <summary>
    /// Ranks audio streams by quality and compatibility score.
    /// </summary>
    private List<(MediaStream stream, double score)> RankAudioStreams(
        List<MediaStream> streams,
        DeviceProfile? deviceProfile,
        string? preferredLanguage)
    {
        var rankedStreams = new List<(MediaStream stream, double score)>();

        foreach (var stream in streams)
        {
            var score = CalculateStreamScore(stream, deviceProfile, preferredLanguage);
            rankedStreams.Add((stream, score));

            _logger.LogDebug(
                "Stream {Index} ({Codec} {Channels}ch): Score = {Score:F2}",
                stream.Index,
                stream.Codec,
                stream.Channels,
                score);
        }

        return rankedStreams.OrderByDescending(x => x.score).ToList();
    }

    /// <summary>
    /// Calculates the quality score for an audio stream.
    /// </summary>
    private double CalculateStreamScore(
        MediaStream stream,
        DeviceProfile? deviceProfile,
        string? preferredLanguage)
    {
        // Weighted scoring formula:
        // Score = (CodecQuality × 40%) + (Channels × 30%) + (Bitrate × 15%) +
        //         (SpatialAudio × 10%) + (Language × 5%)

        var codecScore = GetCodecQualityScore(stream.Codec);
        var channelScore = GetChannelScore(stream.Channels, deviceProfile);
        var bitrateScore = GetBitrateScore(stream.BitRate);
        var spatialScore = GetSpatialAudioBonus(stream.AudioSpatialFormat);
        var languageScore = GetLanguageMatchBonus(stream.Language, preferredLanguage);

        var totalScore =
            (codecScore * 0.40) +
            (channelScore * 0.30) +
            (bitrateScore * 0.15) +
            (spatialScore * 0.10) +
            (languageScore * 0.05);

        return totalScore;
    }

    /// <summary>
    /// Gets the quality score for an audio codec (0-100 points).
    /// </summary>
    private double GetCodecQualityScore(string? codec)
    {
        if (string.IsNullOrEmpty(codec))
        {
            return 0;
        }

        var normalizedCodec = codec.ToLowerInvariant().Trim();

        // Lossless codecs (100 points)
        if (normalizedCodec.Contains("truehd") ||
            normalizedCodec.Contains("dts-hd ma") ||
            normalizedCodec.Contains("dts-hdma") ||
            normalizedCodec == "flac" ||
            normalizedCodec == "pcm" ||
            normalizedCodec == "alac")
        {
            return 100;
        }

        // High-quality lossy (80 points)
        if (normalizedCodec == "eac3" ||
            normalizedCodec == "ec3" ||
            normalizedCodec.Contains("dts-hd hra") ||
            normalizedCodec.Contains("dts-hdhra") ||
            normalizedCodec == "dts")
        {
            return 80;
        }

        // Standard lossy (60 points)
        if (normalizedCodec == "ac3" ||
            normalizedCodec == "aac")
        {
            return 60;
        }

        // Lower quality (40 points)
        if (normalizedCodec == "mp3" ||
            normalizedCodec == "vorbis" ||
            normalizedCodec == "opus")
        {
            return 40;
        }

        // Other supported codecs (20 points)
        return 20;
    }

    /// <summary>
    /// Gets the score for channel configuration (0-100 points).
    /// </summary>
    private double GetChannelScore(int? channels, DeviceProfile? deviceProfile)
    {
        if (!channels.HasValue || channels.Value <= 0)
        {
            return 0;
        }

        var maxSupportedChannels = _capabilityMatcher.GetMaxSupportedChannels(deviceProfile);

        // Score based on channel count relative to max supported
        // More channels = better (up to device maximum)
        var score = Math.Min(100, (channels.Value / (double)maxSupportedChannels) * 100);

        return score;
    }

    /// <summary>
    /// Gets the score for bitrate (0-100 points).
    /// </summary>
    private double GetBitrateScore(int? bitrate)
    {
        if (!bitrate.HasValue || bitrate.Value <= 0)
        {
            return 0;
        }

        // Reference point: 1.5 Mbps = 100 points
        // Higher bitrate = better quality (capped at 100)
        const int referenceBitrate = 1_500_000;
        var score = Math.Min(100, (bitrate.Value / (double)referenceBitrate) * 100);

        return score;
    }

    /// <summary>
    /// Gets the bonus score for spatial audio formats (0-10 points).
    /// </summary>
    private double GetSpatialAudioBonus(Jellyfin.Data.Enums.AudioSpatialFormat? spatialFormat)
    {
        if (!spatialFormat.HasValue)
        {
            return 0;
        }

        // Dolby Atmos or DTS:X
        if (spatialFormat.Value == Jellyfin.Data.Enums.AudioSpatialFormat.DolbyAtmos ||
            spatialFormat.Value == Jellyfin.Data.Enums.AudioSpatialFormat.DTSX)
        {
            return 10;
        }

        return 0;
    }

    /// <summary>
    /// Gets the bonus score for language match (0-5 points).
    /// </summary>
    private double GetLanguageMatchBonus(string? streamLanguage, string? preferredLanguage)
    {
        if (string.IsNullOrEmpty(preferredLanguage) || string.IsNullOrEmpty(streamLanguage))
        {
            return 0;
        }

        if (streamLanguage.Equals(preferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 0;
    }

    /// <summary>
    /// Finds a universally compatible fallback stream (AAC/AC3 stereo).
    /// </summary>
    private MediaStream? FindFallbackStream(List<MediaStream> audioStreams)
    {
        // Priority 1: AAC stereo
        var aacStereo = audioStreams.FirstOrDefault(s =>
            s.Codec?.Equals("aac", StringComparison.OrdinalIgnoreCase) == true &&
            s.Channels == 2);

        if (aacStereo != null)
        {
            return aacStereo;
        }

        // Priority 2: AC3 stereo
        var ac3Stereo = audioStreams.FirstOrDefault(s =>
            s.Codec?.Equals("ac3", StringComparison.OrdinalIgnoreCase) == true &&
            s.Channels == 2);

        if (ac3Stereo != null)
        {
            return ac3Stereo;
        }

        // Priority 3: Any AAC track
        var anyAac = audioStreams.FirstOrDefault(s =>
            s.Codec?.Equals("aac", StringComparison.OrdinalIgnoreCase) == true);

        if (anyAac != null)
        {
            return anyAac;
        }

        // Priority 4: Any AC3 track
        var anyAc3 = audioStreams.FirstOrDefault(s =>
            s.Codec?.Equals("ac3", StringComparison.OrdinalIgnoreCase) == true);

        return anyAc3;
    }

    /// <summary>
    /// Logs detailed information about the selected audio stream.
    /// </summary>
    private void LogAudioStreamDetails(MediaStream stream)
    {
        _logger.LogDebug(
            "Audio Stream Details - Index: {Index}, Codec: {Codec}, Channels: {Channels}, " +
            "ChannelLayout: {Layout}, Bitrate: {Bitrate}, SampleRate: {SampleRate}, " +
            "Language: {Language}, Title: {Title}, SpatialFormat: {Spatial}",
            stream.Index,
            stream.Codec,
            stream.Channels,
            stream.ChannelLayout,
            stream.BitRate,
            stream.SampleRate,
            stream.Language,
            stream.Title,
            stream.AudioSpatialFormat);
    }
}
