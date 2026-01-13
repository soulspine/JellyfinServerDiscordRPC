using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using DiscordRPC.Configuration;
using DiscordRPC.Utility;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;
using DiscordRPC.Utility.Discord;

namespace DiscordRPC;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <inheritdoc />
    public override string Name => "Discord RPC";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("ab0a8ab3-ceb0-49b0-980c-087d2a9c320b");
    public readonly ILogger Logger;
    public readonly BotHandler DiscordBotHandler;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    private readonly ISessionManager _sessionManager;
    public readonly IHttpClientFactory _httpClientFactory;


    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ISessionManager sessionManager,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        BotHandler botHandler,
        PlaybackEventHandler playbackEventHandler
    )
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _sessionManager = sessionManager;
        _httpClientFactory = httpClientFactory;
        _sessionManager.PlaybackProgress += async (s, e) => { await playbackEventHandler.OnPlaybackProgress(s, e); };
        _sessionManager.PlaybackStopped += async (s, e) => { await playbackEventHandler.OnPlaybackStop(s, e); };

        Logger = loggerFactory.CreateLogger("DiscordRPC");
        DiscordBotHandler = botHandler;

        var config = Instance.Configuration;

        if (ulong.TryParse(config.DiscordImagesChannelId, out var channelId))
        {
            botHandler.SetParameters(config.DiscordBotToken, channelId);
            _ = botHandler.Start();
            Logger.LogInformation("Discord bot was initialized properly");
        }


    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "Discord RPC Configuration",
                EnableInMainMenu = true,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
