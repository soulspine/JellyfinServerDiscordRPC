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
using Microsoft.AspNetCore.Identity.Data;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using J2N;
using System.ComponentModel;
using DiscordRPC;
using ICU4N.Text;
using System.Dynamic;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using DiscordRPC.Utility.Discord;
using Jellyfin.Database.Implementations.Entities;
using DiscordRPC.Utility.Discord.GatewayDTO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;

namespace DiscordRPC.Utility;

public class PlaybackEventHandler
{
    /// <summary>
    /// Class representing info and fields needed to properly validate and track stream events.
    /// \\TODO: Make this handle multiple streams since discord gateway allows to specify >1 activity per session/
    /// </summary>
    public class PlaybackInfoContainer
    {
        /// <summary>
        /// Gets updated on every playback event. Used to detect skips and rewinds.
        /// </summary>
        public long PreviousTick { get; set; }
        public long PreviousTimestamp { get; set; }
        public string SessionId { get; set; }
        /// <summary>
        /// Used to only allow for one Discord Rich Presence update when the stream is started.
        /// It exists because setting presence outright in the first event happenened before IMDb metadata was collected.
        /// </summary>
        public bool FirstPresenceSetHappened { get; set; }
        /// <summary>
        /// Whether the player is paused, used to track changes in playback state.
        /// </summary>
        public bool IsPaused { get; set; }
        /// <summary>
        /// Dedicated object handling the Discord Gateway connection. Lets PlaybackEventHandler update rich presences.
        /// </summary>
        public UserHandler Discord { get; set; }
        public bool FetchingImage { get; set; }
        public string? ImageLink { get; set; }
        public ulong? ImageMessageId { get; set; }
        private async Task fetchImage(string imagePath)
        {
            FetchingImage = true;
            var s = await Plugin.Instance!.DiscordBotHandler.GetMediaProxyForThisImage(imagePath);
            if (s != null)
            {
                ImageLink = s.Item1;
                ImageMessageId = s.Item2;
            }

            FetchingImage = false;
        }
        public PlaybackInfoContainer(
            string discordToken,
            string sessionId,
            long previousTick = 0,
            long previousTimestamp = 0,
            bool firstPresenceSetHappened = false,
            bool isPaused = false,
            string imagePath = ""
        )
        {
            SessionId = sessionId;
            PreviousTick = previousTick;
            PreviousTimestamp = previousTimestamp;
            FirstPresenceSetHappened = firstPresenceSetHappened;
            IsPaused = isPaused;
            Discord = new UserHandler(discordToken);
            _ = fetchImage(imagePath);
        }
    }

    /// <summary>
    /// Map of user IDs to their playback info containers. By design, there can only be one active
    /// playback session per user at any given time. It is respected by registering show's Title to
    /// ignore multiple sessions and only track the first one that was started.
    /// Freeing it up will result in the ability to track a new session for that user.
    /// </summary>
    private readonly Dictionary<Guid, PlaybackInfoContainer> _infoMap = new Dictionary<Guid, PlaybackInfoContainer>();
    /// <summary>
    /// This method is called when playback is updated (every second and on pause/resume).
    /// It is responsible for updating playback info (and discord RPC) by detecting skips, rewinds and pauses
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        var item = session.FullNowPlayingItem;

        // only video for now
        if (item.MediaType != MediaType.Video) return;

        var discordToken = Plugin.Instance!.Configuration.UserTokens.FirstOrDefault(x => x.Username == session.UserName)?.DiscordToken;

        if (string.IsNullOrEmpty(discordToken)) return;

        PlaybackInfoContainer info;

        // Initialize playback info container if it's not occupied yet
        if (!_infoMap.TryGetValue(session.UserId, out info!))
        {

            _infoMap[session.UserId] = new PlaybackInfoContainer(
                discordToken,
                e.PlaySessionId,
                e.PlaybackPositionTicks ?? 0,
                previousTimestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                imagePath: item.GetImagePath(ImageType.Primary)
            );
            info = _infoMap[session.UserId];
        }

        // ignore different sessions
        if (e.PlaySessionId != info.SessionId) return;

        bool updatePaused = false;
        bool updatePlaying = false;

        // update presence only 1 time after initial stream start
        if (!info.FirstPresenceSetHappened && info.FetchingImage == false && info.Discord.IsReady())
        {
            updatePlaying = true;
            info.FirstPresenceSetHappened = true;
        }

        //check for pause but only on the first tick - it repeats itself every once in a while
        if (e.IsPaused && !info.IsPaused) updatePaused = true;

        //check for unpause but only on the first tick since they happen once every second
        if (!e.IsPaused && info.IsPaused) updatePlaying = true;

        info.IsPaused = e.IsPaused;

        if (e.PlaybackPositionTicks.HasValue && !info.IsPaused)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var deltaTicks = e.PlaybackPositionTicks.Value - info.PreviousTick;
            var deltaTimeMs = nowMs - info.PreviousTimestamp;

            var speed = (double)deltaTicks / deltaTimeMs;
            var expectedSpeed = TimeSpan.TicksPerSecond / 1000.0; // ticks/ms

            if (Math.Abs(speed) > expectedSpeed * 3)
            {
                updatePlaying = true;
            }

            info.PreviousTick = e.PlaybackPositionTicks.Value;
            info.PreviousTimestamp = nowMs;
        }


        if (updatePaused || updatePlaying)
        {
            string? details = null;
            string? detailsUrl = null;
            string? state = null;
            string? stateUrl = null;

            BaseItem mainItem = item;

            if (item.IndexNumber != null) // series
            {
                mainItem = item.GetParent();
                state = $"S{item.ParentIndexNumber!.Value:D2}E{item.IndexNumber!.Value:D2}";
                stateUrl = $"https://imdb.com/title/{item.GetProviderId(MetadataProvider.Imdb)}";
            }

            details = Plugin.Instance!.Configuration.LocalizedNames ? mainItem.Name : mainItem.OriginalTitle;
            detailsUrl = $"https://imdb.com/title/{mainItem.GetProviderId(MetadataProvider.Imdb)}";

            Assets? assetsObj = null;
            if (info.ImageLink != null)
            {
                assetsObj = new Assets(
                    largeImage: info.ImageLink,
                    largeText: item.CommunityRating == null ? null : $"⭐{item.CommunityRating.Value.ToString("0.0", CultureInfo.InvariantCulture)} / 10",
                    largeUrl: item.RemoteTrailers.FirstOrDefault()?.Url
                );
            }

            Timestamps? timestampsObj = null;
            if (updatePlaying && e.PlaybackPositionTicks.HasValue && item.RunTimeTicks.HasValue)
            {
                long positionMs = (long)TimeSpan.FromTicks(e.PlaybackPositionTicks.Value).TotalMilliseconds;
                long durationMs = (long)TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMilliseconds;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                long startTime = now - positionMs;
                long endTime = startTime + durationMs;
                timestampsObj = new Timestamps(startTime, endTime);
            }

            var act = new Activity("Jellyfin")
            {
                type = ActivityType.Watching,
                status_display_type = StatusDisplayType.Details,
                state = state,
                state_url = stateUrl,
                details = updatePaused ? "[⏸] " + details : details,
                details_url = detailsUrl,
                timestamps = timestampsObj,
                assets = assetsObj,
            };

            //Plugin.Instance!.Logger.LogInformation(JsonConvert.SerializeObject(session.FullNowPlayingItem.LatestItemsIndexContainer.Name, Formatting.Indented, new JsonSerializerSettings
            //{
            //    NullValueHandling = NullValueHandling.Ignore
            //}));

            info.Discord.UpdatePresence(act);
        }
    }

    public async void OnPlaybackStop(object? sender, PlaybackProgressEventArgs e)
    {
        if (_infoMap.TryGetValue(e.Session.UserId, out var info))
        {
            if (info.SessionId != e.PlaySessionId)
            {
                return; // ignore different sessions
            }
            // they have to be deleted at the end
            if (info.ImageMessageId.HasValue) await Plugin.Instance!.DiscordBotHandler.RemoveMessage(info.ImageMessageId.Value);
            await Task.Delay(300);
            info.Discord.Dispose();
            _infoMap.Remove(e.Session.UserId);
        }
    }

}