#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl collection for add-to-list endpoint.
    /// </summary>
    public class SimklCollection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklCollection"/> class.
        /// </summary>
        public SimklCollection()
        {
            Movies = new List<SimklCollectionMovie>();
            Shows = new List<SimklCollectionShow>();
        }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public List<SimklCollectionMovie> Movies { get; set; }

        /// <summary>
        /// Gets or sets list of shows.
        /// </summary>
        [JsonPropertyName("shows")]
        public List<SimklCollectionShow> Shows { get; set; }
    }
}
