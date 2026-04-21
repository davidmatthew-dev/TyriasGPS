using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
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
        private const string PoiCacheFileName = "poi-index-cache.csv";

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
        private Label _statusLabel;
        private Label _cacheSourceLabel;
        private FlowPanel _resultsFlowPanel;
        private TextBox _whisperTargetPreviewTextBox;
        private StandardButton _copyWhisperNameButton;
        private AsyncTexture2D _windowBackgroundTexture;
        private AsyncTexture2D _moduleIconTexture;

        private Task _poiIndexLoadTask;
        private int _copyWhisperNameFeedbackToken;

        private List<PoiSearchResult> _poiIndex = new List<PoiSearchResult>();

        private readonly Dictionary<string, IReadOnlyList<PoiSearchResult>> _queryCache = new Dictionary<string, IReadOnlyList<PoiSearchResult>>();
        private readonly Dictionary<StandardButton, PoiSearchResult> _resultButtons = new Dictionary<StandardButton, PoiSearchResult>();

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

            _windowBackgroundTexture = AsyncTexture2D.FromAssetId(155997);
            _moduleIconTexture = AsyncTexture2D.FromAssetId(440023);

            return Task.CompletedTask;
        }

        private void CreateUi()
        {
            _cornerIcon = new CornerIcon
            {
                Icon = _moduleIconTexture,
                BasicTooltipText = Name,
                Parent = GameService.Graphics.SpriteScreen,
                Priority = 1743521
            };
            _cornerIcon.Click += OnCornerIconClick;

            _window = new StandardWindow(_windowBackgroundTexture, new Microsoft.Xna.Framework.Rectangle(40, 26, 500, 640), new Microsoft.Xna.Framework.Rectangle(50, 50, 480, 596))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Tyria's GPS",
                Location = new Point(300, 220)
            };

            _searchTextBox = new TextBox
            {
                Parent = _window,
                Location = new Point(10, 20),
                Size = new Point(300, 28),
                PlaceholderText = "Search map/location"
            };

            _searchButton = new StandardButton
            {
                Parent = _window,
                Text = "Search",
                Location = new Point(320, 20),
                Size = new Point(80, 28)
            };
            _searchButton.Click += OnSearchButtonClick;

            _whisperTargetPreviewTextBox = new TextBox
            {
                Parent = _window,
                Location = new Point(10, 60),
                Size = new Point(290, 28),
                PlaceholderText = "Current character name unavailable"
            };

            _copyWhisperNameButton = new StandardButton
            {
                Parent = _window,
                Text = "Copy Name",
                Location = new Point(310, 60),
                Size = new Point(170, 28),
                BasicTooltipText = "Copies your character name. Paste it into the whisper window name box."
            };
            _copyWhisperNameButton.Click += OnCopyWhisperNameButtonClick;
            RefreshWhisperNamePreview();

            _statusLabel = new Label
            {
                Parent = _window,
                Location = new Point(10, 95),
                Size = new Point(470, 28),
                WrapText = true,
                Text = "Search for a location to show matching results."
            };

            RebuildResultsPanel();

            _cacheSourceLabel = new Label
            {
                Parent = _window,
                Location = new Point(10, 126),
                Size = new Point(470, 22),
                WrapText = false,
                TextColor = Microsoft.Xna.Framework.Color.LightGray,
                Text = "Results source: Waiting for first search."
            };

            _window.Show();
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            base.OnModuleLoaded(e);
            CreateUi();
        }

        private void OnCornerIconClick(object sender, MouseEventArgs e)
        {
            _window?.ToggleWindow();
        }

        private async void OnSearchButtonClick(object sender, MouseEventArgs e)
        {
            await RunSearchAsync();
        }

        private async void OnCopyWhisperNameButtonClick(object sender, MouseEventArgs e)
        {
            await CopyWhisperNameAsync();
        }

        private async Task RunSearchAsync()
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                _statusLabel.Text = "Enter a search term before searching.";
                return;
            }

            LogHelper.Log("Searching for location: " + query);
            _statusLabel.Text = $"Searching for: {query}";
            UpdateCacheSourceLabel("Results source: Searching...");
            _searchButton.Enabled = false;
            _searchButton.Text = "Searching...";
            var searchingStateStartedAt = DateTime.UtcNow;
            await Task.Yield();

            try
            {
                await EnsurePoiIndexReadyAsync();
                bool fromQueryCache;
                var results = FindMatches(query, out fromQueryCache).Take(25).ToList();
                UpdateSearchResultsSourceLabel(query, fromQueryCache);

                if (results.Count == 0)
                {
                    _statusLabel.Text = "No POIs matched the query.";
                    RebuildResultsPanel();
                    LogHelper.Log("No POIs matched query: " + query);
                    return;
                }

                _statusLabel.Text = $"Found {results.Count} results, sorting...";
                RenderSearchResults(results);
                _statusLabel.Text = $"Found {results.Count} POI matches.";
                LogHelper.Log($"Search completed for query: {query}, found {results.Count} POI matches.");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Search failed. Check logs for details.";
                RebuildResultsPanel();
                LogHelper.LogException(ex, "Search request failed");
            }
            finally
            {
                var searchingVisibleFor = DateTime.UtcNow - searchingStateStartedAt;
                var minimumSearchingVisibleMs = 250;
                if (searchingVisibleFor.TotalMilliseconds < minimumSearchingVisibleMs)
                {
                    await Task.Delay(minimumSearchingVisibleMs - (int)searchingVisibleFor.TotalMilliseconds);
                }

                _searchButton.Enabled = true;
                _searchButton.Text = "Search";
            }
        }

        private void RebuildResultsPanel()
        {
            _resultsFlowPanel?.Dispose();
            _resultButtons.Clear();
            _resultsFlowPanel = new FlowPanel
            {
                Parent = _window,
                Location = new Point(10, 155),
                Size = new Point(470, 385),
                Title = "Results",
                ShowBorder = true,
                CanScroll = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0f, 4f),
                OuterControlPadding = new Vector2(8f, 8f)
            };
        }

        private void RenderSearchResults(IReadOnlyCollection<PoiSearchResult> results)
        {
            RebuildResultsPanel();

            foreach (var result in results)
            {
                var button = new StandardButton
                {
                    Parent = _resultsFlowPanel,
                    Width = 440,
                    Height = 34,
                    Text = $"{result.Name} [{result.Type}] - {result.MapName}",
                    BasicTooltipText = $"Click to copy the chat link for {result.Name}. Use /w <name> first, then paste it.",
                    Icon = _moduleIconTexture,
                    ResizeIcon = true
                };

                button.Click += OnResultButtonClick;
                _resultButtons[button] = result;
            }
        }

        private async void OnResultButtonClick(object sender, MouseEventArgs e)
        {
            if (sender is StandardButton button && _resultButtons.TryGetValue(button, out var result))
            {
                await CopyResultLinkAsync(result);
            }
        }

        private async Task CopyResultLinkAsync(PoiSearchResult result)
        {
            string clipboardText = result.ChatLink;
            await ClipboardUtil.WindowsClipboardService.SetTextAsync(clipboardText);

            _statusLabel.Text = $"Copied the chat link for {result.Name}.";
            LogHelper.Log($"Copied clipboard text for '{result.Name}': {clipboardText}");
        }

        private async Task CopyWhisperNameAsync()
        {
            string currentCharacterName = GameService.Gw2Mumble.PlayerCharacter?.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentCharacterName))
            {
                _statusLabel.Text = "Could not detect your active character name yet.";
                LogHelper.Log("Copy Name requested, but active character name was unavailable.");
                RefreshWhisperNamePreview();
                return;
            }

            await ClipboardUtil.WindowsClipboardService.SetTextAsync(currentCharacterName);
            RefreshWhisperNamePreview();
            _statusLabel.Text = "Copied Name: " + currentCharacterName;
            LogHelper.Log("Copied Name: " + currentCharacterName);

            int feedbackToken = ++_copyWhisperNameFeedbackToken;
            _copyWhisperNameButton.Text = "Copied";
            await Task.Delay(5000);
            if (_copyWhisperNameFeedbackToken == feedbackToken)
            {
                _copyWhisperNameButton.Text = "Copy Name";
            }
        }

        private void RefreshWhisperNamePreview()
        {
            if (_whisperTargetPreviewTextBox == null)
            {
                return;
            }

            string text = GameService.Gw2Mumble.PlayerCharacter?.Name?.Trim() ?? string.Empty;
            _whisperTargetPreviewTextBox.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
        }

        private async Task EnsurePoiIndexReadyAsync()
        {
            if (_poiIndexLoadTask == null)
            {
                lock (_poiIndexLock)
                {
                    if (_poiIndexLoadTask == null)
                    {
                        _poiIndexLoadTask = LoadPoiIndexFromDiskOrRebuildAsync();
                    }
                }
            }

            await _poiIndexLoadTask;
        }

        private async Task LoadPoiIndexFromDiskOrRebuildAsync()
        {
            if (TryLoadPoiIndexFromDisk(out var cachedPoiIndex))
            {
                _poiIndex = cachedPoiIndex;
                _queryCache.Clear();
                LogHelper.Log($"POI index loaded from cache with {_poiIndex.Count} entries.");
                return;
            }

            LogHelper.Log("POI cache unavailable. Rebuilding index from API data.");
            await RebuildPoiIndexAsync();
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
                if (floorTarget.FloorId <= 0)
                {
                    LogHelper.Log($"Skipping unsupported floor id {floorTarget.FloorId} on continent {floorTarget.ContinentId}.");
                    continue;
                }

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
            SavePoiIndexToDisk(_poiIndex);
            LogHelper.Log($"POI index ready with {_poiIndex.Count} searchable entries.");
        }

        private void UpdateCacheSourceLabel(string text)
        {
            if (_cacheSourceLabel != null)
            {
                _cacheSourceLabel.Text = text;
            }
        }

        private void UpdateSearchResultsSourceLabel(string query, bool fromQueryCache)
        {
            if (fromQueryCache)
            {
                UpdateCacheSourceLabel($"Results source: Previous query cache ({query}).");
                return;
            }

            UpdateCacheSourceLabel("Results source: Fresh index search.");
        }

        private string GetPoiCachePath()
        {
            string logsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            return Path.Combine(logsPath, PoiCacheFileName);
        }

        private void SavePoiIndexToDisk(IReadOnlyCollection<PoiSearchResult> poiIndex)
        {
            try
            {
                string cachePath = GetPoiCachePath();
                var lines = new List<string>(poiIndex.Count + 1)
                {
                    "Name,ChatLink,MapName,RegionName,Type,IconUrl"
                };

                foreach (var result in poiIndex)
                {
                    lines.Add(string.Join(",",
                        EscapeCsv(result.Name),
                        EscapeCsv(result.ChatLink),
                        EscapeCsv(result.MapName),
                        EscapeCsv(result.RegionName),
                        EscapeCsv(result.Type),
                        EscapeCsv(result.IconUrl)));
                }

                System.IO.File.WriteAllLines(cachePath, lines, Encoding.UTF8);
                LogHelper.Log($"POI cache saved: {cachePath} ({poiIndex.Count} entries).");
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "Failed to write POI index cache");
            }
        }

        private bool TryLoadPoiIndexFromDisk(out List<PoiSearchResult> poiIndex)
        {
            poiIndex = new List<PoiSearchResult>();

            try
            {
                string cachePath = GetPoiCachePath();
                if (!System.IO.File.Exists(cachePath))
                {
                    LogHelper.Log($"POI cache not found at: {cachePath}");
                    return false;
                }

                string[] lines = System.IO.File.ReadAllLines(cachePath, Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    LogHelper.Log($"POI cache file is empty or header-only: {cachePath}");
                    return false;
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    string[] columns = ParseCsvLine(lines[i]);
                    if (columns.Length < 6)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(columns[0]) || string.IsNullOrWhiteSpace(columns[1]))
                    {
                        continue;
                    }

                    poiIndex.Add(new PoiSearchResult
                    {
                        Name = columns[0],
                        ChatLink = columns[1],
                        MapName = columns[2],
                        RegionName = columns[3],
                        Type = columns[4],
                        IconUrl = columns[5]
                    });
                }

                if (poiIndex.Count == 0)
                {
                    return false;
                }

                poiIndex = poiIndex
                    .GroupBy(result => result.ChatLink)
                    .Select(group => group.First())
                    .ToList();

                LogHelper.Log($"POI cache read from: {cachePath}");

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogException(ex, "Failed to read POI index cache");
                poiIndex = new List<PoiSearchResult>();
                return false;
            }
        }

        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;
            if (text.Contains('"'))
            {
                text = text.Replace("\"", "\"\"");
            }

            if (text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n'))
            {
                return $"\"{text}\"";
            }

            return text;
        }

        private static string[] ParseCsvLine(string line)
        {
            var columns = new List<string>();
            var builder = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    columns.Add(builder.ToString());
                    builder.Clear();
                    continue;
                }

                builder.Append(c);
            }

            columns.Add(builder.ToString());
            return columns.ToArray();
        }

        private IReadOnlyList<PoiSearchResult> FindMatches(string query, out bool fromQueryCache)
        {
            var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
            fromQueryCache = false;

            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return Array.Empty<PoiSearchResult>();
            }

            if (_queryCache.TryGetValue(normalizedQuery, out var cachedResults))
            {
                fromQueryCache = true;
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

            if (_copyWhisperNameButton != null)
            {
                _copyWhisperNameButton.Click -= OnCopyWhisperNameButtonClick;
            }

            foreach (StandardButton button in _resultButtons.Keys.ToList())
            {
                button.Click -= OnResultButtonClick;
            }

            _resultButtons.Clear();

            if (_cornerIcon != null)
            {
                _cornerIcon.Click -= OnCornerIconClick;
                _cornerIcon.Dispose();
                _cornerIcon = null;
            }

            _resultsFlowPanel?.Dispose();
            _window?.Dispose();
            _windowBackgroundTexture = null;
            _moduleIconTexture = null;
        }
    }
}

