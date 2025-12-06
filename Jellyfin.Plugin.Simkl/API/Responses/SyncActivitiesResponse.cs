using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Response from /sync/activities endpoint.
    /// </summary>
    public class SyncActivitiesResponse
    {
        /// <summary>
        /// Gets or sets the latest update time across all categories.
        /// </summary>
        [JsonPropertyName("all")]
        public DateTime? All { get; set; }

        /// <summary>
        /// Gets or sets TV shows activities.
        /// </summary>
        [JsonPropertyName("tv_shows")]
        public MediaActivities? TvShows { get; set; }

        /// <summary>
        /// Gets or sets anime activities.
        /// </summary>
        [JsonPropertyName("anime")]
        public MediaActivities? Anime { get; set; }

        /// <summary>
        /// Gets or sets movies activities.
        /// </summary>
        [JsonPropertyName("movies")]
        public MovieActivities? Movies { get; set; }
    }
}
