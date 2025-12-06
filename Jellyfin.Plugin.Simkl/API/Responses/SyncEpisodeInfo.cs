using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Episode info in sync response.
    /// </summary>
    public class SyncEpisodeInfo
    {
        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        /// <summary>
        /// Gets or sets the watched at timestamp.
        /// </summary>
        [JsonPropertyName("watched_at")]
        public DateTime? WatchedAt { get; set; }
    }
}
