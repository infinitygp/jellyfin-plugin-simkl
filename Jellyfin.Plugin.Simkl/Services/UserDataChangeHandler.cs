using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Handles real-time user data changes and syncs them to SIMKL.
    /// </summary>
    public class UserDataChangeHandler : IHostedService, IDisposable
    {
        private readonly ILogger<UserDataChangeHandler> _logger;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly SimklApi _simklApi;
        private readonly ConcurrentDictionary<Guid, ConcurrentQueue<UserDataChangeItem>> _pendingChanges;
        private readonly ConcurrentDictionary<Guid, Timer> _userTimers;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDataChangeHandler"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{UserDataChangeHandler}"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        public UserDataChangeHandler(
            ILogger<UserDataChangeHandler> logger,
            IUserDataManager userDataManager,
            ILibraryManager libraryManager,
            SimklApi simklApi)
        {
            _logger = logger;
            _userDataManager = userDataManager;
            _libraryManager = libraryManager;
            _simklApi = simklApi;
            _pendingChanges = new ConcurrentDictionary<Guid, ConcurrentQueue<UserDataChangeItem>>();
            _userTimers = new ConcurrentDictionary<Guid, Timer>();
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _logger.LogInformation("UserDataChangeHandler started");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;

            foreach (var timer in _userTimers.Values)
            {
                timer.Dispose();
            }

            _userTimers.Clear();
            _logger.LogInformation("UserDataChangeHandler stopped");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes managed resources.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (var timer in _userTimers.Values)
                {
                    timer.Dispose();
                }

                _userTimers.Clear();
            }

            _disposed = true;
        }

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            // Filter out Import and UpdateUserData reasons to prevent syncing during library scans
            // Only sync when changes are from actual playback (PlaybackProgress, PlaybackFinished)
            if (e.SaveReason == UserDataSaveReason.Import || e.SaveReason == UserDataSaveReason.UpdateUserData)
            {
                return;
            }

            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(e.UserId);
            if (userConfig == null ||
                string.IsNullOrEmpty(userConfig.UserToken) ||
                !userConfig.SyncUserDataChanges)
            {
                return;
            }

            var item = e.Item;
            if (item == null || !IsValidItemType(item) || !HasRequiredIds(item))
            {
                return;
            }

            var changeItem = new UserDataChangeItem
            {
                Item = item,
                IsPlayed = e.UserData.Played,
                PlayedDate = e.UserData.LastPlayedDate
            };

            var queue = _pendingChanges.GetOrAdd(e.UserId, _ => new ConcurrentQueue<UserDataChangeItem>());
            queue.Enqueue(changeItem);

            ResetTimer(e.UserId, userConfig);
        }

        private void ResetTimer(Guid userId, UserConfig userConfig)
        {
            var delay = TimeSpan.FromSeconds(userConfig.UserDataSyncDelay);

            if (_userTimers.TryGetValue(userId, out var existingTimer))
            {
                existingTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                var timer = new Timer(
                    _ => ProcessPendingChangesAsync(userId, userConfig.UserToken).ConfigureAwait(false),
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);
                _userTimers[userId] = timer;
            }
        }

        private async Task ProcessPendingChangesAsync(Guid userId, string userToken)
        {
            if (!_pendingChanges.TryGetValue(userId, out var queue))
            {
                return;
            }

            var changes = new List<UserDataChangeItem>();
            while (queue.TryDequeue(out var change))
            {
                changes.Add(change);
            }

            if (changes.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Processing {Count} user data changes for user {UserId}", changes.Count, userId);

            var toAdd = changes.Where(c => c.IsPlayed).ToList();
            var toRemove = changes.Where(c => !c.IsPlayed).ToList();

            try
            {
                if (toAdd.Count > 0)
                {
                    await SyncWatchedItemsAsync(toAdd, userToken);
                }

                if (toRemove.Count > 0)
                {
                    await SyncUnwatchedItemsAsync(toRemove, userToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing user data changes for user {UserId}", userId);
            }
        }

        private async Task SyncWatchedItemsAsync(List<UserDataChangeItem> items, string userToken)
        {
            var history = new SimklHistory();

            foreach (var change in items)
            {
                var item = change.Item;
                if (item == null)
                {
                    continue;
                }

                if (item is Movie movie)
                {
                    history.Movies.Add(CreateSimklMovie(movie));
                }
                else if (item is Episode episode && episode.Series != null)
                {
                    AddEpisodeToHistory(history, episode);
                }
            }

            if (history.Movies.Count > 0 || history.Shows.Count > 0)
            {
                var response = await _simklApi.SyncHistoryToSimklAsync(history, userToken);
                _logger.LogDebug("Synced watched items, response: {@Response}", response);
            }
        }

        private async Task SyncUnwatchedItemsAsync(List<UserDataChangeItem> items, string userToken)
        {
            var history = new SimklHistory();

            foreach (var change in items)
            {
                var item = change.Item;
                if (item == null)
                {
                    continue;
                }

                if (item is Movie movie)
                {
                    history.Movies.Add(CreateSimklMovie(movie));
                }
                else if (item is Episode episode && episode.Series != null)
                {
                    AddEpisodeToHistory(history, episode);
                }
            }

            if (history.Movies.Count > 0 || history.Shows.Count > 0)
            {
                var response = await _simklApi.RemoveFromHistoryAsync(history, userToken);
                _logger.LogDebug("Removed unwatched items, response: {@Response}", response);
            }
        }

        private static bool IsValidItemType(BaseItem item)
        {
            return item is Movie or Episode;
        }

        private static bool HasRequiredIds(BaseItem item)
        {
            var itemToCheck = item is Episode episode ? episode.Series ?? item : item;
            return !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Imdb)) ||
                   !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Tmdb)) ||
                   !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Tvdb));
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

        private static void AddEpisodeToHistory(SimklHistory history, Episode episode)
        {
            var series = episode.Series;
            if (series == null)
            {
                return;
            }

            if (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
            {
                return;
            }

            var imdb = series.GetProviderId(MetadataProvider.Imdb);
            var tmdb = series.GetProviderId(MetadataProvider.Tmdb);
            var tvdb = series.GetProviderId(MetadataProvider.Tvdb);

            var existingShow = history.Shows.FirstOrDefault(s =>
                (s.Ids != null) &&
                ((!string.IsNullOrEmpty(imdb) && s.Ids.Imdb == imdb) ||
                 (!string.IsNullOrEmpty(tmdb) && s.Ids.Tmdb?.ToString() == tmdb) ||
                 (!string.IsNullOrEmpty(tvdb) && s.Ids.Tvdb?.ToString() == tvdb)));

            if (existingShow == null)
            {
                var providerIds = new Dictionary<string, string>();
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

                var dto = new BaseItemDto
                {
                    SeriesName = series.Name,
                    ProductionYear = series.ProductionYear,
                    ProviderIds = providerIds,
                    ParentIndexNumber = episode.ParentIndexNumber,
                    IndexNumber = episode.IndexNumber
                };

                existingShow = new SimklShow(dto);
                history.Shows.Add(existingShow);
            }
            else
            {
                var seasonNumber = episode.ParentIndexNumber.Value;
                var existingSeason = existingShow.Seasons.FirstOrDefault(s => s.Number == seasonNumber);

                if (existingSeason == null)
                {
                    var seasonsList = existingShow.Seasons.ToList();
                    seasonsList.Add(new API.Objects.Season
                    {
                        Number = seasonNumber,
                        Episodes = new List<ShowEpisode> { new ShowEpisode { Number = episode.IndexNumber.Value } }
                    });
                    existingShow.Seasons = seasonsList;
                }
                else
                {
                    var episodesList = existingSeason.Episodes.ToList();
                    if (!episodesList.Any(e => e.Number == episode.IndexNumber.Value))
                    {
                        episodesList.Add(new ShowEpisode { Number = episode.IndexNumber.Value });
                        existingSeason.Episodes = episodesList;
                    }
                }
            }
        }

        private sealed class UserDataChangeItem
        {
            public BaseItem? Item { get; set; }

            public bool IsPlayed { get; set; }

            public DateTime? PlayedDate { get; set; }
        }
    }
}
