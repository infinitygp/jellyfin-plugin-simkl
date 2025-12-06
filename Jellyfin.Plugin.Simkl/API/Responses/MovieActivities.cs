using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Activity timestamps for movies.
    /// </summary>
    public class MovieActivities
    {
        /// <summary>
        /// Gets or sets the latest update time within the category.
        /// </summary>
        [JsonPropertyName("all")]
        public DateTime? All { get; set; }

        /// <summary>
        /// Gets or sets the last rated at timestamp.
        /// </summary>
        [JsonPropertyName("rated_at")]
        public DateTime? RatedAt { get; set; }

        /// <summary>
        /// Gets or sets the plan to watch timestamp.
        /// </summary>
        [JsonPropertyName("plantowatch")]
        public DateTime? PlanToWatch { get; set; }

        /// <summary>
        /// Gets or sets the completed timestamp.
        /// </summary>
        [JsonPropertyName("completed")]
        public DateTime? Completed { get; set; }

        /// <summary>
        /// Gets or sets the dropped timestamp.
        /// </summary>
        [JsonPropertyName("dropped")]
        public DateTime? Dropped { get; set; }

        /// <summary>
        /// Gets or sets the removed from list timestamp.
        /// </summary>
        [JsonPropertyName("removed_from_list")]
        public DateTime? RemovedFromList { get; set; }
    }
}
