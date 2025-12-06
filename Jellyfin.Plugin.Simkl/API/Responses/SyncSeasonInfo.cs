using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Season info in sync response.
    /// </summary>
    public class SyncSeasonInfo
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        /// <summary>
        /// Gets or sets the episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<SyncEpisodeInfo>? Episodes { get; set; }
    }
}
