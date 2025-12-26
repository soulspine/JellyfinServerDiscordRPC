using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace DiscordRPC.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string DiscordAppId { get; set; } = string.Empty;
        public string LanguageCode { get; set; } = "en";
        // List of user token entries (UserId + DiscordToken)
        public List<UserToken> UserTokens { get; set; } = new List<UserToken>();
    }

    public class UserToken
    {
        public string Username { get; set; } = string.Empty;
        public string DiscordToken { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Username}:{DiscordToken}";
        }
    }
}