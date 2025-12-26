namespace DiscordRPC.Utility.Discord.GatewayDTO;

public class Timestamps
{
    public readonly long? start;
    public readonly long? end;
    public Timestamps(long? startTimestamp = null, long? endTimestamp = null)
    {
        start = startTimestamp;
        end = endTimestamp;
    }
}

public class Assets
{
    public readonly string? large_image;
    public readonly string? large_text;
    public readonly string? large_url;
    public readonly string? small_image;
    public readonly string? small_text;
    public readonly string? small_url;
    public readonly string? invite_cover_image;
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