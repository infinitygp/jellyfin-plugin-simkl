using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Show item for collection.
    /// </summary>
    public class SimklCollectionShow
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
        public SimklCollectionIds? Ids { get; set; }

        /// <summary>
        /// Gets or sets the target list.
        /// </summary>
        [JsonPropertyName("to")]
        public string? To { get; set; }

        /// <summary>
        /// Gets or sets the seasons with episodes.
        /// </summary>
        [JsonPropertyName("seasons")]
        public List<SimklCollectionSeason>? Seasons { get; set; }
    }
}
