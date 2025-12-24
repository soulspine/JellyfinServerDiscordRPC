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
using Microsoft.AspNetCore.Mvc;
using DiscordRPC.Utility;
using Microsoft.AspNetCore.Identity.Data;

namespace DiscordRPC.Utility;

static class PlaybackEventHandler
{
    class PlaybackInfoContainer
    {
        public long PreviousTick { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public string Episode { get; set; } = string.Empty;
        public IMDbScraper.MovieMetadata? Metadata { get; set; }
        public bool IsPaused { get; set; }

    }

    /// <summary>
    /// Map of user IDs to their playback info containers. By design, there can only be one active
    /// playback session per user at any given time. It is respected by registering show's Title to
    /// ignore multiple sessions and only track the first one that was started.
    /// Freeing it up will result in the ability to track a new session for that user.
    /// </summary>
    private static readonly Dictionary<Guid, PlaybackInfoContainer> playbackInfoMap = new Dictionary<Guid, PlaybackInfoContainer>();
    private const string webhook = "https://discord.com/api/webhooks/1453187179437097135/C-T8ikr24S85hQUxMsR33LkvM4kzDBzmlOX8wbEmVEOqNHdMKzAUnT8U5xVAvTbj2WRy";
    private const double seekRange = TimeSpan.TicksPerSecond * 2;

    private static async Task sendWebhookMessage(string message, string? username = null)
    {
        if (Plugin.Instance == null) return;

        await Plugin.Instance._httpClientFactory.CreateClient().PostAsync(webhook, new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("content", message),
            new KeyValuePair<string, string>("username", username ?? "Media Server Bot")
        }));
    }

    private static int getSecondsFromTicks(long ticks)
    {
        return (int)TimeSpan.FromTicks(ticks).TotalSeconds;
    }

    /// <summary>
    /// This method is called when playback is updated (every second and on pause/resume).
    /// It is responsible for updating playback info (and discord RPC) by detecting skips, rewinds and pauses
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public static async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        var item = e.MediaInfo;

        // Initialize playback info container if it's not occupied yet
        if (!playbackInfoMap.ContainsKey(session.UserId))
        {
            playbackInfoMap[session.UserId] = new PlaybackInfoContainer(); // this is fast and we need to occupy this space quickly since there is a blocking await scraper down below, I could implement a lock but this also works and is easier to do
            playbackInfoMap[session.UserId].PreviousTick = e.PlaybackPositionTicks ?? 0;
            playbackInfoMap[session.UserId].Title = item.Name;
            playbackInfoMap[session.UserId].Season = item.SeasonName;
            playbackInfoMap[session.UserId].Episode = item.EpisodeTitle;
            playbackInfoMap[session.UserId].IsPaused = e.IsPaused;
            playbackInfoMap[session.UserId].Metadata = await IMDbScraper.GetImdbMetadata(item.GetProviderId(MetadataProvider.Imdb), Plugin.Instance!.Configuration.LanguageCode);
        }

        var playbackInfo = playbackInfoMap[session.UserId];

        // check for title/season/episode change - playback different from one we have stored
        if (playbackInfo.Title != item.Name || playbackInfo.Season != item.SeasonName || playbackInfo.Episode != item.EpisodeTitle)
        {
            return; // ignore different sessions
        }

        //check for pause but only on the first tick - it repeats itself every once in a while
        if (e.IsPaused && !playbackInfo.IsPaused)
        {
            // clear timestamp from presence and update it to paused        
            await sendWebhookMessage("pause");
        }

        //check for unpause but only on the first tick since they happen once every second
        if (!e.IsPaused && playbackInfo.IsPaused)
        {
            // update presence with new timestamps
            await sendWebhookMessage("unpause");
        }

        playbackInfo.IsPaused = e.IsPaused;

        // check for skips / rewinds
        if (e.PlaybackPositionTicks.HasValue && !playbackInfo.IsPaused)
        {
            if (Math.Abs(e.PlaybackPositionTicks.Value - playbackInfo.PreviousTick) > seekRange)
            {
                // seek occured
            }
            playbackInfo.PreviousTick = e.PlaybackPositionTicks.Value;
        }

    }

    public static async void OnPlaybackStop(object? sender, PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        var item = e.MediaInfo;

        if (!playbackInfoMap.ContainsKey(session.UserId))
        {
            return; // don't clear anything if nothing is there
        }

        var playbackInfo = playbackInfoMap[session.UserId];
        if (playbackInfo.Title != item.Name || playbackInfo.Season != item.SeasonName || playbackInfo.Episode != item.EpisodeTitle)
        {
            return; // ignore different sessions
        }

        playbackInfoMap.Remove(session.UserId);
    }

}