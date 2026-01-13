using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace DiscordRPC.Utility.Discord;

public class BotHandler : IDisposable
{
    private readonly DiscordSocketClient _client;
    private bool _parametersSet;
    private ulong _channelId;
    private IMessageChannel? _channel;
    private string _token;
    public BotHandler()
    {
        _parametersSet = false;
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
        });
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public bool SetParameters(string token, ulong channelId)
    {
        if (_parametersSet) return false;

        _parametersSet = true;
        _token = token;
        _channelId = channelId;

        return true;
    }

    public string? cutBeforeExternal(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        int index = url.IndexOf("attachments", StringComparison.OrdinalIgnoreCase);

        if (index == -1) return null;

        return url.Substring(index);
    }

    public async Task RemoveMessage(ulong id)
    {
        if (!_parametersSet || _channel == null) return;
        await _channel.DeleteMessageAsync(id);
    }

    public async Task<Tuple<string, ulong>?> GetMediaProxyForThisImage(string filepath)
    {

        if (!File.Exists(filepath) || !_parametersSet || _channel == null) return null;

        var newMsg = await _channel.SendFileAsync(filepath);
        if (newMsg == null) return null;

        // cannot delete it here, only after the playback because discord does not hold them in memory for very long
        //_ = newMsg.DeleteAsync();

        return new Tuple<string, ulong>(("mp:" + cutBeforeExternal(newMsg.Attachments.FirstOrDefault()?.ProxyUrl) + "&width=280&height=280").Replace("&&", "&"), newMsg.Id);
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
        Plugin.Instance?.Logger.LogInformation("Discord bot started");
    }
}