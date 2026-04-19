using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Gw2Sharp;
using Gw2Sharp.WebApi.V2.Models;
using Microsoft.Xna.Framework;

namespace TyriasGPS
{
    [Export(typeof(Module))]
    public class TyriasGPSModule : Module
    {
        private sealed class PoiSearchResult
        {
            public string Name { get; set; }

            public string ChatLink { get; set; }

            public string MapName { get; set; }

            public string RegionName { get; set; }

            public string Type { get; set; }

            public string IconUrl { get; set; }
        }

        private readonly Gw2Client _publicGw2Client = new Gw2Client();
        private readonly object _poiIndexLock = new object();

        private SettingEntry<string> _locationSearch;
        private CornerIcon _cornerIcon;
        private StandardWindow _window;
        private TextBox _searchTextBox;
        private StandardButton _searchButton;
        private Label _resultLabel;

        private Task _poiIndexLoadTask;

        private List<PoiSearchResult> _poiIndex = new List<PoiSearchResult>();

        private readonly Dictionary<string, IReadOnlyList<PoiSearchResult>> _queryCache = new Dictionary<string, IReadOnlyList<PoiSearchResult>>();

        [ImportingConstructor]
        public TyriasGPSModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            _locationSearch = settings.DefineSetting("locationSearch", string.Empty, () => "Location Search", () => "Search term for POI search.");
        }

        protected override void Initialize()
        {
        }

        protected override Task LoadAsync()
        {
            LogHelper.Log("Module loaded.");

            var cornerIconTexture = ModuleParameters.ContentsManager.GetTexture("corner-icon.png");
            _cornerIcon = new CornerIcon
            {
                Icon = cornerIconTexture,
                BasicTooltipText = Name,
                Parent = GameService.Graphics.SpriteScreen,
                Priority = 1743521
            };
            _cornerIcon.Click += OnCornerIconClick;

            var windowTexture = ModuleParameters.ContentsManager.GetTexture("window-background.png");

            _window = new StandardWindow(windowTexture, new Microsoft.Xna.Framework.Rectangle(40, 26, 280, 200), new Microsoft.Xna.Framework.Rectangle(50, 36, 260, 170))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Tyria's GPS",
                Location = new Point(300, 220)
            };

            _searchTextBox = new TextBox
            {
                Parent = _window,
                Location = new Point(10, 20),
                Size = new Point(180, 28),
                PlaceholderText = "Search map/location"
            };

            _searchButton = new StandardButton
            {
                Parent = _window,
                Text = "Search",
                Location = new Point(200, 20),
                Size = new Point(60, 28)
            };
            _searchButton.Click += OnSearchButtonClick;

            _resultLabel = new Label
            {
                Parent = _window,
                Location = new Point(10, 60),
                Size = new Point(240, 80),
                WrapText = true
            };

            _window.Show();

            return Task.CompletedTask;
        }

        private void OnCornerIconClick(object sender, MouseEventArgs e)
        {
            _window?.ToggleWindow();
        }

        private async void OnSearchButtonClick(object sender, MouseEventArgs e)
        {
            await RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
                return;

            LogHelper.Log("Searching for location: " + query);

            try
            {
                await EnsurePoiIndexReadyAsync();
                var results = FindMatches(query).Take(25).ToList();

                if (results.Count == 0)
                {
                    _resultLabel.Text = "No POIs matched the query.";
                    LogHelper.Log("No POIs matched query: " + query);
                    return;
                }

                var topResult = results.First();
                _resultLabel.Text = $"{topResult.MapName}: {topResult.Name} {topResult.ChatLink}";
                LogHelper.Log($"Search completed for query: {query}, found {results.Count} POI matches.");
            }
            catch (Exception ex)
            {
                _resultLabel.Text = "Search failed. Check logs for details.";
                LogHelper.LogException(ex, "Search request failed");
            }
        }

        private async Task EnsurePoiIndexReadyAsync()
        {
            if (_poiIndexLoadTask == null)
            {
                lock (_poiIndexLock)
                {
                    if (_poiIndexLoadTask == null)
                    {
                        _poiIndexLoadTask = RebuildPoiIndexAsync();
                    }
                }
            }

            await _poiIndexLoadTask;
        }

        private async Task RebuildPoiIndexAsync()
        {
            var poiIndex = new List<PoiSearchResult>();

            var floorTargets = (await _publicGw2Client.WebApi.V2.Maps.AllAsync())
                .Where(map => map.ContinentId > 0)
                .GroupBy(map => new { map.ContinentId, FloorId = map.DefaultFloor })
                .Select(group => group.Key)
                .ToList();

            LogHelper.Log($"Building POI index from {floorTargets.Count} continent-floor combinations.");

            foreach (var floorTarget in floorTargets)
            {
                try
                {
                    var floor = await _publicGw2Client.WebApi.V2.Continents[floorTarget.ContinentId].Floors[floorTarget.FloorId].GetAsync();

                    foreach (var region in floor.Regions.Values)
                    {
                        foreach (var map in region.Maps.Values)
                        {
                            var pois = map.PointsOfInterest?.Values ?? Enumerable.Empty<ContinentFloorRegionMapPoi>();

                            foreach (var poi in pois)
                            {
                                if (string.IsNullOrWhiteSpace(poi.Name) || string.IsNullOrWhiteSpace(poi.ChatLink))
                                {
                                    continue;
                                }

                                poiIndex.Add(new PoiSearchResult
                                {
                                    Name = poi.Name.Trim(),
                                    ChatLink = poi.ChatLink.Trim(),
                                    MapName = map.Name?.Trim() ?? string.Empty,
                                    RegionName = region.Name?.Trim() ?? string.Empty,
                                    Type = poi.Type.ToString(),
                                    IconUrl = poi.Icon?.Url?.AbsoluteUri
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogException(ex, $"Failed to index continent {floorTarget.ContinentId} floor {floorTarget.FloorId}");
                }
            }

            _poiIndex = poiIndex
                .GroupBy(result => result.ChatLink)
                .Select(group => group.First())
                .ToList();

            _queryCache.Clear();
            LogHelper.Log($"POI index ready with {_poiIndex.Count} searchable entries.");
        }

        private IReadOnlyList<PoiSearchResult> FindMatches(string query)
        {
            var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return Array.Empty<PoiSearchResult>();
            }

            if (_queryCache.TryGetValue(normalizedQuery, out var cachedResults))
            {
                return cachedResults;
            }

            var matches = QueryPoiIndex(normalizedQuery).Take(200).ToList();
            _queryCache[normalizedQuery] = matches;
            return matches;
        }

        private IEnumerable<PoiSearchResult> QueryPoiIndex(string normalizedQuery)
        {
            var tokens = normalizedQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            return _poiIndex
                .Select(result => new
                {
                    Result = result,
                    Score = GetMatchScore(result, normalizedQuery, tokens)
                })
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.Result.Name, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Result);
        }

        private static int GetMatchScore(PoiSearchResult result, string normalizedQuery, string[] tokens)
        {
            var name = (result.Name ?? string.Empty).ToLowerInvariant();
            var mapName = (result.MapName ?? string.Empty).ToLowerInvariant();
            var regionName = (result.RegionName ?? string.Empty).ToLowerInvariant();
            var type = (result.Type ?? string.Empty).ToLowerInvariant();
            var searchableText = string.Join(" ", name, mapName, regionName, type);

            if (name == normalizedQuery)
            {
                return 120;
            }

            if (name.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return 100;
            }

            if (name.Contains(normalizedQuery))
            {
                return 90;
            }

            if (mapName == normalizedQuery)
            {
                return 80;
            }

            if (mapName.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return 70;
            }

            if (mapName.Contains(normalizedQuery))
            {
                return 60;
            }

            if (tokens.Length > 1 && tokens.All(token => searchableText.Contains(token)))
            {
                return 50;
            }

            if (searchableText.Contains(normalizedQuery))
            {
                return 40;
            }

            return 0;
        }

        protected override void Unload()
        {
            if (_searchButton != null)
            {
                _searchButton.Click -= OnSearchButtonClick;
            }

            if (_cornerIcon != null)
            {
                _cornerIcon.Click -= OnCornerIconClick;
                _cornerIcon.Dispose();
                _cornerIcon = null;
            }

            _window?.Dispose();
        }
    }
}
