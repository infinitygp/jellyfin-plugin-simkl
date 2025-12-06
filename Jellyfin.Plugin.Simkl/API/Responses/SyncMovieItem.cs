using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Movie item from sync response.
    /// </summary>
    public class SyncMovieItem
    {
        /// <summary>
        /// Gets or sets the last watched at timestamp.
        /// </summary>
        [JsonPropertyName("last_watched_at")]
        public DateTime? LastWatchedAt { get; set; }

        /// <summary>
        /// Gets or sets the user rating.
        /// </summary>
        [JsonPropertyName("user_rating")]
        public int? UserRating { get; set; }

        /// <summary>
        /// Gets or sets the status.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// Gets or sets the movie info.
        /// </summary>
        [JsonPropertyName("movie")]
        public SyncMovieInfo? Movie { get; set; }
    }
}
