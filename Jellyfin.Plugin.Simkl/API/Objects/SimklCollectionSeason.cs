using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Season item in collection show.
    /// </summary>
    public class SimklCollectionSeason
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("number")]
        public int Number { get; set; }

        /// <summary>
        /// Gets or sets the episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<SimklCollectionEpisode>? Episodes { get; set; }
    }
}
