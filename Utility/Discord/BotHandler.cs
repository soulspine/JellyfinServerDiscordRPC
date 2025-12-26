using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace DiscordRPC.Utility.Discord;

public class BotHandler
{
    private readonly DiscordSocketClient _client;
    private bool _parametersSet;
    private ulong _channelId;
    private IMessageChannel? _channel;
    private static int MAX_RETRIES = 5;
    private string _token;
    public BotHandler()
    {
        _parametersSet = false;
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
        });
    }

    public bool SetParameters(string token, ulong channelId)
    {
        if (_parametersSet) return false;

        _parametersSet = true;
        _token = token;
        _channelId = channelId;

        return true;
    }

    public async Task<string?> GetMediaProxyForThisImage(string imageUrl)
    {
        if (!_parametersSet || _channel == null) return null;

        var newMsg = await _channel.GetMessageAsync((await _channel.SendMessageAsync(imageUrl)).Id);
        if (newMsg == null) return null;

        int i = 0;
        while (newMsg.Embeds.Count == 0)
        {
            if (++i >= MAX_RETRIES) return null;
            await Task.Delay(100);
            newMsg = await _channel.GetMessageAsync(newMsg.Id);
        }

        var proxyUrl = newMsg.Embeds.FirstOrDefault()?.Thumbnail?.ProxyUrl;

        if (!string.IsNullOrEmpty(proxyUrl))
        {
            int idx = proxyUrl.IndexOf("external/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                proxyUrl = proxyUrl[idx..]; // od external/ do końca
        }

        await newMsg.DeleteAsync();

        return proxyUrl;

    }

    public async Task Start()
    {
        if (!_parametersSet) throw new InvalidOperationException("Bot parameters not set");

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();

        var tcs = new TaskCompletionSource<bool>();
        _client.Ready += () =>
        {
            tcs.SetResult(true);
            return Task.CompletedTask;
        };
        await tcs.Task;

        _channel = await _client.GetChannelAsync(_channelId) as IMessageChannel;

        if (_channel == null)
        {
            Plugin.Instance?.Logger.LogWarning("Tried to start the discord bot but channel could not be reached");
            await _client.StopAsync();
            return;
        }
    }
}