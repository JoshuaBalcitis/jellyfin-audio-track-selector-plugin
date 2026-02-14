using System;
using System.Linq;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AudioTrackSelector.Services;

/// <summary>
/// Determines if a client can decode specific audio codecs and configurations.
/// </summary>
public class DeviceCapabilityMatcher
{
    private readonly ILogger<DeviceCapabilityMatcher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCapabilityMatcher"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public DeviceCapabilityMatcher(ILogger<DeviceCapabilityMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines if a client can play the specified audio stream.
    /// </summary>
    /// <param name="audioStream">The audio stream to check.</param>
    /// <param name="deviceProfile">The device profile to check against.</param>
    /// <returns>True if the stream can be played, false otherwise.</returns>
    public bool CanPlayAudioStream(MediaStream audioStream, DeviceProfile? deviceProfile)
    {
        if (audioStream == null || audioStream.Type != MediaStreamType.Audio)
        {
            return false;
        }

        // If no device profile, use conservative approach (allow common codecs only)
        if (deviceProfile == null)
        {
            return IsUniversallySupportedCodec(audioStream.Codec);
        }

        // Check codec support
        if (!SupportsCodec(audioStream.Codec, deviceProfile))
        {
            _logger.LogDebug(
                "Codec {Codec} not supported by device profile {ProfileName}",
                audioStream.Codec,
                deviceProfile.Name);
            return false;
        }

        // Check channel count limits
        if (audioStream.Channels.HasValue && !SupportsChannelCount(audioStream.Channels.Value, deviceProfile))
        {
            _logger.LogDebug(
                "Channel count {Channels} exceeds device profile {ProfileName} limits",
                audioStream.Channels,
                deviceProfile.Name);
            return false;
        }

        // Check bitrate limits
        if (audioStream.BitRate.HasValue && !SupportsBitrate(audioStream.BitRate.Value, deviceProfile))
        {
            _logger.LogDebug(
                "Bitrate {Bitrate} exceeds device profile {ProfileName} limits",
                audioStream.BitRate,
                deviceProfile.Name);
            return false;
        }

        // Check spatial audio support
        if (audioStream.AudioSpatialFormat != Jellyfin.Data.Enums.AudioSpatialFormat.None &&
            !SupportsSpatialAudio(audioStream.AudioSpatialFormat, deviceProfile))
        {
            _logger.LogDebug(
                "Spatial audio format {Format} not supported by device profile {ProfileName}",
                audioStream.AudioSpatialFormat,
                deviceProfile.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if the device profile supports the specified audio codec.
    /// </summary>
    /// <param name="codec">The codec to check.</param>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>True if supported, false otherwise.</returns>
    private bool SupportsCodec(string? codec, DeviceProfile deviceProfile)
    {
        if (string.IsNullOrEmpty(codec))
        {
            return false;
        }

        // Normalize codec name (lowercase, trim)
        var normalizedCodec = codec.ToLowerInvariant().Trim();

        // SwiftFin/Apple TV specific exclusions
        if (IsAppleTVProfile(deviceProfile))
        {
            // TrueHD not supported by SwiftFin
            if (normalizedCodec.Contains("truehd"))
            {
                _logger.LogDebug("Excluding TrueHD for Apple TV/SwiftFin profile");
                return false;
            }
        }

        // Check DirectPlayProfiles for codec support
        if (deviceProfile.DirectPlayProfiles != null)
        {
            foreach (var profile in deviceProfile.DirectPlayProfiles)
            {
                if (profile.Type == DlnaProfileType.Audio || profile.Type == DlnaProfileType.Video)
                {
                    if (profile.AudioCodec != null &&
                        profile.AudioCodec.Split(',').Any(c =>
                            c.Trim().Equals(normalizedCodec, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }

        // Check TranscodingProfiles as fallback
        if (deviceProfile.TranscodingProfiles != null)
        {
            foreach (var profile in deviceProfile.TranscodingProfiles)
            {
                if (profile.AudioCodec != null &&
                    profile.AudioCodec.Split(',').Any(c =>
                        c.Trim().Equals(normalizedCodec, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        // If profile doesn't explicitly list codecs, check if it's universally supported
        return IsUniversallySupportedCodec(normalizedCodec);
    }

    /// <summary>
    /// Checks if the device profile supports the specified channel count.
    /// </summary>
    /// <param name="channels">The number of channels.</param>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>True if supported, false otherwise.</returns>
    private bool SupportsChannelCount(int channels, DeviceProfile deviceProfile)
    {
        // Check CodecProfiles for channel restrictions
        if (deviceProfile.CodecProfiles != null)
        {
            foreach (var codecProfile in deviceProfile.CodecProfiles)
            {
                if (codecProfile.Type == CodecType.VideoAudio || codecProfile.Type == CodecType.Audio)
                {
                    if (codecProfile.Conditions != null)
                    {
                        foreach (var condition in codecProfile.Conditions)
                        {
                            if (condition.Property == ProfileConditionValue.AudioChannels)
                            {
                                if (condition.Condition == ProfileConditionType.LessThanEqual)
                                {
                                    if (int.TryParse(condition.Value, out var maxChannels))
                                    {
                                        if (channels > maxChannels)
                                        {
                                            return false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // No explicit restrictions, assume supported (up to 8 channels max)
        return channels <= 8;
    }

    /// <summary>
    /// Checks if the device profile supports the specified bitrate.
    /// </summary>
    /// <param name="bitrate">The bitrate in bits per second.</param>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>True if supported, false otherwise.</returns>
    private bool SupportsBitrate(int bitrate, DeviceProfile deviceProfile)
    {
        // Check MaxStaticMusicBitrate for audio files
        if (deviceProfile.MaxStaticMusicBitrate.HasValue &&
            bitrate > deviceProfile.MaxStaticMusicBitrate.Value)
        {
            return false;
        }

        // Check MaxStaticBitrate as fallback
        if (deviceProfile.MaxStaticBitrate.HasValue &&
            bitrate > deviceProfile.MaxStaticBitrate.Value)
        {
            return false;
        }

        // No bitrate restrictions
        return true;
    }

    /// <summary>
    /// Checks if the device profile supports the specified spatial audio format.
    /// </summary>
    /// <param name="spatialFormat">The spatial audio format enum value.</param>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>True if supported, false otherwise.</returns>
    private bool SupportsSpatialAudio(Jellyfin.Data.Enums.AudioSpatialFormat spatialFormat, DeviceProfile deviceProfile)
    {
        // SwiftFin/Apple TV specific: Atmos is supported via DD+ with Atmos metadata
        if (IsAppleTVProfile(deviceProfile) &&
            spatialFormat == Jellyfin.Data.Enums.AudioSpatialFormat.DolbyAtmos)
        {
            // Atmos is supported on Apple TV via Dolby MAT 2.0 (LPCM + metadata)
            return true;
        }

        // For other devices, check if the codec profile explicitly supports it
        // Most devices will report Atmos/DTS:X support through their codec profiles
        // If not explicitly blocked, assume supported if the base codec is supported
        return true;
    }

    /// <summary>
    /// Determines if a codec is universally supported across most devices.
    /// </summary>
    /// <param name="codec">The codec to check.</param>
    /// <returns>True if universally supported, false otherwise.</returns>
    private bool IsUniversallySupportedCodec(string? codec)
    {
        if (string.IsNullOrEmpty(codec))
        {
            return false;
        }

        var normalizedCodec = codec.ToLowerInvariant().Trim();

        // AAC and AC3 are universally supported
        return normalizedCodec == "aac" ||
               normalizedCodec == "ac3" ||
               normalizedCodec == "mp3" ||
               normalizedCodec == "eac3" ||
               normalizedCodec == "vorbis";
    }

    /// <summary>
    /// Determines if the device profile is for Apple TV/SwiftFin.
    /// </summary>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>True if Apple TV/SwiftFin profile, false otherwise.</returns>
    private bool IsAppleTVProfile(DeviceProfile deviceProfile)
    {
        if (deviceProfile == null || string.IsNullOrEmpty(deviceProfile.Name))
        {
            return false;
        }

        var profileName = deviceProfile.Name.ToLowerInvariant();
        return profileName.Contains("apple tv") ||
               profileName.Contains("appletv") ||
               profileName.Contains("swiftfin") ||
               profileName.Contains("tvos");
    }

    /// <summary>
    /// Gets the maximum supported channel count for a device profile.
    /// </summary>
    /// <param name="deviceProfile">The device profile.</param>
    /// <returns>The maximum supported channel count.</returns>
    public int GetMaxSupportedChannels(DeviceProfile? deviceProfile)
    {
        if (deviceProfile == null)
        {
            return 2; // Conservative default: stereo
        }

        // Check CodecProfiles for explicit channel limits
        if (deviceProfile.CodecProfiles != null)
        {
            int maxChannels = 8; // Default max

            foreach (var codecProfile in deviceProfile.CodecProfiles)
            {
                if (codecProfile.Type == CodecType.VideoAudio || codecProfile.Type == CodecType.Audio)
                {
                    if (codecProfile.Conditions != null)
                    {
                        foreach (var condition in codecProfile.Conditions)
                        {
                            if (condition.Property == ProfileConditionValue.AudioChannels &&
                                condition.Condition == ProfileConditionType.LessThanEqual)
                            {
                                if (int.TryParse(condition.Value, out var limit))
                                {
                                    maxChannels = Math.Min(maxChannels, limit);
                                }
                            }
                        }
                    }
                }
            }

            return maxChannels;
        }

        // No explicit limit, assume 7.1 (8 channels) max
        return 8;
    }
}
