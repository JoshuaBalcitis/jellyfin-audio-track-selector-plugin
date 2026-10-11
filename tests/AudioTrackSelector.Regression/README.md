# Regression checks

Run with .NET 10:

```sh
dotnet run --project tests/AudioTrackSelector.Regression
```

This executable exits with an error on a failed assertion. It exercises capability
matching, language ranking, and the MVC action filter against Jellyfin 12.2 interfaces
using in-memory dependency proxies. No external server or test framework is required.

The controller and playback body stand-ins have Jellyfin.Api's public type names and
property contract because Jellyfin.Api is not published as a plugin dependency.
These checks verify requests entering playback planning; they do not simulate a full
Jellyfin server, native player, remuxer, or transcoder. Real playback still requires
client/server smoke testing.
