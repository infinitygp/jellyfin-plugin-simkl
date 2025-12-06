using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Episode item in collection season.
    /// </summary>
    public class SimklCollectionEpisode
    {
        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        [JsonPropertyName("number")]
        public int Number { get; set; }
    }
}
