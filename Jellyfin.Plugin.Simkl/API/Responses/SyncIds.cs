using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// IDs in sync response.
    /// </summary>
    public class SyncIds
    {
        /// <summary>
        /// Gets or sets the Simkl ID.
        /// </summary>
        [JsonPropertyName("simkl")]
        public int? Simkl { get; set; }

        /// <summary>
        /// Gets or sets the IMDB ID.
        /// </summary>
        [JsonPropertyName("imdb")]
        public string? Imdb { get; set; }

        /// <summary>
        /// Gets or sets the TMDB ID.
        /// </summary>
        [JsonPropertyName("tmdb")]
        public string? Tmdb { get; set; }

        /// <summary>
        /// Gets or sets the TVDB ID.
        /// </summary>
        [JsonPropertyName("tvdb")]
        public string? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the MAL ID.
        /// </summary>
        [JsonPropertyName("mal")]
        public string? Mal { get; set; }

        /// <summary>
        /// Gets or sets the AniDB ID.
        /// </summary>
        [JsonPropertyName("anidb")]
        public string? Anidb { get; set; }
    }
}
