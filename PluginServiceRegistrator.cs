using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using DiscordRPC.Utility.Discord;
using DiscordRPC.Utility;

namespace DiscordRPC;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost
    )
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton<BotHandler>();
        serviceCollection.AddSingleton<PlaybackEventHandler>();
        serviceCollection.AddSingleton<IMDbScraper>();
    }
}