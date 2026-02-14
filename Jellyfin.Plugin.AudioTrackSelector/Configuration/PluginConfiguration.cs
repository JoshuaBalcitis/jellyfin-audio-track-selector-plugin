using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AudioTrackSelector.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // Set default configuration values
        Enabled = true;
        EnableFalsePositiveDetection = true;
        PreferredAudioLanguage = "eng";
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether false positive detection is enabled.
    /// </summary>
    public bool EnableFalsePositiveDetection { get; set; }

    /// <summary>
    /// Gets or sets the preferred audio language (ISO 639 code, e.g., "eng", "spa", "fra").
    /// </summary>
    public string PreferredAudioLanguage { get; set; }

    // Future enhancements:
    // - Codec priority override list
    // - Per-user settings
    // - Custom scoring weights
}
