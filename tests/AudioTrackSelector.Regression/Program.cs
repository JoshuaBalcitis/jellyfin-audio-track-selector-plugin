using System.Reflection;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.AudioTrackSelector.Configuration;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AudioTrackSelector;
using Jellyfin.Plugin.AudioTrackSelector.Services;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Api.Models.MediaInfoDtos;

var matcher = new DeviceCapabilityMatcher(NullLogger<DeviceCapabilityMatcher>.Instance);
var selector = new AudioTrackSelectionService(NullLogger<AudioTrackSelectionService>.Instance, matcher);
var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); count++; }
MediaStream Audio(int index, string codec = "aac", int channels = 2, int bitrate = 128000, string language = "eng") => new() { Index = index, Type = MediaStreamType.Audio, Codec = codec, Channels = channels, BitRate = bitrate, Language = language };
MediaSourceInfo Source(string id, params MediaStream[] audio) => new() { Id = id, Container = "mkv", DefaultAudioStreamIndex = audio[0].Index, MediaStreams = new[] { new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "h264", BitRate = 1000000 } }.Concat(audio).ToArray() };
DeviceProfile Profile(string codecs = "aac,ac3") => new() { DirectPlayProfiles = new[] { new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv", AudioCodec = codecs } } };
var profile = Profile();
profile.CodecProfiles = new[] { new CodecProfile { Type = CodecType.VideoAudio, Codec = "aac", Conditions = new[] { new ProfileCondition { Property = ProfileConditionValue.AudioChannels, Condition = ProfileConditionType.LessThanEqual, Value = "2" } } } };
var source = Source("source", Audio(1), Audio(2, "ac3", 6));
Check(!matcher.CanPlayAudioStream(Audio(2, "eac3", 6), Profile("aac"), source), "AAC profile rejects EAC3");
Check(matcher.CanPlayAudioStream(Audio(2, "ac3", 6), profile, source), "AAC channel limit does not restrict AC3");
Check(!matcher.CanPlayAudioStream(Audio(2, "aac", 6), profile, source), "Matching AAC channel limit enforced");
Check(!matcher.CanPlayAudioStream(Audio(2, "ac3", 6), profile, source, 2), "Request channel limit enforced");
Check(!matcher.CanPlayAudioStream(Audio(2), profile, source, null, 1000000), "Total video plus audio bitrate limit enforced");
Check(selector.SelectOptimalAudioTrack(Source("language", Audio(1, bitrate:128000, language:"eng"), Audio(2, bitrate:256000, language:"spa")), Profile(), "en-US") == 1, "Preferred language outranks bitrate");
Check(selector.SelectOptimalAudioTrack(Source("fallback", Audio(1, channels:6), Audio(2, "eac3",6)), profile, "eng") == null, "Unsupported fallback leaves server selection");
Check(selector.SelectOptimalAudioTrack(source, null, "eng") == null, "Missing capabilities leaves server selection");
Check(!matcher.CanPlayAudioStream(Audio(1), new DeviceProfile { TranscodingProfiles = new[] { new TranscodingProfile { Type = DlnaProfileType.Video, AudioCodec = "aac" } } }, source), "Transcoding outputs do not imply native support");
Check(!matcher.CanPlayAudioStream(Audio(1), new DeviceProfile { DirectPlayProfiles = new[] { new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv" } } }, source), "Undeclared audio codecs leave selection unchanged");

var mixedProfile = new DeviceProfile { DirectPlayProfiles = new[] {
    new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv", VideoCodec = "h264", AudioCodec = "truehd" },
    new DirectPlayProfile { Type = DlnaProfileType.Video, Container = "mkv", VideoCodec = "av1", AudioCodec = "aac" }
} };
var mixedSource = Source("av1", Audio(1), Audio(2, "truehd", 8)); mixedSource.MediaStreams[0].Codec = "av1";
Check(!matcher.CanPlayAudioStream(mixedSource.MediaStreams[2], mixedProfile, mixedSource), "Audio support cannot be borrowed from a different video codec profile");
Check(matcher.CanPlayAudioStream(mixedSource.MediaStreams[1], mixedProfile, mixedSource), "Matching video and audio codec profile permits AAC");
var primaryOnly = Profile();
primaryOnly.CodecProfiles = new[] { new CodecProfile { Type = CodecType.VideoAudio, Codec = "aac", Conditions = new[] { new ProfileCondition { Property = ProfileConditionValue.IsSecondaryAudio, Condition = ProfileConditionType.Equals, Value = "false" } } } };
var secondarySource = Source("secondary", Audio(1), Audio(2));
Check(matcher.CanPlayAudioStream(secondarySource.MediaStreams[1], primaryOnly, secondarySource), "Primary audio satisfies primary-only restriction");
Check(!matcher.CanPlayAudioStream(secondarySource.MediaStreams[2], primaryOnly, secondarySource), "Secondary audio rejected by primary-only restriction");

var wrongContainer = Source("mp4", Audio(1)); wrongContainer.Container = "mp4";
Check(!matcher.CanPlayAudioStream(wrongContainer.MediaStreams[1], Profile(), wrongContainer), "Mismatched source container rejects direct-play profile");
var conditionalProfile = Profile("aac");
conditionalProfile.CodecProfiles = new[] { new CodecProfile {
    Type = CodecType.VideoAudio, Codec = "aac",
    ApplyConditions = new[] { new ProfileCondition { Property = ProfileConditionValue.AudioChannels, Condition = ProfileConditionType.GreaterThanEqual, Value = "6" } },
    Conditions = new[] { new ProfileCondition { Property = ProfileConditionValue.AudioBitrate, Condition = ProfileConditionType.LessThanEqual, Value = "64000" } }
} };
Check(matcher.CanPlayAudioStream(Audio(1, channels:2, bitrate:128000), conditionalProfile, source), "Inactive apply condition does not impose codec restriction");
Check(!matcher.CanPlayAudioStream(Audio(1, channels:6, bitrate:128000), conditionalProfile, source), "Active apply condition imposes codec restriction");
Check(matcher.CanPlayAudioStream(Audio(1, channels:6, bitrate:64000), conditionalProfile, source), "Active apply condition accepts stream satisfying codec restriction");

var userId = Guid.NewGuid(); var itemId = Guid.NewGuid();
var user = new User("tester", "default", "default") { Id = userId };
BaseItem item = new Video { Id = itemId };
var primary = Source(itemId.ToString("N"), Audio(1), Audio(2, "ac3", 6));
primary.TranscodingUrl = "/video/master.m3u8?AudioStreamIndex=1";
var alternate = Source("alternate", Audio(4), Audio(5, "ac3", 6));
var sources = new List<MediaSourceInfo> { primary, alternate };
DeviceProfile? storedProfile = Profile();
var reads = 0; Exception? selectionError = null;
var devices = Proxy.Create<IDeviceManager>((m,a) => m.Name == "GetCapabilities" ? new ClientCapabilities { DeviceProfile = storedProfile } : null);
var users = Proxy.Create<IUserManager>((m,a) => m.Name == "GetUserById" ? user : null);
var library = Proxy.Create<ILibraryManager>((m,a) => m.Name == "GetItemById" ? item : null);
var media = Proxy.Create<IMediaSourceManager>((m,a) => { if (m.Name == "GetPlaybackMediaSources") { reads++; if (selectionError is not null) throw selectionError; return Task.FromResult<IReadOnlyList<MediaSourceInfo>>(sources); } return null; });
var filter = new PlaybackInfoActionFilter(NullLogger<PlaybackInfoActionFilter>.Instance, selector, devices, library, users, media);
ActionExecutingContext Context(PlaybackInfoDto? body = null, string method = "GetPostedPlaybackInfo") {
    var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Jellyfin-UserId", userId.ToString()), new Claim("Jellyfin-DeviceId", "device") }, "test")) };
    var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = typeof(Jellyfin.Api.Controllers.MediaInfoController).GetTypeInfo(), MethodInfo = typeof(Jellyfin.Api.Controllers.MediaInfoController).GetMethod(method)! };
    var action = new ActionContext(http, new RouteData(), descriptor);
    return new ActionExecutingContext(action, new List<IFilterMetadata>(), new Dictionary<string, object?> { ["itemId"] = itemId, ["userId"] = null, ["audioStreamIndex"] = null, ["mediaSourceId"] = null, ["playbackInfoDto"] = body }, new object());
}
async Task<int> Run(ActionExecutingContext context) {
    var calls = 0;
    await filter.OnActionExecutionAsync(context, () => { calls++; return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller) { Result = new ObjectResult(primary) }); });
    return calls;
}
var ctx = Context();
Check(await Run(ctx) == 1 && (int?)ctx.ActionArguments["audioStreamIndex"] == 2 && (string?)ctx.ActionArguments["mediaSourceId"] == primary.Id, "Authenticated capabilities select before planning and call next once");
Check(primary.DefaultAudioStreamIndex == 1 && primary.TranscodingUrl == "/video/master.m3u8?AudioStreamIndex=1", "Selection does not mutate shared source or generated playback result");
ctx = Context(); ctx.ActionArguments["audioStreamIndex"] = 1; var before = reads;
await Run(ctx); Check((int?)ctx.ActionArguments["audioStreamIndex"] == 1 && reads == before, "Explicit query selection preserved");
ctx = Context(new PlaybackInfoDto { AudioStreamIndex = 1 }); before = reads;
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null && reads == before, "Explicit body selection preserved");
ctx = Context(new PlaybackInfoDto { DeviceProfile = Profile("aac") });
await Run(ctx); Check((int?)ctx.ActionArguments["audioStreamIndex"] == 1, "Posted client profile takes precedence over stored capabilities");
ctx = Context(new PlaybackInfoDto { MediaSourceId = "alternate" });
await Run(ctx); Check((int?)ctx.ActionArguments["audioStreamIndex"] == 5 && (string?)ctx.ActionArguments["mediaSourceId"] == "alternate", "Alternate source selection uses that source's indexes");
storedProfile = null; ctx = Context(); await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null, "Missing device profile retains controller request"); storedProfile = Profile();
ctx = Context(); ctx.ActionArguments["userId"] = Guid.NewGuid(); before = reads;
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null && reads == before, "Different user's request left to controller authorization");
ctx = Context(method:"GetPlaybackInfo"); before = reads;
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null && reads == before, "GET playback info bypasses automatic selection");
ctx = Context(); ctx.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); before = reads;
await Run(ctx); Check(reads == before, "Unauthenticated requests bypass selection");
ctx = Context(new PlaybackInfoDto { LiveStreamId = "live" }); before = reads;
await Run(ctx); Check(reads == before, "Live stream continuation preserves track selection");
primary.DefaultAudioIndexSource = AudioIndexSource.User; ctx = Context();
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null, "Remembered manual audio choice preserved"); primary.DefaultAudioIndexSource = AudioIndexSource.None;
item = new MediaBrowser.Controller.Entities.Audio.Audio { Id = itemId }; ctx = Context(); before = reads;
await Run(ctx); Check(reads == before && ctx.ActionArguments["audioStreamIndex"] == null, "Audio-only items retain Jellyfin selection"); item = new Video { Id = itemId };
ctx = Context(); ctx.ModelState.AddModelError("itemId", "invalid"); before = reads;
await Run(ctx); Check(reads == before, "Invalid model state leaves request for controller");
ctx = Context(new PlaybackInfoDto { MaxAudioChannels = 8 }); ctx.ActionArguments["maxAudioChannels"] = 2;
await Run(ctx); Check((int?)ctx.ActionArguments["audioStreamIndex"] == 1, "Query channel limit overrides posted body limit");
ctx = Context(new PlaybackInfoDto { MediaSourceId = "unknown" });
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null && ctx.ActionArguments["mediaSourceId"] == null, "Unknown source preserves original request");
var allowedItem = item; item = null!; ctx = Context(); before = reads;
await Run(ctx); Check(reads == before && ctx.ActionArguments["audioStreamIndex"] == null, "Denied item access preserves controller request"); item = allowedItem;
primary.RequiresOpening = true; ctx = Context();
await Run(ctx); Check(ctx.ActionArguments["audioStreamIndex"] == null, "Source requiring opening retains controller selection"); primary.RequiresOpening = false;
selectionError = new InvalidOperationException("capability lookup failed"); ctx = Context();
Check(await Run(ctx) == 1 && ctx.ActionArguments["audioStreamIndex"] == null, "Selection failure preserves request and executes controller once"); selectionError = null;
using (var cancellation = new CancellationTokenSource()) {
    cancellation.Cancel(); ctx = Context(); ctx.HttpContext.RequestAborted = cancellation.Token;
    selectionError = new OperationCanceledException(cancellation.Token); var called = false;
    try { await filter.OnActionExecutionAsync(ctx, () => { called = true; throw new Exception("Controller must not execute for canceled selection"); }); throw new Exception("Cancellation swallowed"); }
    catch (OperationCanceledException) { Check(!called && ctx.ActionArguments["audioStreamIndex"] == null, "Canceled selection propagates cancellation without calling controller"); }
    selectionError = null;
}
// Configuration is normally loaded by the host; supply its stored configuration directly
// without creating config files or booting a Jellyfin server.
var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
var configurationField = typeof(Plugin).BaseType!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(PluginConfiguration));
configurationField.SetValue(plugin, new PluginConfiguration { Enabled = false });
typeof(Plugin).GetProperty("Instance")!.SetValue(null, plugin);
ctx = Context(); before = reads;
await Run(ctx); Check(reads == before && ctx.ActionArguments["audioStreamIndex"] == null, "Disabled plugin leaves playback selection unchanged");
typeof(Plugin).GetProperty("Instance")!.SetValue(null, null);
var error = new InvalidOperationException("controller failed"); var nextCalls = 0; ctx = Context();
try { await filter.OnActionExecutionAsync(ctx, () => { nextCalls++; throw error; }); throw new Exception("Exception was swallowed"); }
catch (InvalidOperationException ex) { Check(ReferenceEquals(ex, error) && nextCalls == 1, "Controller exceptions propagate unchanged and next executes once"); }
Console.WriteLine($"{count} regression checks passed.");

public class Proxy : DispatchProxy {
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class { var instance = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)instance).Handler = handler; return instance; }
}
namespace Jellyfin.Api.Controllers { public class MediaInfoController { public void GetPostedPlaybackInfo() {} public void GetPlaybackInfo() {} } }
namespace Jellyfin.Api.Models.MediaInfoDtos {
    // Test stand-in for Jellyfin.Api's bound body contract (not published as a NuGet plugin API).
    public class PlaybackInfoDto {
        public int? AudioStreamIndex { get; set; }
        public Guid? UserId { get; set; }
        public string? MediaSourceId { get; set; }
        public string? LiveStreamId { get; set; }
        public DeviceProfile? DeviceProfile { get; set; }
        public int? MaxAudioChannels { get; set; }
        public int? MaxStreamingBitrate { get; set; }
    }
}
