using System;
using System.Threading.Tasks;
using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Burmalk.Services
{
    internal sealed class NowPlayingTrack
    {
        public string Source;
        public string Title;
        public string Artist;
        public bool Playing;
        public string ThumbnailPath;
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
                        track.ThumbnailPath = await SaveThumbnailAsync(metadata.Thumbnail);
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

        private static async Task<string> SaveThumbnailAsync(IRandomAccessStreamReference thumbnail)
        {
            if (thumbnail == null) return null;
            try
            {
                using (var stream = await thumbnail.OpenReadAsync())
                {
                    if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        await reader.LoadAsync((uint)stream.Size);
                        var bytes = new byte[(int)stream.Size];
                        reader.ReadBytes(bytes);
                        string folder = Path.Combine(App.Store.RootDir, "music-artwork");
                        Directory.CreateDirectory(folder);
                        string extension = stream.ContentType?.ToLowerInvariant() switch
                        {
                            "image/jpeg" or "image/jpg" => ".jpg", "image/png" => ".png", "image/bmp" => ".bmp", "image/gif" => ".gif", _ => ".img"
                        };
                        string path = Path.Combine(folder, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + extension);
                        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, bytes);
                        return path;
                    }
                }
            }
            catch (Exception ex) { ErrorLog.Write("Music artwork: " + ex.Message); return null; }
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
