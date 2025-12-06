using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Scheduled task that exports Jellyfin library to SIMKL collection.
    /// </summary>
    public class SyncLibraryTask : IScheduledTask
    {
        private const int BatchSize = 100;
        private readonly ILogger<SyncLibraryTask> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncLibraryTask"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SyncLibraryTask}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        public SyncLibraryTask(
            ILogger<SyncLibraryTask> logger,
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
        public string Name => "Export library to SIMKL";

        /// <inheritdoc />
        public string Key => "SimklSyncLibrary";

        /// <inheritdoc />
        public string Description => "Exports watched items from Jellyfin library to SIMKL as watched history";

        /// <inheritdoc />
        public string Category => "SIMKL";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting SIMKL library export task");

            var configuration = SimklPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                _logger.LogWarning("Plugin configuration is null, cannot sync");
                return;
            }

            var userConfigs = configuration.UserConfigs
                .Where(c => !string.IsNullOrEmpty(c.UserToken) && c.SyncLibraryToSimkl)
                .ToList();

            if (userConfigs.Count == 0)
            {
                _logger.LogInformation("No users configured for SIMKL library sync");
                return;
            }

            var processedUsers = 0;
            foreach (var userConfig in userConfigs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await SyncUserLibraryAsync(userConfig, cancellationToken);
                processedUsers++;
                progress.Report((double)processedUsers / userConfigs.Count * 100);
            }

            _logger.LogInformation("Completed SIMKL library export task");
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(24).Ticks
                }
            };
        }

        private async Task SyncUserLibraryAsync(UserConfig userConfig, CancellationToken cancellationToken)
        {
            var user = _userManager.GetUserById(userConfig.Id);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found", userConfig.Id);
                return;
            }

            _logger.LogInformation("Syncing library to SIMKL for user {UserName}", user.Username);

            try
            {
                var watchedMovies = GetWatchedMovies(user);
                var watchedEpisodes = GetWatchedEpisodes(user);

                _logger.LogInformation(
                    "Found {MovieCount} watched movies and {EpisodeCount} watched episodes for user {UserName}",
                    watchedMovies.Count,
                    watchedEpisodes.Count,
                    user.Username);

                await SyncMoviesAsync(watchedMovies, userConfig.UserToken, cancellationToken);
                await SyncEpisodesAsync(watchedEpisodes, userConfig.UserToken, cancellationToken);

                _logger.LogInformation("Completed library sync for user {UserName}", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing library for user {UserName}", user.Username);
            }
        }

        private List<Movie> GetWatchedMovies(JellyfinUser user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            };

            return _libraryManager.GetItemList(query)
                .OfType<Movie>()
                .Where(m => HasRequiredIds(m) && IsWatched(m, user))
                .ToList();
        }

        private List<Episode> GetWatchedEpisodes(JellyfinUser user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsVirtualItem = false,
                Recursive = true
            };

            return _libraryManager.GetItemList(query)
                .OfType<Episode>()
                .Where(e => HasRequiredIds(e) && IsWatched(e, user))
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
                   !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Tmdb)) ||
                   !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Tvdb));
        }

        private async Task SyncMoviesAsync(List<Movie> movies, string userToken, CancellationToken cancellationToken)
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

                var history = new SimklHistory
                {
                    Movies = batch.Select(CreateSimklMovie).ToList()
                };

                var response = await _simklApi.SyncHistoryToSimklAsync(history, userToken);
                _logger.LogDebug("Synced batch of {Count} movies, response: {@Response}", batch.Count, response);
            }
        }

        private async Task SyncEpisodesAsync(List<Episode> episodes, string userToken, CancellationToken cancellationToken)
        {
            if (episodes.Count == 0)
            {
                return;
            }

            var showGroups = episodes
                .Where(e => e.Series != null)
                .GroupBy(e => e.Series!.Id)
                .ToList();

            foreach (var showGroup in showGroups)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var firstEpisode = showGroup.First();
                var series = firstEpisode.Series;
                if (series == null)
                {
                    continue;
                }

                var history = new SimklHistory
                {
                    Shows = new List<SimklShow>
                    {
                        CreateSimklShow(series, showGroup.ToList())
                    }
                };

                var response = await _simklApi.SyncHistoryToSimklAsync(history, userToken);
                _logger.LogDebug("Synced show {ShowName} with {Count} episodes, response: {@Response}", series.Name, showGroup.Count(), response);
            }
        }

        private static SimklMovie CreateSimklMovie(Movie movie)
        {
            var providerIds = new Dictionary<string, string>();
            var imdb = movie.GetProviderId(MetadataProvider.Imdb);
            var tmdb = movie.GetProviderId(MetadataProvider.Tmdb);

            if (!string.IsNullOrEmpty(imdb))
            {
                providerIds["Imdb"] = imdb;
            }

            if (!string.IsNullOrEmpty(tmdb))
            {
                providerIds["Tmdb"] = tmdb;
            }

            var dto = new BaseItemDto
            {
                Name = movie.Name,
                ProductionYear = movie.ProductionYear,
                ProviderIds = providerIds
            };

            return new SimklMovie(dto);
        }

        private static SimklShow CreateSimklShow(Series series, List<Episode> episodes)
        {
            var providerIds = new Dictionary<string, string>();
            var imdb = series.GetProviderId(MetadataProvider.Imdb);
            var tmdb = series.GetProviderId(MetadataProvider.Tmdb);
            var tvdb = series.GetProviderId(MetadataProvider.Tvdb);
            var mal = series.GetProviderId("MyAnimeList");
            var anidb = series.GetProviderId("AniDB");

            if (!string.IsNullOrEmpty(imdb))
            {
                providerIds["Imdb"] = imdb;
            }

            if (!string.IsNullOrEmpty(tmdb))
            {
                providerIds["Tmdb"] = tmdb;
            }

            if (!string.IsNullOrEmpty(tvdb))
            {
                providerIds["Tvdb"] = tvdb;
            }

            if (!string.IsNullOrEmpty(mal))
            {
                providerIds["MyAnimeList"] = mal;
            }

            if (!string.IsNullOrEmpty(anidb))
            {
                providerIds["AniDB"] = anidb;
            }

            var seasons = CreateSeasons(episodes);
            var firstEpisode = episodes.FirstOrDefault(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue);

            var dto = new BaseItemDto
            {
                SeriesName = series.Name,
                ProductionYear = series.ProductionYear,
                ProviderIds = providerIds,
                ParentIndexNumber = firstEpisode?.ParentIndexNumber,
                IndexNumber = firstEpisode?.IndexNumber
            };

            var show = new SimklShow(dto);

            if (seasons != null && seasons.Count > 0)
            {
                show.Seasons = seasons;
            }

            return show;
        }

        private static List<API.Objects.Season>? CreateSeasons(List<Episode> episodes)
        {
            var seasonGroups = episodes
                .Where(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue)
                .GroupBy(e => e.ParentIndexNumber!.Value)
                .ToList();

            if (seasonGroups.Count == 0)
            {
                return null;
            }

            return seasonGroups.Select(g => new API.Objects.Season
            {
                Number = g.Key,
                Episodes = g.Select(e => new ShowEpisode
                {
                    Number = e.IndexNumber!.Value
                }).ToList()
            }).ToList();
        }
    }
}
