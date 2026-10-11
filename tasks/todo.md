# Jellyfin 12.2 compatibility review

- [x] Inspect project targets, registration, and playback hooks.
- [x] Verify Jellyfin 12.2 release and published migration guidance.
- [x] Compare relevant server APIs and playback pipeline with plugin assumptions.
- [x] Build existing code and assess a temporary 12.2 retarget without changing plugin source.
- [x] Report concrete defects and verification limits.

## Review results

Current plugin release is built for Jellyfin 10.11.6 and net9.0; manifest ABI is 10.11.6.0. Jellyfin's official 12.0 upgrade notes explicitly require updated builds for 10.11 plugins. Jellyfin 12.2 Controller targets net10.0 and publishes version 12.2.0.

Verification:
- Original solution restored and built successfully: zero warnings/errors.
- Temporary source copy under /private/tmp/audio-selector-review12 changed only target framework to net10.0 and Controller/Model references to 12.2.0. Build succeeded: zero warnings/errors. This establishes source-level compatibility, not existing binary compatibility or server load behavior.
- Temporary executable probes under /private/tmp/audio-selector-probes used real Jellyfin 12.2 model types and plugin code. Reproduced:
  - AC3 5.1 rejected by a codec-specific AAC stereo restriction.
  - An AAC-only direct-play profile accepts EAC3.
  - Preferred English AAC 128 kbps loses to Spanish AAC 256 kbps.
  - Fallback chooses a track already rejected by capability checks.
  - PlaybackInfo filter changes default audio index from 1 to 2 but leaves a transcoding URL with AudioStreamIndex=1.

Priority findings:
1. Blocking release compatibility: project and manifest require an updated 12.x build/package. Merely changing manifest ABI is insufficient.
2. High: PlaybackInfoResultFilter.cs:85 changes only the default after server stream planning. Jellyfin 12.2 MediaInfoController calls SetDeviceSpecificData before returning; MediaInfoHelper generates SupportsDirectPlay/SupportsDirectStream/TranscodingUrl and the audio default together. Select before planning, or recompute the plan for the chosen track.
3. High: PlaybackInfoResultFilter.cs:54 reads only DeviceId query / standalone X-Emby-Device-Id. It ignores authenticated device claims and posted DeviceProfile, whereas Jellyfin's controller uses both. Match the request's actual profile and channel/bitrate limits; retain explicit user selection.
4. High: DeviceCapabilityMatcher.cs:146 accepts common codecs despite an explicit profile excluding them. It also treats transcoding output codec declarations as native input support and ignores container matching. Honor applicable direct-play and codec conditions; keep transcodeability distinct.
5. High: DeviceCapabilityMatcher.cs:161 applies channel restrictions across unrelated codecs; GetMaxSupportedChannels repeats this. Match codec and context.
6. Medium: AudioTrackSelectionService.cs:83 fallback bypasses the capability checks. Preserve server default or validate a deliberate transcoding path when no compatible input exists.
7. Medium: AudioTrackSelectionService.cs:194 gives language only 0.25 points and spatial format only 1 point despite documented 5%/10% weights. Normalize or enforce language preference before quality ranking.
8. Medium: PlaybackInterceptorService.cs:112 loops every source rather than the source actually playing, compares against file default rather than actual playback audio index (:156), and does not await SendGeneralCommand (:171). Restrict to the playing source/current index and observe command completion/errors. Client support for this command also requires validation.

Additional observations: PlaybackEntryPoint and AudioTrackMediaSourceProvider are not registered by the active registrator; EnableFalsePositiveDetection is unused; GetPages returns no settings pages; no test project is present.

Limitations: No live Jellyfin 12.2 server/client playback was exercised. Startup, direct play, HLS/remux/transcoding, user track overrides, multiple versions, and actual client command support remain integration checks for a corrected release. Plugin source and manifest were not modified. Build outputs/restore metadata are ignored; only this requested durable review record was added. Pre-existing untracked tvshow.nfo was untouched.

Sources:
- https://github.com/jellyfin/jellyfin/releases/tag/v12.2
- https://jellyfin.org/posts/jellyfin-release-12.0/
- https://github.com/jellyfin/jellyfin/blob/v12.2/MediaBrowser.Controller/MediaBrowser.Controller.csproj
- https://github.com/jellyfin/jellyfin/blob/v12.2/Jellyfin.Api/Controllers/MediaInfoController.cs
- https://github.com/jellyfin/jellyfin/blob/v12.2/Jellyfin.Api/Helpers/MediaInfoHelper.cs

# Jellyfin 12.2 update

## Design

Replace the result filter and runtime switch backup with an MVC action filter scoped to Jellyfin's POST PlaybackInfo action. After model binding, resolve the authenticated user and their accessible item, load playback media sources, select only the requested/default item source, and pass audioStreamIndex/mediaSourceId to the controller before its stream builder runs. Preserve explicit query/body audio selection, skip live/opening sources and unknown capabilities, and leave failures to the normal Jellyfin pipeline. Read the unpublished API DTO through a small bounded adapter; do not bundle or depend on the server's API assembly. Do not mutate shared media sources or rewrite generated URLs.

Capability matching will honor applicable direct-play container/media-type/codec profiles and per-codec conditions, exclude transcoding output codecs as evidence of native support, apply request channel/bitrate limits, and have no unchecked fallback. Prefer the configured language among compatible tracks before quality scoring.

Tradeoff: this intentionally covers POST PlaybackInfo for ordinary file playback. GET discovery and live stream opening retain Jellyfin defaults. It removes late track changes and duplicate alternate-source commands. A server API shape change causes selection to be skipped instead of corrupting playback.

- [x] Implement pre-planning playback hook and remove obsolete hooks.
- [x] Fix capability matching, fallback, language/spatial scoring.
- [x] Retarget net10.0 / Jellyfin 12.2.0 and bump release version.
- [x] Add deterministic regression runner covering real 12.2 model types and MVC contexts.
- [x] Build, run regression checks, and review changes.
- [x] Package local release and update manifest with verified checksum; document installation and scope.

Verification: original bug probes, explicit query/body track overrides, posted/authenticated profiles, selected alternate source, missing/unsupported profiles, codec/container/channel/bitrate constraints, action failure/cancellation and disabled plugin, and preservation of the controller's generated result. Release build and ZIP checksum validation. Live server playback remains a manual integration check unless an existing server is available.

## Update review results

Implemented release 3.0.0.0 with .NET 10 and Jellyfin 12.2.0 references. Pre-planning action filter preserves explicit and remembered manual choices, uses posted/authenticated profiles, validates its unpublished DTO adapter, and scopes automatic selection to ordinary video playback. Shared sources and generated playback results are untouched. Removed dead providers, late response filter, runtime switch services, and unused configuration.

Verified Release solution build with `dotnet build Jellyfin.Plugin.AudioTrackSelector.sln -c Release --no-restore --disable-build-servers -m:1`: zero warnings/errors. The parallel solution build stalled in this sandbox and was cancelled; single-worker build completed successfully. `dotnet run --project tests/AudioTrackSelector.Regression -c Release --no-build`: all 40 checks passed. Code and authorization/security re-review found no remaining concrete defects. `git diff --check` passed.

Created local dist/jellyfin-plugin-audioTrackselector_3.0.0.0.zip; verified archive integrity, exact Release DLL contents, version/ABI, and manifest checksum. Release URL is prepared but has not been published. Root README and packaging script document reproducible packaging, installation, and supported scope.

No live Jellyfin startup/client smoke test was run. Automated contexts use genuine Jellyfin interfaces/model types with stand-ins for the unpublished Jellyfin.Api DTO/controller. Validate direct playback, remux/transcoding and client track overrides on a real server before broad deployment. Existing tvshow.nfo remains untouched.
