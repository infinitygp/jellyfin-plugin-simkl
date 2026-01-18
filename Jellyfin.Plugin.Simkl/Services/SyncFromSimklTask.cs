using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Scheduled task that imports watch history from SIMKL to Jellyfin.
    /// </summary>
    public class SyncFromSimklTask : IScheduledTask
    {
        private readonly ILogger<SyncFromSimklTask> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncFromSimklTask"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SyncFromSimklTask}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        public SyncFromSimklTask(
            ILogger<SyncFromSimklTask> logger,
            SimklApi simklApi,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            IUserManager userManager)
        {
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _userManager = userManager;
        }

        /// <inheritdoc />
        public string Name => "Import history from SIMKL";

        /// <inheritdoc />
        public string Key => "SimklSyncFromSimkl";

        /// <inheritdoc />
        public string Description => "Imports watch history from SIMKL and marks matching items as watched in Jellyfin";

        /// <inheritdoc />
        public string Category => "SIMKL";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting SIMKL history import task");

            var configuration = SimklPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                _logger.LogWarning("Plugin configuration is null, cannot sync");
                return;
            }

            var userConfigs = configuration.UserConfigs
                .Where(c => !string.IsNullOrEmpty(c.UserToken) && c.SyncHistoryFromSimkl)
                .ToList();

            if (userConfigs.Count == 0)
            {
                _logger.LogInformation("No users configured for SIMKL sync");
                return;
            }

            var processedUsers = 0;
            foreach (var userConfig in userConfigs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await SyncUserHistoryAsync(userConfig, cancellationToken);
                processedUsers++;
                progress.Report((double)processedUsers / userConfigs.Count * 100);
            }

            _logger.LogInformation("Completed SIMKL history import task");
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(12).Ticks
                }
            };
        }

        private async Task SyncUserHistoryAsync(UserConfig userConfig, CancellationToken cancellationToken)
        {
            var user = _userManager.GetUserById(userConfig.Id);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found", userConfig.Id);
                return;
            }

            _logger.LogInformation("Syncing SIMKL history for user {UserName}", user.Username);

            try
            {
                var activities = await _simklApi.GetActivitiesAsync(userConfig.UserToken);
                if (activities == null)
                {
                    _logger.LogWarning("Failed to get activities for user {UserName}", user.Username);
                    return;
                }

                var shouldSync = ShouldSyncUser(userConfig, activities);
                if (!shouldSync)
                {
                    _logger.LogInformation("No changes detected for user {UserName}, skipping sync", user.Username);
                    return;
                }

                var allItems = await _simklApi.GetAllItemsAsync(userConfig.UserToken);

                if (allItems == null)
                {
                    _logger.LogWarning("Failed to get all items for user {UserName}", user.Username);
                    return;
                }

                await ProcessMoviesAsync(allItems.Movies, user, cancellationToken);
                await ProcessShowsAsync(allItems.Shows, user, cancellationToken);
                await ProcessShowsAsync(allItems.Anime, user, cancellationToken);

                userConfig.LastSyncActivities = activities.All;
                SimklPlugin.Instance?.SaveConfiguration();

                _logger.LogInformation("Completed history sync for user {UserName}", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing history for user {UserName}", user.Username);
            }
        }

        private static bool ShouldSyncUser(UserConfig userConfig, SyncActivitiesResponse activities)
        {
            if (!userConfig.LastSyncActivities.HasValue)
            {
                return true;
            }

            return activities.All > userConfig.LastSyncActivities.Value;
        }

        private async Task ProcessMoviesAsync(
            List<SyncMovieItem>? movies,
            User user,
            CancellationToken cancellationToken)
        {
            if (movies == null || movies.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Processing {Count} movies from SIMKL", movies.Count);

            foreach (var simklMovie in movies)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Only process items with "completed" status to avoid marking unwatched items
                if (!string.Equals(simklMovie.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var movie = FindMovieByIds(simklMovie.Movie?.Ids, user);
                if (movie == null)
                {
                    continue;
                }

                await MarkAsWatchedAsync(movie, user, simklMovie.LastWatchedAt);
            }
        }

        private async Task ProcessShowsAsync(
            List<SyncShowItem>? shows,
            User user,
            CancellationToken cancellationToken)
        {
            if (shows == null || shows.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Processing {Count} shows from SIMKL", shows.Count);

            foreach (var simklShow in shows)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Only process items with "completed" status to avoid marking unwatched items
                if (!string.Equals(simklShow.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (simklShow.Seasons == null)
                {
                    continue;
                }

                foreach (var season in simklShow.Seasons)
                {
                    if (season.Episodes == null)
                    {
                        continue;
                    }

                    foreach (var episode in season.Episodes)
                    {
                        var jellyfinEpisode = FindEpisodeByIds(
                            simklShow.Show?.Ids,
                            season.Number,
                            episode.Number,
                            user);

                        if (jellyfinEpisode == null)
                        {
                            continue;
                        }

                        await MarkAsWatchedAsync(jellyfinEpisode, user, episode.WatchedAt);
                    }
                }
            }
        }

        private Movie? FindMovieByIds(SyncIds? ids, User user)
        {
            if (ids == null)
            {
                return null;
            }

            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            };

            var movies = _libraryManager.GetItemList(query).OfType<Movie>();

            foreach (var movie in movies)
            {
                if (MatchesIds(movie, ids))
                {
                    return movie;
                }
            }

            return null;
        }

        private Episode? FindEpisodeByIds(
            SyncIds? showIds,
            int? seasonNumber,
            int? episodeNumber,
            User user)
        {
            if (showIds == null || !seasonNumber.HasValue || !episodeNumber.HasValue)
            {
                return null;
            }

            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                IsVirtualItem = false,
                Recursive = true
            };

            var series = _libraryManager.GetItemList(query).OfType<Series>();

            foreach (var show in series)
            {
                if (!MatchesIds(show, showIds))
                {
                    continue;
                }

                var episodeQuery = new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    ParentIndexNumber = seasonNumber.Value,
                    IndexNumber = episodeNumber.Value,
                    AncestorIds = new[] { show.Id },
                    IsVirtualItem = false,
                    Recursive = true
                };

                var episode = _libraryManager.GetItemList(episodeQuery).OfType<Episode>().FirstOrDefault();
                if (episode != null)
                {
                    return episode;
                }
            }

            return null;
        }

        private static bool MatchesIds(BaseItem item, SyncIds ids)
        {
            if (!string.IsNullOrEmpty(ids.Imdb) &&
                string.Equals(item.GetProviderId(MetadataProvider.Imdb), ids.Imdb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(ids.Tmdb) &&
                string.Equals(item.GetProviderId(MetadataProvider.Tmdb), ids.Tmdb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(ids.Tvdb) &&
                string.Equals(item.GetProviderId(MetadataProvider.Tvdb), ids.Tvdb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private Task MarkAsWatchedAsync(BaseItem item, User user, DateTime? watchedAt)
        {
            var userData = _userDataManager.GetUserData(user, item);
            if (userData == null || userData.Played)
            {
                return Task.CompletedTask;
            }

            userData.Played = true;
            userData.PlayCount = Math.Max(userData.PlayCount, 1);
            if (watchedAt.HasValue)
            {
                userData.LastPlayedDate = watchedAt.Value;
            }

            _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.Import, CancellationToken.None);
            _logger.LogDebug("Marked {ItemName} as watched for {UserName}", item.Name, user.Username);

            return Task.CompletedTask;
        }
    }
}
