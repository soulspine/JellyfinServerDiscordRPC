# Jellyfin Server-side Discord RPC
Fully server-side implementation of Discord Rich Presence for Jellyfin media server.

# ⚠️ Important Disclaimer
This plugin utilizes Discord tokens as a way to connect to the [Gateway](https://discord.com/developers/docs/events/gateway) as the user and update the rich presence. While it does not perform any other actions, Discord's ToS prohibits ANY usage of tokens as a way of user automation, also known as self-botting. By using this plugin, you acknowledge the risk associated with using it. I am not responsible for any damage caused to any discord account that may occur. From my experience, the risk is very low but it is not zero.

# Why would you consider using this plugin?
Main reason why you would consider using it is that it is fully server-sided. There is no need to install anything on the clients (excluding a browser or the Jellyfin app). \
This means that no matter what device the user will be watching from, their Discord RPC will be properly updated. \
As mentioned above, there is the risk of using user tokens. If you want a fully ToS-compliant alternative, I recommend checking out the client-sided [jellyfin-rpc](https://github.com/Radiicall/jellyfin-rpc).

# Installation
For installation instructions, see [Installation and Configuration Guide](README/InstallConfigGuide/text.md)

# Showcase
For now the plugin supports only movies and shows. Other media types are planned to be implemented in the future.
### Watching a movie
![Movie Showcase - Joker Playing Small](README/Showcase/jokerPlayingSmall.png) \
![Movie Showcase - Joker Playing Big](README/Showcase/jokerPlayingBig.png)

### Having a movie paused
![Movie Showcase - Joker Paused Small](README/Showcase/jokerPausedSmall.png) \
![Movie Showcase - Joker Paused Big](README/Showcase/jokerPausedBig.png)

### Watching a show
![Show Showcase - Your Lie in April Playing Small](README/Showcase/aprilPlayingSmall.png) \
![Show Showcase - Your Lie in April Playing Big](README/Showcase/aprilPlayingBig.png)

### Having a show paused
![Show Showcase - Your Lie in April Paused Small](README/Showcase/aprilPausedSmall.png) \
![Show Showcase - Your Lie in April Paused Big](README/Showcase/aprilPausedBig.png)


## Links and Ratings
### Hovering over the image will show the media's community rating if available
![IMDb Rating Showcase](README/Showcase/imdbRatingShowcase.png)

### Clicking on the media name will open its IMDb page if available
![IMDb Link Showcase 1](README/Showcase/fullLinkShowcase1.png) \
![IMDb Link Showcase 2](README/Showcase/fullLinkShowcase2.png)

### When watching a show, clicking on the current season and episode will open its IMDb page if available
![IMDb Link Showcase - Episode 1](README/Showcase/episodeLinkShowcase1.png)
![IMDb Link Showcase - Episode 2](README/Showcase/episodeLinkShowcase2.png)

## Metadata Languages
The plugin does not fetch metadata from any external source. It is using what Jellyfin is providing. This is why, the config checkbox `Use Localized Names` is for. When enabled, the plugin will use **localized names** (displayed in the user interface) - for me, they are in Polish, as shown below. When disabled, the plugin will use their **original names**, that does not mean they will be in English. \
![Localized Names Checkbox](README/Showcase/localizedNamesCheckbox.png)

## Localized name
![Localized Names Enabled Showcase](README/Showcase/localizedNamesOn.png)

## Original name
![Localized Names Disabled Showcase](README/Showcase/localizedNamesOff.png)
