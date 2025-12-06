using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Response from /sync/all-items endpoint.
    /// </summary>
    public class SyncAllItemsResponse
    {
        /// <summary>
        /// Gets or sets list of TV shows.
        /// </summary>
        [JsonPropertyName("shows")]
        public List<SyncShowItem>? Shows { get; set; }

        /// <summary>
        /// Gets or sets list of anime.
        /// </summary>
        [JsonPropertyName("anime")]
        public List<SyncShowItem>? Anime { get; set; }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public List<SyncMovieItem>? Movies { get; set; }
    }
}
