using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace DiscordRPC.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string DiscordBotToken { get; set; } = string.Empty;
        public string DiscordImagesChannelId { get; set; } = string.Empty;
        public bool LocalizedNames { get; set; } = false;
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
