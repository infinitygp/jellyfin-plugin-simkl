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
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Handles real-time library changes and syncs them to SIMKL collection.
    /// </summary>
    public class LibraryChangeHandler : IHostedService, IDisposable
    {
        private readonly ILogger<LibraryChangeHandler> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly SimklApi _simklApi;
        private readonly ConcurrentQueue<LibraryChangeItem> _pendingAdditions;
        private readonly ConcurrentQueue<LibraryChangeItem> _pendingRemovals;
        private Timer? _syncTimer;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryChangeHandler"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{LibraryChangeHandler}"/> interface.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        public LibraryChangeHandler(
            ILogger<LibraryChangeHandler> logger,
            ILibraryManager libraryManager,
            SimklApi simklApi)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _simklApi = simklApi;
            _pendingAdditions = new ConcurrentQueue<LibraryChangeItem>();
            _pendingRemovals = new ConcurrentQueue<LibraryChangeItem>();
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
            _logger.LogInformation("LibraryChangeHandler started");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _syncTimer?.Dispose();
            _logger.LogInformation("LibraryChangeHandler stopped");
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
                _syncTimer?.Dispose();
            }

            _disposed = true;
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            ProcessItemChange(e, isAddition: true);
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            ProcessItemChange(e, isAddition: false);
        }

        private void ProcessItemChange(ItemChangeEventArgs e, bool isAddition)
        {
            var item = e.Item;
            if (item == null || !IsValidItemType(item) || !HasRequiredIds(item))
            {
                return;
            }

            var configuration = SimklPlugin.Instance?.Configuration;
            if (configuration == null)
            {
                return;
            }

            var userConfigs = configuration.UserConfigs
                .Where(c => !string.IsNullOrEmpty(c.UserToken) && c.SyncLibraryChanges)
                .ToList();

            if (userConfigs.Count == 0)
            {
                return;
            }

            foreach (var userConfig in userConfigs)
            {
                var changeItem = new LibraryChangeItem
                {
                    Item = item,
                    UserToken = userConfig.UserToken,
                    UserId = userConfig.Id
                };

                if (isAddition)
                {
                    _pendingAdditions.Enqueue(changeItem);
                }
                else
                {
                    _pendingRemovals.Enqueue(changeItem);
                }
            }

            ResetTimer();
        }

        private void ResetTimer()
        {
            var configuration = SimklPlugin.Instance?.Configuration;
            var delay = TimeSpan.FromSeconds(configuration?.UserConfigs.FirstOrDefault()?.LibrarySyncDelay ?? 30);

            if (_syncTimer != null)
            {
                _syncTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _syncTimer = new Timer(
                    _ => ProcessPendingChangesAsync().ConfigureAwait(false),
                    null,
                    delay,
                    Timeout.InfiniteTimeSpan);
            }
        }

        private async Task ProcessPendingChangesAsync()
        {
            var additionsByUser = new Dictionary<string, List<BaseItem>>();
            while (_pendingAdditions.TryDequeue(out var addition))
            {
                if (!additionsByUser.ContainsKey(addition.UserToken))
                {
                    additionsByUser[addition.UserToken] = new List<BaseItem>();
                }

                if (addition.Item != null)
                {
                    additionsByUser[addition.UserToken].Add(addition.Item);
                }
            }

            foreach (var (userToken, items) in additionsByUser)
            {
                if (items.Count == 0)
                {
                    continue;
                }

                try
                {
                    await SyncAdditionsAsync(items, userToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing library additions for user");
                }
            }

            var removalsByUser = new Dictionary<string, List<BaseItem>>();
            while (_pendingRemovals.TryDequeue(out var removal))
            {
                if (!removalsByUser.ContainsKey(removal.UserToken))
                {
                    removalsByUser[removal.UserToken] = new List<BaseItem>();
                }

                if (removal.Item != null)
                {
                    removalsByUser[removal.UserToken].Add(removal.Item);
                }
            }

            foreach (var (userToken, items) in removalsByUser)
            {
                if (items.Count == 0)
                {
                    continue;
                }

                _logger.LogInformation("Library items removed: {Count} items. Note: SIMKL may not support removal from collection via API.", items.Count);
            }
        }

        private async Task SyncAdditionsAsync(List<BaseItem> items, string userToken)
        {
            var collection = new SimklCollection
            {
                Movies = new List<SimklCollectionMovie>(),
                Shows = new List<SimklCollectionShow>()
            };

            foreach (var item in items)
            {
                if (item is Movie movie)
                {
                    collection.Movies.Add(CreateCollectionMovie(movie));
                }
                else if (item is Series series)
                {
                    collection.Shows.Add(CreateCollectionShow(series));
                }
                else if (item is Episode episode && episode.Series != null)
                {
                    AddEpisodeToCollection(collection, episode);
                }
            }

            if (collection.Movies.Count > 0 || collection.Shows.Count > 0)
            {
                _logger.LogInformation(
                    "Syncing library additions: {MovieCount} movies, {ShowCount} shows",
                    collection.Movies.Count,
                    collection.Shows.Count);

                var response = await _simklApi.AddToCollectionAsync(collection, userToken);
                _logger.LogDebug("Synced library additions, response: {@Response}", response);
            }
        }

        private static bool IsValidItemType(BaseItem item)
        {
            return item is Movie or Series or Episode;
        }

        private static bool HasRequiredIds(BaseItem item)
        {
            var itemToCheck = item is Episode episode ? episode.Series ?? item : item;
            return !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Imdb)) ||
                   !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Tmdb)) ||
                   !string.IsNullOrEmpty(itemToCheck.GetProviderId(MetadataProvider.Tvdb));
        }

        private static SimklCollectionMovie CreateCollectionMovie(Movie movie)
        {
            return new SimklCollectionMovie
            {
                Title = movie.Name,
                Year = movie.ProductionYear,
                Ids = new SimklCollectionIds
                {
                    Imdb = movie.GetProviderId(MetadataProvider.Imdb),
                    Tmdb = movie.GetProviderId(MetadataProvider.Tmdb),
                    Tvdb = movie.GetProviderId(MetadataProvider.Tvdb)
                },
                To = "plantowatch"
            };
        }

        private static SimklCollectionShow CreateCollectionShow(Series series)
        {
            return new SimklCollectionShow
            {
                Title = series.Name,
                Year = series.ProductionYear,
                Ids = new SimklCollectionIds
                {
                    Imdb = series.GetProviderId(MetadataProvider.Imdb),
                    Tmdb = series.GetProviderId(MetadataProvider.Tmdb),
                    Tvdb = series.GetProviderId(MetadataProvider.Tvdb)
                },
                To = "plantowatch"
            };
        }

        private static void AddEpisodeToCollection(SimklCollection collection, Episode episode)
        {
            var series = episode.Series;
            if (series == null)
            {
                return;
            }

            var existingShow = collection.Shows.FirstOrDefault(s =>
                s.Ids?.Imdb == series.GetProviderId(MetadataProvider.Imdb) ||
                s.Ids?.Tmdb == series.GetProviderId(MetadataProvider.Tmdb) ||
                s.Ids?.Tvdb == series.GetProviderId(MetadataProvider.Tvdb));

            if (existingShow == null)
            {
                existingShow = new SimklCollectionShow
                {
                    Title = series.Name,
                    Year = series.ProductionYear,
                    Ids = new SimklCollectionIds
                    {
                        Imdb = series.GetProviderId(MetadataProvider.Imdb),
                        Tmdb = series.GetProviderId(MetadataProvider.Tmdb),
                        Tvdb = series.GetProviderId(MetadataProvider.Tvdb)
                    },
                    To = "plantowatch",
                    Seasons = new List<SimklCollectionSeason>()
                };
                collection.Shows.Add(existingShow);
            }

            if (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
            {
                return;
            }

            var seasonNumber = episode.ParentIndexNumber.Value;
            var existingSeason = existingShow.Seasons?.FirstOrDefault(s => s.Number == seasonNumber);

            if (existingSeason == null)
            {
                existingSeason = new SimklCollectionSeason
                {
                    Number = seasonNumber,
                    Episodes = new List<SimklCollectionEpisode>()
                };
                existingShow.Seasons ??= new List<SimklCollectionSeason>();
                existingShow.Seasons.Add(existingSeason);
            }

            existingSeason.Episodes ??= new List<SimklCollectionEpisode>();
            existingSeason.Episodes.Add(new SimklCollectionEpisode
            {
                Number = episode.IndexNumber.Value
            });
        }

        private sealed class LibraryChangeItem
        {
            public BaseItem? Item { get; set; }

            public string UserToken { get; set; } = string.Empty;

            public Guid UserId { get; set; }
        }
    }
}
