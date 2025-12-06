using System;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// User-specific configuration for the Simkl plugin.
    /// </summary>
    public class UserConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserConfig"/> class.
        /// </summary>
        public UserConfig()
        {
            ScrobbleMovies = true;
            ScrobbleShows = true;
            ScrobblePercentage = 70;
            ScrobbleNowWatchingPercentage = 5;
            MinLength = 5;
            UserToken = string.Empty;
            ScrobbleTimeout = 30;

            SyncLibraryToSimkl = false;
            SyncHistoryFromSimkl = false;
            SyncUserDataChanges = true;
            SyncLibraryChanges = true;
            UserDataSyncDelay = 10;
            LibrarySyncDelay = 30;
            LastSyncActivities = null;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to scrobble movies.
        /// </summary>
        public bool ScrobbleMovies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to scrobble TV shows.
        /// </summary>
        public bool ScrobbleShows { get; set; }

        /// <summary>
        /// Gets or sets the percentage of playback required to trigger a scrobble.
        /// </summary>
        public int ScrobblePercentage { get; set; }

        /// <summary>
        /// Gets or sets the percentage of playback required to send now watching status.
        /// </summary>
        public int ScrobbleNowWatchingPercentage { get; set; }

        /// <summary>
        /// Gets or sets the minimum media length in minutes to be considered for scrobbling.
        /// </summary>
        public int MinLength { get; set; }

        /// <summary>
        /// Gets or sets the SIMKL authentication token for this user.
        /// </summary>
        public string UserToken { get; set; }

        /// <summary>
        /// Gets or sets the timeout in seconds for scrobble requests.
        /// </summary>
        public int ScrobbleTimeout { get; set; }

        /// <summary>
        /// Gets or sets the Jellyfin user ID associated with this configuration.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync Jellyfin library to SIMKL.
        /// </summary>
        public bool SyncLibraryToSimkl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to import watch history from SIMKL.
        /// </summary>
        public bool SyncHistoryFromSimkl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync user data changes in real-time.
        /// </summary>
        public bool SyncUserDataChanges { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to sync library changes in real-time.
        /// </summary>
        public bool SyncLibraryChanges { get; set; }

        /// <summary>
        /// Gets or sets the delay in seconds before syncing user data changes.
        /// </summary>
        public int UserDataSyncDelay { get; set; }

        /// <summary>
        /// Gets or sets the delay in seconds before syncing library changes.
        /// </summary>
        public int LibrarySyncDelay { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the last sync activities check.
        /// </summary>
        public DateTime? LastSyncActivities { get; set; }
    }
}