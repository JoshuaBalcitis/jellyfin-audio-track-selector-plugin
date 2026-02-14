using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jellyfin.Plugin.AudioTrackSelector;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Register core services as singletons
        serviceCollection.AddSingleton<DeviceCapabilityMatcher>();
        serviceCollection.AddSingleton<AudioTrackSelectionService>();

        // Register the PlaybackInfo result filter to modify DefaultAudioStreamIndex
        // BEFORE the client receives the response (most reliable approach)
        serviceCollection.AddSingleton<PlaybackInfoResultFilter>();
        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            options.Filters.Add(typeof(PlaybackInfoResultFilter));
        });

        // Also keep the hosted service as a backup for runtime track switching
        serviceCollection.AddHostedService<PlaybackInterceptorService>();
    }
}
