using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Extensions;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Websocket.Client;
using Newtonsoft.Json;
using System.Text.Json.Nodes;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using J2N;
using System.Runtime.Serialization;
using System.ComponentModel.DataAnnotations;
using MediaBrowser.Controller.Entities;
using System.Linq;


namespace DiscordRPC.Utility.API;

public class DiscordHandler : IDisposable
{
    private WebsocketClient? _ws;
    private const string INITIAL_URL = "wss://gateway.discord.gg";
    private string _cachedUrl;
    private string _session_id;
    private int? _sequenceNum;
    private int _heartbeatInterval;
    private readonly string _token;
    private bool _isDisposed;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatTask;
    private bool _isReady;
    static private Random _random = new Random();
    /// <summary>
    /// Holds update websocket message.
    /// Will be executed only after connection is established.
    /// </summary>

    private static class Payloads
    {
        public static string Heartbeat(int? sequenceNum)
        {
            return JsonConvert.SerializeObject(new
            {
                op = 1,
                d = sequenceNum,
            });
        }
        public static string Identify(string userToken)
        {
            return JsonConvert.SerializeObject(new
            {
                op = 2,
                d = new
                {
                    token = userToken,
                    intents = 0,
                    properties = new
                    {
                        os = "linux",
                        browser = "chrome",
                        device = "jellyfin",
                    },
                }
            });
        }
        public static string Resume(string userToken, string sessionId, int sequenceNum)
        {
            return JsonConvert.SerializeObject(new
            {
                op = 6,
                d = new
                {
                    token = userToken,
                    session_id = sessionId,
                    seq = sequenceNum,
                }
            });
        }
        public readonly static string ActivityClear = JsonConvert.SerializeObject(new
        {
            op = 3,
            d = new
            {
                status = "online",
                activities = new object[] { },
                afk = false,
                since = (long?)null,
            },
        });
    }

    public bool IsReady()
    {
        return _isReady;
    }

    public DiscordHandler(string token)
    {
        _isDisposed = false;
        _isReady = false;
        _token = token;
        _cachedUrl = INITIAL_URL;
        initializeWebsocket();
    }

    // todo add state and state_url
    public void UpdatePresence(string activityName, string? detailsUrl = null, long? startTime = null, long? endTime = null)
    {
        if (!_isReady) return;

        dynamic? timestampsObj = null;
        if (startTime.HasValue || endTime.HasValue)
        {
            timestampsObj = new
            {
                start = startTime,
                end = endTime
            };
        }

        /*dynamic? assetsObj = null;
        if (!string.IsNullOrEmpty(imageUrl))
        {
            assetsObj = new
            {
                large_image = imageUrl,
                large_text = "text",
            };
        }*/

        _ws?.Send(
            JsonConvert.SerializeObject(new
            {
                op = 3,
                d = new
                {
                    activities = new[]
                                    {
                            new
                            {
                                name = "Jellyfin",
                                details = activityName,
                                details_url = detailsUrl,
                                type = 3,
                                timestamps = timestampsObj,
                                status_display_type = 2,
                            }
                        },
                    status = "dnd",
                    afk = true,
                    since = 0,
                },
            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })
        );
    }
    public void ClearPresence()
    {
        if (_isReady)
        {
            _ws?.Send(Payloads.ActivityClear);
        }
    }

    private void initializeWebsocket()
    {
        _ws?.Stop(WebSocketCloseStatus.NormalClosure, "GG");
        _ws?.Dispose();
        _ws = new WebsocketClient(new Uri($"{_cachedUrl}/?v=10&encoding=json"));
        _ws.ReconnectionHappened.Subscribe(info => _ = OnConnect(info));
        _ws.DisconnectionHappened.Subscribe(info => _ = OnDisconnect(info));
        _ws.MessageReceived.Subscribe(info => _ = OnMessage(info.Text));
        _ = _ws.Start();
    }

    private void sendHeatbeat()
    {
        if (_ws != null && _ws.IsRunning)
        {
            _ws.Send(Payloads.Heartbeat(_sequenceNum));
            Plugin.Log("Heartbeat sent");
        }
    }

    private async Task heartbeatLoop(CancellationTokenSource cts)
    {
        await Task.Delay((int)(_heartbeatInterval * _random.NextSingle()));
        while (!cts.IsCancellationRequested)
        {
            sendHeatbeat();
            await Task.Delay(_heartbeatInterval);
        }
    }

    private async Task OnConnect(ReconnectionInfo info)
    {
        Plugin.Log("Ws connected");
        if (_cachedUrl == INITIAL_URL) //definitely first connect
        {
            _ws?.Send(Payloads.Identify(_token));
            Plugin.Log("Sent Identify Payload");
        }
        else // reconnect
        {
            _ws?.Send(Payloads.Resume(_token, _session_id, _sequenceNum!.Value));
            Plugin.Log("Sent Resume Payload");
        }

    }

    private async Task OnDisconnect(DisconnectionInfo info)
    {
        if (info.CloseStatus == WebSocketCloseStatus.NormalClosure) return; // user-invoked one

        _isReady = false;
        Plugin.Log("Ws disconnected");
        if (!_isDisposed)
        {
            Plugin.Log("Reconnecting...");
            initializeWebsocket();
        }
    }

    private async Task OnMessage(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        dynamic j = JsonConvert.DeserializeObject(msg)!;

        int opCode = j.op;
        dynamic data = j.d;
        string? eventName = j.t;
        int? sequenceNum = j.s;

        Plugin.Log($"Ws message: {opCode} {eventName} {sequenceNum}");

        if (sequenceNum.HasValue) _sequenceNum = sequenceNum.Value;

        switch (opCode)
        {
            case 7: //RECONNECT
                {
                    initializeWebsocket();
                    break;
                }
            case 9:
                {
                    _cachedUrl = INITIAL_URL;
                    _sequenceNum = null;
                    await Task.Delay(3000);
                    initializeWebsocket();
                    break;
                }
            case 10: // HELLO
                {
                    _heartbeatInterval = data.heartbeat_interval;
                    _heartbeatCts?.Cancel();
                    _heartbeatCts = new CancellationTokenSource();
                    await Task.Run(() => heartbeatLoop(_heartbeatCts));
                    break;
                }
        }

        switch (eventName)
        {
            case "READY":
                {
                    _isReady = true;
                    _session_id = data.session_id;
                    _cachedUrl = data.resume_gateway_url;
                    break;
                }
            case "SESSIONS_REPLACE":
                {
                    Plugin.Log(JsonConvert.SerializeObject(data));
                    break;
                }
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _heartbeatCts?.Cancel();
            _ws?.Dispose();
        }
    }
}
