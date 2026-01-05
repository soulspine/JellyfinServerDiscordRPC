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
using System.Collections.Concurrent;
using Polly.Caching;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace DiscordRPC.Utility;

public class PlaybackEventHandler : IDisposable
{
    public PlaybackEventHandler()
    {
        _cleanupTask = Task.Run(async () =>
        {
            while (!_cleanupCts.Token.IsCancellationRequested)
            {
                try
                {
                    cleanupDeadSessions();
                    await Task.Delay(5000, _cleanupCts.Token);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Plugin.Instance!.Logger.LogError(ex, "Cleanup loop error");
                }
            }
        });
    }

    public void Dispose()
    {
        _cleanupCts.Cancel();
        try
        {
            _cleanupTask?.Wait();
        }
        catch { }
    }

    private const int _seekTolerance = 3; // in seconds, that means if abs of time discrepency is higher than this value, it is considered as seek
    private const int PlaybackTimeout = 15;
    public static string imdbLink(string id)
    {
        return $"https://imdb.com/title/{id}";
    }

    private class UserInfoContainer
    {
        /// <summary>
        /// Dedicated object handling the Discord Gateway connection. Lets PlaybackEventHandler update rich presences.
        /// </summary>
        public readonly UserHandler Discord;
        public readonly ConcurrentDictionary<string, PlaybackInfoContainer> Sessions;
        public UserInfoContainer(string discordToken)
        {
            Discord = new UserHandler(discordToken);
            Sessions = new();
        }

        /// <summary>
        /// Creates an array of Activity objects based on contents of Sessions dictionary
        /// </summary>
        public void updatePresence()
        {
            List<Activity> acts = new();
            int i = 0; // used to add i amount of 0width charactersto the name so they are unique and discord allows them
            var sessionsSnapshot = Sessions.Values.ToArray();
            foreach (var playbackInfo in sessionsSnapshot)
            {
                string? details = null;
                string? detailsUrl = null;
                string? state = null;
                string? stateUrl = null;

                BaseItem playbackItem = playbackInfo.PlaybackItem;
                BaseItem mainItem = playbackItem;

                if (playbackItem.IndexNumber.HasValue && playbackItem.ParentIndexNumber.HasValue) // episode
                {
                    BaseItem? parent = mainItem.GetParent();
                    while (parent != null && !parent.IsTopParent)
                    {
                        mainItem = parent;
                        parent = mainItem.GetParent();
                    }

                    state = $"S{playbackItem.ParentIndexNumber.Value:D2}E{playbackItem.IndexNumber.Value:D2}";
                    var episodeId = playbackItem.GetProviderId(MetadataProvider.Imdb);
                    if (!string.IsNullOrEmpty(episodeId)) stateUrl = imdbLink(episodeId);
                }

                // series / movie title
                details = mainItem.Name;

                // we check if there is an original title available and apply it if config has localized names turned off
                if (!string.IsNullOrEmpty(mainItem.OriginalTitle) && !Plugin.Instance!.Configuration.LocalizedNames)
                {
                    details = mainItem.OriginalTitle;
                }

                var mainId = mainItem.GetProviderId(MetadataProvider.Imdb);
                if (!string.IsNullOrEmpty(mainId)) detailsUrl = imdbLink(mainId);

                Assets? assetsObj = null;
                if (playbackInfo.ImageLink != null)
                {
                    assetsObj = new Assets(
                        largeImage: playbackInfo.ImageLink,
                        largeText: playbackItem.CommunityRating == null ? null : $"⭐{playbackItem.CommunityRating.Value.ToString("0.0", CultureInfo.InvariantCulture)} / 10"
                    );
                }

                Timestamps? timestampsObj = null;
                if (!playbackInfo.IsPaused && playbackItem.RunTimeTicks.HasValue)
                {
                    long positionMs = (long)TimeSpan.FromTicks(playbackInfo.PreviousTick).TotalMilliseconds;
                    long durationMs = (long)TimeSpan.FromTicks(playbackItem.RunTimeTicks.Value).TotalMilliseconds;

                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    long startTime = now - positionMs;
                    long endTime = startTime + durationMs;
                    timestampsObj = new Timestamps(startTime, endTime);
                }

                acts.Add(new Activity("Jellyfin" + new string('\u200B', i++))
                {
                    type = ActivityType.Watching,
                    status_display_type = StatusDisplayType.Details,
                    state = state,
                    state_url = stateUrl,
                    details = playbackInfo.IsPaused ? "[⏸] " + details : details,
                    details_url = detailsUrl,
                    timestamps = timestampsObj,
                    assets = assetsObj,
                });
            }

            Discord.UpdatePresence(acts.ToArray());
        }
    }

    /// <summary>
    /// Class representing info and fields needed to properly validate and track stream events.
    /// \\TODO: Make this handle multiple streams since discord gateway allows to specify >1 activity per session/
    /// </summary>
    private class PlaybackInfoContainer
    {
        public BaseItem PlaybackItem { get; set; }
        /// <summary>
        /// Gets updated on every playback event. Used to detect skips and rewinds.
        /// </summary>
        public long PreviousTick { get; set; }
        public long PreviousTimestamp { get; set; }
        /// <summary>
        /// Used to only allow for one Discord Rich Presence update when the stream is started.
        /// It exists because setting presence outright in the first event happenened before IMDb metadata was collected.
        /// </summary>
        public bool FirstPresenceSetHappened { get; set; }
        /// <summary>
        /// Whether the player is paused, used to track changes in playback state.
        /// </summary>
        public bool IsPaused { get; set; }
        public bool FetchingImage { get; set; }
        public string? ImageLink { get; set; }
        public ulong? ImageMessageId { get; set; }
        private async Task tryFetchImage(string imagePath)
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
            BaseItem playbackItem,
            long previousTick = 0,
            long previousTimestamp = 0,
            bool firstPresenceSetHappened = false,
            bool isPaused = false,
            string imagePath = ""
        )
        {
            PlaybackItem = playbackItem;
            PreviousTick = previousTick;
            PreviousTimestamp = previousTimestamp;
            FirstPresenceSetHappened = firstPresenceSetHappened;
            IsPaused = isPaused;
            _ = tryFetchImage(imagePath);
        }
    }

    /// <summary>
    /// Map of user IDs to their playback info containers. By design, there can only be one active
    /// playback session per user at any given time. It is respected by registering show's Title to
    /// ignore multiple sessions and only track the first one that was started.
    /// Freeing it up will result in the ability to track a new session for that user.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, UserInfoContainer> _userMap = new();
    /// <summary>
    /// This method is called when playback is updated (every second and on pause/resume).
    /// It is responsible for updating playback info (and discord RPC) by detecting skips, rewinds and pauses
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private CancellationTokenSource _cleanupCts = new();
    private Task? _cleanupTask;

    private void stopSession(Guid userId, string playSessionId, string reason)
    {
        if (!_userMap.TryGetValue(userId, out var userInfo))
            return;

        if (!userInfo.Sessions.TryRemove(playSessionId, out var playbackInfo))
            return;

        Plugin.Instance!.Logger.LogInformation(
            $"Stopping session {playSessionId} ({reason})");

        if (playbackInfo.ImageMessageId.HasValue)
        {
            _ = Plugin.Instance!.DiscordBotHandler
                .RemoveMessage(playbackInfo.ImageMessageId.Value);
        }

        if (userInfo.Sessions.IsEmpty)
        {
            userInfo.Discord.Dispose();
            _userMap.TryRemove(userId, out _);
        }
        else
        {
            userInfo.updatePresence();
        }
    }


    private void cleanupDeadSessions()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var userPair in _userMap)
        {
            var userInfo = userPair.Value;

            foreach (var sessionPair in userInfo.Sessions)
            {
                var playbackInfo = sessionPair.Value;

                if (now - playbackInfo.PreviousTimestamp > PlaybackTimeout * TimeSpan.MillisecondsPerSecond)
                {
                    stopSession(userPair.Key, sessionPair.Key, "Timeout");
                }
            }
        }
    }


    public async Task OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        var session = e.Session;
        var item = session.FullNowPlayingItem;

        // only video for now
        if (item.MediaType != MediaType.Video) return;

        string? discordToken = Plugin.Instance!.Configuration.UserTokens.FirstOrDefault(x => x.Username == session.UserName)?.DiscordToken;

        // return if this user does not have a token in the config
        if (string.IsNullOrEmpty(discordToken)) return;

        UserInfoContainer userInfo;

        if (!_userMap.TryGetValue(session.UserId, out userInfo!))
        {
            // initialize a user map entry if this user was not there before
            userInfo = _userMap[session.UserId] = new(discordToken);
        }

        PlaybackInfoContainer playbackInfo;
        if (!userInfo.Sessions.TryGetValue(e.PlaySessionId, out playbackInfo!))
        {
            // the same thing
            // initialize a session playback entry if it was not there yet
            playbackInfo = userInfo.Sessions[e.PlaySessionId] = new(
                playbackItem: item,
                previousTick: e.PlaybackPositionTicks ?? 0,
                previousTimestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                imagePath: item.GetImagePath(ImageType.Primary)
            );
        }

        // wait until image is fetched
        if (playbackInfo.FetchingImage) return;

        bool updatePresence = false;

        // update presence only 1 time after initial stream start
        if (!playbackInfo.FirstPresenceSetHappened && userInfo.Discord.IsReady())
        {
            updatePresence = true;
            playbackInfo.FirstPresenceSetHappened = true;
        }

        //check for pause / unpause
        if ((e.IsPaused && !playbackInfo.IsPaused) || (!e.IsPaused && playbackInfo.IsPaused)) updatePresence = true;

        if (e.PlaybackPositionTicks.HasValue && !playbackInfo.IsPaused)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            long playbackElapsedMs = (e.PlaybackPositionTicks.Value - playbackInfo.PreviousTick) / TimeSpan.TicksPerMillisecond;

            long realElapsedMs = now - playbackInfo.PreviousTimestamp;

            long drift = Math.Abs(playbackElapsedMs - realElapsedMs);

            if (drift > _seekTolerance * 1000)
            {
                updatePresence = true;
            }
        }

        // update the state
        playbackInfo.IsPaused = e.IsPaused;
        playbackInfo.PreviousTick = e.PlaybackPositionTicks ?? 0;
        playbackInfo.PreviousTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        playbackInfo.PlaybackItem = item;

        if (updatePresence)
        {
            userInfo.updatePresence();
        }
    }

    public Task OnPlaybackStop(object? sender, PlaybackProgressEventArgs e)
    {
        stopSession(
            e.Session.UserId,
            e.PlaySessionId,
            "PlaybackStopped"
        );

        return Task.CompletedTask;
    }

}