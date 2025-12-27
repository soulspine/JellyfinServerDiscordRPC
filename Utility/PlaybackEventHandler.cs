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
            bool firstPresenceSetHappened = false,
            bool isPaused = false,
            string imagePath = ""
        )
        {
            SessionId = sessionId;
            PreviousTick = previousTick;
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
    /// Used to determine whether a skip / rewind happened. Exact way of checking this is: if abs(currentTick - previousTick) > _seekRange, we can register is as seek / rewind.
    /// Recommended value is double the value of tps because sometimes updates report a bit less / more.
    /// This basically allows the best ratio of making sure we are correct while allowing for small deviations like that.
    /// </summary>
    private const double _seekRange = TimeSpan.TicksPerSecond * 3;

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

        // check for skips / rewinds
        if (e.PlaybackPositionTicks.HasValue && !info.IsPaused)
        {
            if (Math.Abs(e.PlaybackPositionTicks.Value - info.PreviousTick) > _seekRange)
            {
                Plugin.Instance!.Logger.LogInformation($"SEEK {Math.Abs(e.PlaybackPositionTicks.Value - info.PreviousTick)} {_seekRange}");
                updatePlaying = true;
            }
            info.PreviousTick = e.PlaybackPositionTicks.Value;
        }

        if (updatePaused || updatePlaying)
        {
            string? details = null;
            string? detailsUrl = null;
            string? state = null;
            string? stateUrl = null;

            if (item.IndexNumber != null) // series
            {
                var parent = item.GetParent();
                details = parent.OriginalTitle;
                detailsUrl = $"https://imdb.com/title/{parent.GetProviderId(MetadataProvider.Imdb)}";
                int seasonNumber = item.ParentIndexNumber!.Value;
                int episodeNumber = item.IndexNumber!.Value;

                state = $"S{seasonNumber:D2}E{episodeNumber:D2}";
                stateUrl = $"https://imdb.com/title/{item.GetProviderId(MetadataProvider.Imdb)}";
            }
            else // movie
            {
                // TODO add a config option that would let user choose
                // between localized / original titles
                details = item.OriginalTitle;
                detailsUrl = $"https://imdb.com/title/{item.GetProviderId(MetadataProvider.Imdb)}";
            }

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
                details = updatePaused ? "⏸️ " + details : details,
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
            await Task.Delay(300);
            if (info.ImageMessageId.HasValue) await Plugin.Instance!.DiscordBotHandler.RemoveMessage(info.ImageMessageId.Value);
            info.Discord.Dispose();
            _infoMap.Remove(e.Session.UserId);
        }
    }

}