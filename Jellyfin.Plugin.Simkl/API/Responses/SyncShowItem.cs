using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Show item from sync response.
    /// </summary>
    public class SyncShowItem
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
        /// Gets or sets the last watched episode identifier.
        /// </summary>
        [JsonPropertyName("last_watched")]
        public string? LastWatched { get; set; }

        /// <summary>
        /// Gets or sets the next to watch episode identifier.
        /// </summary>
        [JsonPropertyName("next_to_watch")]
        public string? NextToWatch { get; set; }

        /// <summary>
        /// Gets or sets the watched episodes count.
        /// </summary>
        [JsonPropertyName("watched_episodes_count")]
        public int? WatchedEpisodesCount { get; set; }

        /// <summary>
        /// Gets or sets the total episodes count.
        /// </summary>
        [JsonPropertyName("total_episodes_count")]
        public int? TotalEpisodesCount { get; set; }

        /// <summary>
        /// Gets or sets the show info.
        /// </summary>
        [JsonPropertyName("show")]
        public SyncShowInfo? Show { get; set; }

        /// <summary>
        /// Gets or sets the seasons with watched episodes.
        /// </summary>
        [JsonPropertyName("seasons")]
        public List<SyncSeasonInfo>? Seasons { get; set; }
    }
}
