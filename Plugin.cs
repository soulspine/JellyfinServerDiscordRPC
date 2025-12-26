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
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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

    private const string webhook = "https://discord.com/api/webhooks/1453187179437097135/C-T8ikr24S85hQUxMsR33LkvM4kzDBzmlOX8wbEmVEOqNHdMKzAUnT8U5xVAvTbj2WRy";

    public readonly ILogger _logger;
    public static void Log(string msg)
    {
        Instance!._logger.LogInformation(msg);
    }
    public async Task sendWebhookMessage(string message, string? username = null)
    {
        await _httpClientFactory.CreateClient().PostAsync(webhook, new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("content", message),
            new KeyValuePair<string, string>("username", username ?? "Media Server Bot")
        }));
    }

    public static PluginConfiguration Config()
    {
        return Instance!.Configuration;
    }

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
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ISessionManager sessionManager, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _sessionManager = sessionManager;
        _httpClientFactory = httpClientFactory;
        _sessionManager.PlaybackStopped += PlaybackEventHandler.OnPlaybackStop;
        _sessionManager.PlaybackProgress += PlaybackEventHandler.OnPlaybackProgress;
        _logger = loggerFactory.CreateLogger("DiscordRPC");
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
