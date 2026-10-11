using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Services;

/// <summary>Ranks compatible audio tracks, honoring language before quality.</summary>
public class AudioTrackSelectionService
{
    private static readonly Dictionary<string, string> LanguageCodes = BuildLanguageCodes();
    private readonly ILogger<AudioTrackSelectionService> _logger;
    private readonly DeviceCapabilityMatcher _capabilityMatcher;

    /// <summary>Initializes a new instance of the <see cref="AudioTrackSelectionService"/> class.</summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="capabilityMatcher">Device capability matcher.</param>
    public AudioTrackSelectionService(
        ILogger<AudioTrackSelectionService> logger,
        DeviceCapabilityMatcher capabilityMatcher)
    {
        _logger = logger;
        _capabilityMatcher = capabilityMatcher;
    }

    /// <summary>Selects a supported audio track; null preserves Jellyfin's default selection.</summary>
    /// <param name="mediaSource">Source containing candidate streams.</param>
    /// <param name="deviceProfile">Client capabilities.</param>
    /// <param name="preferredLanguage">Preferred ISO 639 language code.</param>
    /// <param name="maxAudioChannels">Request channel limit.</param>
    /// <param name="maxStreamingBitrate">Request streaming bitrate limit.</param>
    /// <returns>Selected stream index, or null when support cannot be established.</returns>
    public int? SelectOptimalAudioTrack(
        MediaSourceInfo mediaSource,
        DeviceProfile? deviceProfile,
        string? preferredLanguage = null,
        int? maxAudioChannels = null,
        long? maxStreamingBitrate = null)
    {
        if (mediaSource?.MediaStreams == null || deviceProfile == null)
        {
            return null;
        }

        var language = NormalizeLanguage(preferredLanguage);
        var selected = mediaSource.MediaStreams
            .Where(stream => _capabilityMatcher.CanPlayAudioStream(stream, deviceProfile, mediaSource,
                maxAudioChannels, maxStreamingBitrate))
            .OrderByDescending(stream => language.Length > 0 && NormalizeLanguage(stream.Language) == language)
            .ThenByDescending(CalculateQualityScore)
            .ThenByDescending(stream => stream.Index == mediaSource.DefaultAudioStreamIndex)
            .ThenBy(stream => stream.Index)
            .FirstOrDefault();

        if (selected == null)
        {
            _logger.LogDebug("No natively supported audio track found for {Profile}; preserving server selection",
                deviceProfile.Name);
            return null;
        }

        _logger.LogDebug("Selected audio track {Index}: {Codec}, {Channels} channels, {Language}, for {Profile}",
            selected.Index, selected.Codec, selected.Channels, selected.Language, deviceProfile.Name);
        return selected.Index;
    }

    private static double CalculateQualityScore(MediaStream stream)
    {
        // Each component uses the same 0–100 scale; spatial metadata is a quality
        // preference, not evidence of device support (which is supplied by its profile).
        var channels = Math.Min(100, Math.Max(0, stream.Channels ?? 0) / 8.0 * 100);
        var bitrate = Math.Min(100, Math.Max(0, stream.BitRate ?? 0) / 1_500_000.0 * 100);
        var spatial = stream.AudioSpatialFormat is AudioSpatialFormat.DolbyAtmos or AudioSpatialFormat.DTSX ? 100 : 0;
        return GetCodecQualityScore(stream.Codec) * 0.40 + channels * 0.30 + bitrate * 0.20 + spatial * 0.10;
    }

    private static double GetCodecQualityScore(string? codec)
    {
        var normalized = codec?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Contains("truehd") || normalized.Contains("dts-hd ma") ||
            normalized.Contains("dts-hdma") || normalized.StartsWith("pcm", StringComparison.Ordinal) ||
            normalized is "flac" or "alac")
        {
            return 100;
        }

        if (normalized is "eac3" or "ec3" or "dts" || normalized.Contains("dts-hd hra") ||
            normalized.Contains("dts-hdhra"))
        {
            return 80;
        }

        return normalized switch
        {
            "ac3" or "aac" => 60,
            "mp3" or "vorbis" or "opus" => 40,
            "" => 0,
            _ => 20
        };
    }

    private static string NormalizeLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant().Split('-', '_')[0] ?? string.Empty;
        return LanguageCodes.TryGetValue(normalized, out var code) ? code : normalized;
    }

    private static Dictionary<string, string> BuildLanguageCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            codes[culture.TwoLetterISOLanguageName] = culture.ThreeLetterISOLanguageName;
            codes[culture.ThreeLetterISOLanguageName] = culture.ThreeLetterISOLanguageName;
        }

        // ISO 639-2 bibliographic aliases still occur in media metadata.
        foreach (var pair in new[] { ("ger", "deu"), ("fre", "fra"), ("chi", "zho"), ("dut", "nld"),
            ("cze", "ces"), ("slo", "slk"), ("rum", "ron"), ("gre", "ell"), ("wel", "cym"),
            ("alb", "sqi"), ("arm", "hye"), ("baq", "eus"), ("bur", "mya"), ("geo", "kat"),
            ("ice", "isl"), ("mac", "mkd"), ("may", "msa"), ("per", "fas"), ("tib", "bod") })
        {
            codes[pair.Item1] = pair.Item2;
        }

        return codes;
    }
}
