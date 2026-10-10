using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace Burmalk.Services
{
    internal sealed class NowPlayingTrack
    {
        public string Source;
        public string Title;
        public string Artist;
        public bool Playing;
    }

    /// <summary>Reads only the media metadata applications publish to Windows.</summary>
    internal static class NowPlayingService
    {
        public static async Task<NowPlayingTrack> ReadAsync()
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                NowPlayingTrack paused = null;
                foreach (var session in manager.GetSessions())
                {
                    try
                    {
                        string source = SourceName(session.SourceAppUserModelId ?? "");
                        if (source == null) continue;
                        var playback = session.GetPlaybackInfo();
                        bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        var metadata = await session.TryGetMediaPropertiesAsync();
                        if (metadata == null || string.IsNullOrWhiteSpace(metadata.Title)) continue;
                        var track = new NowPlayingTrack
                        {
                            Source = source,
                            Title = metadata.Title.Trim(),
                            Artist = string.IsNullOrWhiteSpace(metadata.Artist) ? "Исполнитель не указан" : metadata.Artist.Trim(),
                            Playing = playing
                        };
                        if (playing) return track;
                        paused ??= track;
                    }
                    catch (Exception ex) { ErrorLog.Write("Music session: " + ex.Message); }
                }
                return paused;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Now playing: " + ex.Message);
                return null;
            }
        }

        internal static string SourceName(string sourceId)
        {
            // Chrome's installed SoundCloud app advertises a Chrome._crx_... ID,
            // not a name containing "SoundCloud". These are its observed app IDs.
            if (sourceId.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (sourceId.Contains("soundcloud", StringComparison.OrdinalIgnoreCase) ||
                sourceId.Contains("eikjhbkpemcmdeeeamdpkgabmk", StringComparison.OrdinalIgnoreCase) ||
                sourceId.Contains("eikjhbkpemdappjfcmdeeeamdpkgabmk", StringComparison.OrdinalIgnoreCase)) return "SoundCloud";
            return null;
        }
    }
}
