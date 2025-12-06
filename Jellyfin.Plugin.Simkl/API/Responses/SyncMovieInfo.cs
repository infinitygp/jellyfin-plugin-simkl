using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Movie info in sync response.
    /// </summary>
    public class SyncMovieInfo
    {
        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the IDs.
        /// </summary>
        [JsonPropertyName("ids")]
        public SyncIds? Ids { get; set; }
    }
}
