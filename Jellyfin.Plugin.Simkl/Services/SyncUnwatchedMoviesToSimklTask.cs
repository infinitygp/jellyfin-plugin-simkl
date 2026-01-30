using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Scheduled task that syncs unwatched movies to SIMKL as "plan to watch".
    /// </summary>
    public class SyncUnwatchedMoviesToSimklTask : IScheduledTask
    {
        private const int BatchSize = 100;
        private const string CompletedStatus = "completed";
        private const string PlanToWatchStatus = "plantowatch";
        private readonly ILogger<SyncUnwatchedMoviesToSimklTask> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncUnwatchedMoviesToSimklTask"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SyncUnwatchedMoviesToSimklTask}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        public SyncUnwatchedMoviesToSimklTask(
            ILogger<SyncUnwatchedMoviesToSimklTask> logger,
            SimklApi simklApi,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IUserDataManager userDataManager)
        {
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _userManager = userManager;
            _userDataManager = userDataManager;
        }

        /// <inheritdoc />
        public string Name => "Sync unwatched movies to SIMKL";

        /// <inheritdoc />
        public string Key => "SimklSyncUnwatchedMovies";

        /// <inheritdoc />
        public string Description => "Syncs unwatched movies from Jellyfin library to SIMKL as 'plan to watch'";

        /// <inheritdoc />
        public string Category => "SIMKL";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting SIMKL unwatched movies sync task");

            var configuration = SimklPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                _logger.LogWarning("Plugin configuration is null, cannot sync");
                return;
            }

            var userConfigs = configuration.UserConfigs
                .Where(c => !string.IsNullOrEmpty(c.UserToken))
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

                await SyncUserUnwatchedMoviesAsync(userConfig, cancellationToken);
                processedUsers++;
                progress.Report((double)processedUsers / userConfigs.Count * 100);
            }

            _logger.LogInformation("Completed SIMKL unwatched movies sync task");
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Non-periodic task - must be triggered manually
            return Array.Empty<TaskTriggerInfo>();
        }

        private async Task SyncUserUnwatchedMoviesAsync(UserConfig userConfig, CancellationToken cancellationToken)
        {
            var user = _userManager.GetUserById(userConfig.Id);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found", userConfig.Id);
                return;
            }

            _logger.LogInformation("Syncing unwatched movies to SIMKL for user {UserName}", user.Username);

            try
            {
                // Get all movies from SIMKL to check their status
                var simklItems = await _simklApi.GetAllItemsAsync(userConfig.UserToken, "movies");
                var simklMovieStatuses = BuildSimklMovieStatusMap(simklItems?.Movies);

                // Get unwatched movies from Jellyfin
                var unwatchedMovies = GetUnwatchedMovies(user);

                _logger.LogInformation(
                    "Found {MovieCount} unwatched movies for user {UserName}",
                    unwatchedMovies.Count,
                    user.Username);

                // Filter movies that need to be added to SIMKL as "plan to watch"
                var moviesToSync = unwatchedMovies
                    .Where(m => ShouldAddToPlanToWatch(m, simklMovieStatuses))
                    .ToList();

                _logger.LogInformation(
                    "{MovieCount} movies need to be synced as 'plan to watch' for user {UserName}",
                    moviesToSync.Count,
                    user.Username);

                await SyncMoviesAsPlanToWatchAsync(moviesToSync, userConfig.UserToken, cancellationToken);

                _logger.LogInformation("Completed unwatched movies sync for user {UserName}", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing unwatched movies for user {UserName}", user.Username);
            }
        }

        private static Dictionary<string, string> BuildSimklMovieStatusMap(List<SyncMovieItem>? movies)
        {
            var statusMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (movies == null)
            {
                return statusMap;
            }

            foreach (var movie in movies)
            {
                if (movie.Movie?.Ids == null || string.IsNullOrEmpty(movie.Status))
                {
                    continue;
                }

                // Store status by various IDs for lookup
                if (!string.IsNullOrEmpty(movie.Movie.Ids.Imdb))
                {
                    statusMap[movie.Movie.Ids.Imdb] = movie.Status;
                }

                if (!string.IsNullOrEmpty(movie.Movie.Ids.Tmdb))
                {
                    statusMap["tmdb:" + movie.Movie.Ids.Tmdb] = movie.Status;
                }
            }

            return statusMap;
        }

        private static bool ShouldAddToPlanToWatch(Movie movie, Dictionary<string, string> simklStatuses)
        {
            // Check if movie already has "completed" or "plantowatch" status in SIMKL
            var imdb = movie.GetProviderId(MetadataProvider.Imdb);
            var tmdb = movie.GetProviderId(MetadataProvider.Tmdb);

            if (!string.IsNullOrEmpty(imdb) && simklStatuses.TryGetValue(imdb, out var imdbStatus))
            {
                if (string.Equals(imdbStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(imdbStatus, PlanToWatchStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(tmdb) && simklStatuses.TryGetValue("tmdb:" + tmdb, out var tmdbStatus))
            {
                if (string.Equals(tmdbStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tmdbStatus, PlanToWatchStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private List<Movie> GetUnwatchedMovies(JellyfinUser user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            };

            return _libraryManager.GetItemList(query)
                .OfType<Movie>()
                .Where(m => HasRequiredIds(m) && !IsWatched(m, user))
                .ToList();
        }

        private bool IsWatched(BaseItem item, JellyfinUser user)
        {
            var userData = _userDataManager.GetUserData(user, item);
            return userData?.Played == true;
        }

        private static bool HasRequiredIds(BaseItem item)
        {
            return !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Imdb)) ||
                   !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Tmdb));
        }

        private async Task SyncMoviesAsPlanToWatchAsync(List<Movie> movies, string userToken, CancellationToken cancellationToken)
        {
            if (movies.Count == 0)
            {
                return;
            }

            var batches = movies
                .Select((movie, index) => new { movie, index })
                .GroupBy(x => x.index / BatchSize)
                .Select(g => g.Select(x => x.movie).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var collection = new SimklCollection
                {
                    Movies = batch.Select(CreateSimklCollectionMovie).ToList()
                };

                var response = await _simklApi.AddToCollectionAsync(collection, userToken);
                _logger.LogDebug("Synced batch of {Count} movies as 'plan to watch', response: {@Response}", batch.Count, response);
            }
        }

        private static SimklCollectionMovie CreateSimklCollectionMovie(Movie movie)
        {
            var ids = new SimklCollectionIds();
            var imdb = movie.GetProviderId(MetadataProvider.Imdb);
            var tmdb = movie.GetProviderId(MetadataProvider.Tmdb);

            if (!string.IsNullOrEmpty(imdb))
            {
                ids.Imdb = imdb;
            }

            if (!string.IsNullOrEmpty(tmdb))
            {
                ids.Tmdb = tmdb;
            }

            return new SimklCollectionMovie
            {
                Title = movie.Name,
                Year = movie.ProductionYear,
                Ids = ids,
                To = PlanToWatchStatus
            };
        }
    }
}
