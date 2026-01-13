namespace DiscordRPC.Utility.Discord.GatewayDTO;

public class Timestamps
{
    public long? start { get; set; }
    public long? end { get; set; }
    public Timestamps(long? startTimestamp = null, long? endTimestamp = null)
    {
        start = startTimestamp;
        end = endTimestamp;
    }
}

public class Assets
{
    public string? large_image { get; set; }
    public string? large_text { get; set; }
    public string? large_url { get; set; }
    public string? small_image { get; set; }
    public string? small_text { get; set; }
    public string? small_url { get; set; }
    public string? invite_cover_image { get; set; }
    public Assets(
        string? largeImage = null, string? largeText = null, string? largeUrl = null,
        string? smallImage = null, string? smallText = null, string? smallUrl = null,
        string? inviteCoverImage = null
    )
    {
        large_image = largeImage;
        large_text = largeText;
        large_url = largeUrl;
        small_image = smallImage;
        small_text = smallText;
        small_url = smallUrl;
        invite_cover_image = inviteCoverImage;
    }
}

public enum ActivityType
{
    /// <summary>
    /// Playing {name}
    /// </summary>
    Playing = 0,
    /// <summary>
    /// Streaming {details} - works only for https://twitch.tv and https://youtube.com
    /// </summary>
    Streaming = 1,
    /// <summary>
    /// Listening go {name}
    /// </summary>
    Listening = 2,
    /// <summary>
    /// Watching {name}
    /// </summary>
    Watching = 3,
    /// <summary>
    /// {emoji} {state}
    /// </summary>
    Custom = 4,
    /// <summary>
    /// Competing in {name}
    /// </summary>
    Competing = 5,
}

public enum StatusDisplayType
{
    Name = 0,
    State = 1,
    Details = 2,
}

public class Emoji
{
    public string name { get; set; }
    public string? id { get; set; }
    public bool? animated { get; set; }
    public Emoji(string name)
    {
        this.name = name;
    }
}

public class Party
{
    public string? id { get; set; }
    /// <summary>
    /// Has to have only 2 elements: (current_size, max_size)
    /// </summary>
    public int[]? size { get; set; }
}

public class Secrets
{
    public string? join { get; set; }
    public string? spectate { get; set; }
    public string? match { get; set; }
}

public enum ActivityFlags
{
    Instance = 1 << 0,
    Join = 1 << 1,
    Spectate = 1 << 2,
    JoinRequest = 1 << 3,
    Sync = 1 << 4,
    Play = 1 << 5,
    PartyPrivacyFriends = 1 << 6,
    PartyPrivacyVoiceChannel = 1 << 7,
    Embedded = 1 << 8,
}

public class Button
{
    public string label { get; set; }
    public string url { get; set; }
    public Button(string label, string url)
    {
        this.label = label;
        this.url = url;
    }
}

public class Activity
{
    /// <summary>
    /// Activity's name
    /// </summary>
    public string name { get; set; }
    /// <summary>
    /// Dictates the way Activity will be displayed
    /// </summary>
    public ActivityType type { get; set; }
    /// <summary>
    /// Stream URL
    /// </summary>
    public string? url { get; set; }
    /// <summary>
    /// Unix timestamp (in milliseconds) of when the activity was added to the user's session.
    /// </summary>
    public long? created_at { get; set; }
    public Timestamps? timestamps { get; set; }
    public string? application_id { get; set; }
    public StatusDisplayType? status_display_type { get; set; }
    public string? details { get; set; }
    public string? details_url { get; set; }
    public string? state { get; set; }
    public string? state_url { get; set; }
    public Emoji? emoji { get; set; }
    public Party? party { get; set; }
    public Assets? assets { get; set; }
    public Secrets? secrets { get; set; }
    public bool? instance { get; set; }
    public ActivityFlags? flags { get; set; }
    public Button[]? buttons { get; set; }
    public Activity(string name) { this.name = name; }
}
