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
        /// Title of watched media, fetched from Jellyfin so it will be localized based on the server's settings. It's used as a fallback when IBMDb fetch fails.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Season name used for validating whether stream event is the one we want to track.
        /// </summary>
        public string Season { get; set; }
        /// <summary>
        /// Episode name used for validating whether stream event is the one we want to track.
        /// </summary>
        public string Episode { get; set; }
        /// <summary>
        /// Container for IMDb scraped metadata. It's null only before fetcher job has finished. 
        /// </summary>
        public IMDbScraper.MovieMetadata? Metadata { get; set; }
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
        /// <summary>
        /// It's here only because fetching this data in the constructor synchronously would freeze it for 2-3 seconds. It's basically a background runner.
        /// </summary>
        /// <param name="id"></param>
        private async Task runIMDbFetch(string? id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Metadata = await Plugin.Instance!.IMDbScraper.GetImdbMetadata(id);
        }
        /// <summary>
        /// Constructor for the PlaybackInfoContainer class.
        /// </summary>
        /// <param name="previousTick">Leaving this as default will most likely result in mislabel it as seek / rewind on the next update. Default: 0</param>
        /// <param name="title">Default: ""</param>
        /// <param name="seasonName">Default: ""</param>
        /// <param name="episodeName">Default: ""</param>
        /// <param name="imdbId">If specified and valid, Metadata field will have info about this media. Default: null</param>
        /// <param name="discordToken">Discord token of the that will have their presence updated</param>
        /// <param name="firstPresenceSetHappened">Whether first presence update request was already sent. Best practice is to leave this as false in the constructor and only update RPC after imdb check was finished (Metadata is not null). Default: false</param>
        /// <param name="isPaused">Used to keep track of player state. Default: false</param>
        public PlaybackInfoContainer(
            string discordToken,
            string sessionId,
            long previousTick = 0,
            string title = "",
            string seasonName = "",
            string episodeName = "",
            string? imdbId = null,
            bool firstPresenceSetHappened = false,
            bool isPaused = false
        )
        {
            SessionId = sessionId;
            PreviousTick = previousTick;
            Title = title;
            Season = seasonName;
            Episode = episodeName;
            Metadata = null;
            _ = runIMDbFetch(imdbId);
            FirstPresenceSetHappened = firstPresenceSetHappened;
            IsPaused = isPaused;
            Discord = new UserHandler(discordToken);
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
        var item = e.MediaInfo;

        // TODO ADD CHECKING IF USER HAS A TOKEN SET IN THE CONFIG PAGE

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
                item.Name,
                item.SeasonName,
                item.EpisodeTitle,
                item.GetProviderId(MetadataProvider.Imdb)
            );
            info = _infoMap[session.UserId];
        }

        // check for title/season/episode change - playback different from one we have stored
        if (e.PlaySessionId != info.SessionId)
        {
            return; // ignore different sessions
        }

        bool updatePaused = false;
        bool updatePlaying = false;

        // update presence only 1 time after initial stream start
        if (!info.FirstPresenceSetHappened && info.Metadata != null && info.Discord.IsReady())
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
            var title = info.Metadata?.Title != null ? info.Metadata.Title : info.Title;
            var link = info.Metadata?.Id != null ? $"https://www.imdb.com/title/{info.Metadata.Id}" : null;

            // for some reason idk why sometimes it just doesnt get the image, then this comes in handy
            if (info.Metadata?.Id != null && info.Metadata?.PosterUrl == null)
            {
                info.Metadata = await Plugin.Instance!.IMDbScraper.GetImdbMetadata(info.Metadata!.Id);
            }

            Assets? assets = null;
            if (info.Metadata?.PosterUrl != null)
            {
                assets = new Assets(
                    largeImage: "mp:" + info.Metadata.PosterUrl + "?width=250&height=250",
                    largeUrl: link
                );
            }

            Plugin.Instance!.Logger.LogInformation($"{e.PlaySessionId} {updatePaused} {updatePlaying}");

            if (updatePlaying)
            {
                Timestamps? timestamps = null;
                if (e.PlaybackPositionTicks.HasValue && item.RunTimeTicks.HasValue)
                {
                    long positionMs = (long)TimeSpan.FromTicks(e.PlaybackPositionTicks.Value).TotalMilliseconds;
                    long durationMs = (long)TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMilliseconds;

                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    long startTime = now - positionMs;
                    long endTime = startTime + durationMs;
                    timestamps = new Timestamps(startTime, endTime);
                }

                info.Discord.UpdatePresence(
                    activityName: title,
                    detailsUrl: link,
                    timestampsObj: timestamps,
                    assetsObj: assets
                );
            }
            else if (updatePaused)
            {
                info.Discord.UpdatePresence(
                    activityName: title,
                    detailsUrl: link,
                    assetsObj: assets
                );
            }
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
            info.Discord.ClearPresence();
            await Task.Delay(300);
            info.Discord.Dispose();
            _infoMap.Remove(e.Session.UserId);
        }
    }

}