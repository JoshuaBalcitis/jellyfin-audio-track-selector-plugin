using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.AudioTrackSelector;

/// <summary>Registers selection before Jellyfin generates playback URLs.</summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<DeviceCapabilityMatcher>();
        serviceCollection.AddSingleton<AudioTrackSelectionService>();
        serviceCollection.AddScoped<PlaybackInfoActionFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options =>
            options.Filters.Add(typeof(PlaybackInfoActionFilter)));
    }
}
