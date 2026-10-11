# Jellyfin Audio Track Selector

Version 3.0.0.0 targets **Jellyfin Server 12.2** and **.NET 10**. Earlier builds target older Jellyfin releases and cannot be reused for 12.x.

The plugin chooses audio before Jellyfin generates its playback plan, keeping the selected track, direct-play decision, and transcoding URL consistent. A track explicitly requested by a client takes precedence. Automatic selection uses the request's posted device profile, or the authenticated device's registered profile.

Only tracks declared compatible with the source's media type, container, codec conditions, and request channel/streaming bitrate limits are considered. Among supported tracks, the configured language is preferred first, then quality (codec 40%, channels 30%, bitrate 20%, spatial metadata 10%). ISO 639 two/three letter language aliases are recognized. Spatial metadata increases ranking; it does not prove Atmos or DTS:X passthrough support.

If no track can be verified as compatible, Jellyfin keeps its own selection and transcoding behavior. Transcoding output profiles are not treated as native codec support. The plugin does not send playback-start track-switch commands or modify completed playback responses.

## Scope and configuration

Automatic selection handles authenticated **POST `/Items/{itemId}/PlaybackInfo`** requests for ordinary video playback with already resolved media sources with multiple audio tracks. Explicit query/body and remembered manual track selections, audio-only items, GET discovery, live streams, sources requiring opening, invalid requests, and unknown device profiles retain Jellyfin behavior. Clients with no explicit native audio codec declarations also retain Jellyfin behavior. The plugin follows the requested media source; when none is specified it uses the queried item's source rather than switching to another version.

Defaults: `Enabled=true`, `PreferredAudioLanguage=eng`. Existing XML configuration remains usable. There is currently no dashboard configuration page; edit these fields in Jellyfin's plugin XML configuration while the server is stopped. The unused `EnableFalsePositiveDetection` setting has been removed.

## Build and verify

Requires the .NET 10 SDK:

```sh
dotnet build Jellyfin.Plugin.AudioTrackSelector.sln -c Release
dotnet run --project tests/AudioTrackSelector.Regression -c Release
python3 scripts/package.py --timestamp 2026-10-11T17:00:00Z
```

The regression executable uses real Jellyfin 12.2 model types and MVC filter contexts. It exits with a nonzero status on failure; no external test framework or running Jellyfin server is needed.

The packaging script creates `dist/jellyfin-plugin-audioTrackselector_3.0.0.0.zip` and updates the corresponding entry in `manifest.json` with the ZIP's MD5 checksum. Re-run packaging after changing a Release binary. The catalog requires MD5; it is an integrity field, not a security signature.

## Install

Stop Jellyfin and replace the old plugin with the DLL from the new ZIP in a versioned plugin directory, e.g. `<Jellyfin data directory>/plugins/Audio Track Selector_3.0.0.0/`. Remove the older version's directory so only the new plugin is present. Ensure Jellyfin can read and write the new directory, then restart and check the plugin dashboard and logs.

The 3.0.0.0 ZIP is prepared locally. Its manifest URL is the intended GitHub release location; catalog installation will work only after that exact ZIP is uploaded to release `v3.0.0.0` and the updated manifest is hosted. Building and packaging do not publish anything.

Before deploying broadly, verify playback on your clients: native playback, remux/HLS/transcoding, manual track changes, preferred language, and alternate versions. Automated checks do not replace a live server/client playback test.
