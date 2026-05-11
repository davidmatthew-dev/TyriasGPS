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
using Microsoft.Xna.Framework.Input;
using TyriasGPS.Controls;

namespace TyriasGPS
{
    [Export(typeof(Module))]
    public class TyriasGPSModule : Module
    {
        private static readonly Logger Logger = Logger.GetLogger<TyriasGPSModule>();
        private const string PoiCacheFileName = "poi-index-cache.csv";

        private static readonly string[] ResultsUsageBullets = new string[]
        {
            "How to use:",
            "- Type a POI, map, or location into the search box",
            "- Click Search",
            "- Open a whisper window",
            "- Click Copy Name to copy your character name",
            "- Click a result to copy the POI chat link",
            "- Paste the link into chat"
        };

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
        private SettingEntry<KeyBinding> _openWindowKeybind;
        private KeyBinding _boundOpenWindowKeybind;
        private CornerIcon _cornerIcon;
        private StandardWindow _window;
        private TextBox _searchTextBox;
        private GpsActionButton _searchButton;
        private GpsActionButton _clearSearchButton;
        private GpsActionButton _clearCacheButton;
        private Label _statusLabel;
        private Label _cacheSourceLabel;
        private FlowPanel _resultsFlowPanel;
        private GpsActionButton _copyNameButton;
        private Label _versionLabel;
        private AsyncTexture2D _windowBackgroundTexture;
        private AsyncTexture2D _moduleIconTexture;
        private Panel _topBackgroundPanel;

        private Task _poiIndexLoadTask;
        private int _copyNameFeedbackToken;
        private int _clearSearchFeedbackToken;
        private int _clearCacheFeedbackToken;
        private string _cachedCharacterName;

        private List<PoiSearchResult> _poiIndex = new List<PoiSearchResult>();

        private readonly Dictionary<string, IReadOnlyList<PoiSearchResult>> _queryCache = new Dictionary<string, IReadOnlyList<PoiSearchResult>>();
        private readonly Dictionary<GpsResultItem, PoiSearchResult> _resultButtons = new Dictionary<GpsResultItem, PoiSearchResult>();

        [ImportingConstructor]
        public TyriasGPSModule([Import("ModuleParameters")] ModuleParameters moduleParameters) : base(moduleParameters)
        {
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            _locationSearch = settings.DefineSetting("locationSearch", string.Empty, () => "Location Search", () => "Search term for POI search.");
            _openWindowKeybind = settings.DefineSetting("openWindowKeybind", new KeyBinding(Keys.None), () => "Open Window Keybind", () => "Keybind to open the location search window.");
        }

        protected override void Initialize()
        {
        }

        protected override Task LoadAsync()
        {
            Logger.Info("Module loaded.");

            _windowBackgroundTexture = AsyncTexture2D.FromAssetId(502049);
            _moduleIconTexture = ModuleParameters.ContentsManager.GetTexture("icons/module-icon.png");

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

            _window = new StandardWindow(
                _windowBackgroundTexture,
                new Microsoft.Xna.Framework.Rectangle(35, 26, 930, 710),
                new Microsoft.Xna.Framework.Rectangle(35, 11, 924, 699))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Tyria's GPS",
                Location = new Point(300, 220),
                Size = new Point(500, 660)
            };
            _window.Emblem = _moduleIconTexture;

            _topBackgroundPanel = new Panel
            {
                Parent = _window,
                Location = new Point(0, 0),
                Size = new Point(494, 620),
                BackgroundColor = new Microsoft.Xna.Framework.Color(30, 34, 42, 255),
                ZIndex = 0
            };

            _searchTextBox = new TextBox
            {
                Parent = _window,
                Location = new Point(5, 8),
                Size = new Point(330, 28),
                PlaceholderText = "Search map/location"
            };
            _searchTextBox.EnterPressed += OnSearchTextBoxEnterPressed;

            _searchButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Search",
                Location = new Point(340, 8),
                Size = new Point(148, 28)
            };
            _searchButton.Click += OnSearchButtonClick;

            _clearSearchButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Clear Search",
                Location = new Point(340, 40),
                Size = new Point(148, 28),
                BasicTooltipText = "Clear search box and results."
            };
            _clearSearchButton.Click += OnClearSearchButtonClick;

            _clearCacheButton = new GpsActionButton
            {
                Parent = _window,
                Location = new Point(5, 568),
                Size = new Point(148, 28),
                Text = "Clear Cache",
                Style = GpsActionButtonStyle.Accent,
                BasicTooltipText = "Clear in-memory and disk cache."
            };
            _clearCacheButton.Click += OnClearCacheButtonClick;

            _copyNameButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Copy Name",
                Location = new Point(5, 40),
                Size = new Point(148, 28),
                BasicTooltipText = "Copies your character name. Paste it into the whisper window name box."
            };
            _copyNameButton.Click += OnCopyNameButtonClick;

            _statusLabel = new Label
            {
                Parent = _window,
                Location = new Point(5, 72),
                Size = new Point(484, 28),
                WrapText = true,
                Text = "Search for a location to show matching results."
            };

            RebuildResultsPanel();

            _cacheSourceLabel = new Label
            {
                Parent = _window,
                Location = new Point(5, 100),
                Size = new Point(484, 20),
                WrapText = false,
                TextColor = Microsoft.Xna.Framework.Color.LightGray,
                Text = "Results source: Waiting for first search."
            };

            _versionLabel = new Label
            {
                Parent = _window,
                Location = new Point(260, 570),
                Size = new Point(229, 20),
                Text = "v" + typeof(Module).GetProperty("Version")?.GetValue(this),
                TextColor = Microsoft.Xna.Framework.Color.LightGray,
                HorizontalAlignment = HorizontalAlignment.Right,
                WrapText = false
            };
        }

        protected override void OnModuleLoaded(EventArgs e)
        {
            base.OnModuleLoaded(e);
            CreateUi();
            EnsureOpenWindowKeybindHooked();
        }

        private void EnsureOpenWindowKeybindHooked()
        {
            KeyBinding currentKeybind = _openWindowKeybind?.Value;

            if (ReferenceEquals(_boundOpenWindowKeybind, currentKeybind))
            {
                return;
            }

            if (_boundOpenWindowKeybind != null)
            {
                _boundOpenWindowKeybind.Activated -= OnOpenWindowKeybindActivated;
                _boundOpenWindowKeybind.BindingChanged -= OnOpenWindowKeybindBindingChanged;
                Logger.Debug("Open window keybind handler detached from previous binding.");
            }

            _boundOpenWindowKeybind = currentKeybind;

            if (_boundOpenWindowKeybind != null)
            {
                ApplyOpenWindowKeybindSettings(_boundOpenWindowKeybind);
                _boundOpenWindowKeybind.BindingChanged += OnOpenWindowKeybindBindingChanged;
                _boundOpenWindowKeybind.Activated += OnOpenWindowKeybindActivated;
                Logger.Debug("Open window keybind handler attached: " + _boundOpenWindowKeybind.GetBindingDisplayText());
            }
        }

        private void ApplyOpenWindowKeybindSettings(KeyBinding keybind)
        {
            if (keybind == null)
            {
                return;
            }

            keybind.Enabled = true;
            keybind.IgnoreWhenInTextField = false;

            Logger.Debug($"Open window keybind configured: Primary={keybind.PrimaryKey}, Modifiers={keybind.ModifierKeys}, Enabled={keybind.Enabled}, IgnoreWhenInTextField={keybind.IgnoreWhenInTextField}, Display='{keybind.GetBindingDisplayText()}'");
        }

        private void OnOpenWindowKeybindBindingChanged(object sender, EventArgs e)
        {
            if (sender is KeyBinding keybind)
            {
                ApplyOpenWindowKeybindSettings(keybind);
                Logger.Debug("Open window keybind changed by user.");
            }
        }

        private void OnOpenWindowKeybindActivated(object sender, EventArgs e)
        {
            if (_window == null)
            {
                Logger.Debug("Open window keybind activated, but window is not ready yet.");
                return;
            }

            if (_window.Visible)
            {
                _window.Hide();
                Logger.Debug("Open window keybind activated: window hidden.");
            }
            else
            {
                _window.Show();
                Logger.Debug("Open window keybind activated: window shown.");
            }
        }

        protected override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            EnsureOpenWindowKeybindHooked();

            if (_window == null)
            {
                return;
            }

            if (_cachedCharacterName == null)
            {
                string name = GameService.Gw2Mumble.PlayerCharacter?.Name?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _cachedCharacterName = name;
                    Logger.Debug("Character name detected: " + name);
                }
            }
        }

        private void OnCornerIconClick(object sender, MouseEventArgs e)
        {
            _window?.ToggleWindow();
        }

        private async void OnSearchButtonClick(object sender, MouseEventArgs e)
        {
            await RunSearchAsync();
        }

        private async void OnSearchTextBoxEnterPressed(object sender, EventArgs e)
        {
            Logger.Debug("Search requested via Enter key.");
            await RunSearchAsync();
        }

        private async void OnClearSearchButtonClick(object sender, MouseEventArgs e)
        {
            _searchTextBox.Text = string.Empty;
            _resultsFlowPanel.ClearChildren();
            _resultButtons.Clear();
            _statusLabel.Text = "Search cleared. Enter a new search term.";
            _cacheSourceLabel.Text = "Results source: Waiting for first search.";
            RebuildResultsPanel();
            Logger.Debug("Search and results cleared by user.");

            int feedbackToken = ++_clearSearchFeedbackToken;
            _clearSearchButton.Text = "Search Cleared";
            await Task.Delay(5000);
            if (_clearSearchFeedbackToken == feedbackToken)
            {
                _clearSearchButton.Text = "Clear Search";
            }
        }

        private async void OnClearCacheButtonClick(object sender, MouseEventArgs e)
        {
            ClearPoiCaches();

            int feedbackToken = ++_clearCacheFeedbackToken;
            _clearCacheButton.Text = "Cache Cleared";
            await Task.Delay(5000);
            if (_clearCacheFeedbackToken == feedbackToken)
            {
                _clearCacheButton.Text = "Clear Cache";
            }
        }

        private void ClearPoiCaches()
        {
            try
            {
                int queryCacheCount = _queryCache.Count;
                int poiCount = _poiIndex?.Count ?? 0;

                _queryCache.Clear();
                _poiIndex = new List<PoiSearchResult>();

                lock (_poiIndexLock)
                {
                    _poiIndexLoadTask = null;
                }

                string cachePath = GetPoiCachePath();
                if (System.IO.File.Exists(cachePath))
                {
                    System.IO.File.Delete(cachePath);
                    Logger.Debug("Clear Cache used: removed disk cache file at " + cachePath + ".");
                }
                else
                {
                    Logger.Debug("Clear Cache used: no disk cache file found at " + cachePath + ".");
                }

                UpdateCacheSourceLabel("Results source: Cache cleared. Next search will rebuild index.");
                _statusLabel.Text = "Cache cleared. Next search will rebuild POI index.";
                Logger.Debug($"Clear Cache used: cleared {queryCacheCount} query-cache entries and reset {poiCount} indexed POIs.");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Cache clear failed. Check logs for details.";
                Logger.Warn(ex, "Clear Cache action failed.");
            }
        }

        private async void OnCopyNameButtonClick(object sender, MouseEventArgs e)
        {
            await CopyNameAsync();
        }

        private async Task RunSearchAsync()
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                _statusLabel.Text = "Enter a search term before searching.";
                return;
            }

            Logger.Debug("Searching for location: " + query);
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
                    Logger.Debug("No POIs matched query: " + query);
                    return;
                }

                _statusLabel.Text = $"Found {results.Count} results, sorting...";
                RenderSearchResults(results);
                _statusLabel.Text = $"Found {results.Count} POI matches.";
                Logger.Info($"Search completed for query: {query}, found {results.Count} POI matches.");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Search failed. Check logs for details.";
                RebuildResultsPanel();
                Logger.Warn(ex, "Search request failed.");
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

        private void RebuildResultsPanel(bool showUsageText = true)
        {
            _topBackgroundPanel?.Dispose();
            _resultsFlowPanel?.Dispose();
            _resultButtons.Clear();
            _resultsFlowPanel = new FlowPanel
            {
                Parent = _window,
                Location = new Point(5, 148),
                Size = new Point(484, 416),
                Title = "Results",
                ShowBorder = true,
                BackgroundColor = new Microsoft.Xna.Framework.Color(14, 20, 36, 210),
                CanScroll = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0f, 4f),
                OuterControlPadding = new Vector2(8f, 8f)
            };

            if (!showUsageText)
            {
                return;
            }

            foreach (string bullet in ResultsUsageBullets)
            {
                bool isHeader = !bullet.StartsWith("-");
                var label = new Label
                {
                    Parent = _resultsFlowPanel,
                    Width = 452,
                    AutoSizeHeight = true,
                    WrapText = false,
                    TextColor = isHeader ? Microsoft.Xna.Framework.Color.White : Microsoft.Xna.Framework.Color.LightGray,
                    Text = bullet
                };

                if (isHeader)
                {
                    label.Font = GameService.Content.DefaultFont16;
                }
            }
        }

        private void RenderSearchResults(IReadOnlyCollection<PoiSearchResult> results)
        {
            RebuildResultsPanel(showUsageText: false);

            foreach (var result in results)
            {
                var item = new GpsResultItem
                {
                    Parent = _resultsFlowPanel,
                    Width = 452,
                    Height = 34,
                    Text = $"{result.Name} [{result.Type}] - {result.MapName}",
                    BasicTooltipText = $"Click to copy the chat link for {result.Name}. Use /w <name> first, then paste it.",
                    Icon = _moduleIconTexture
                };

                item.Click += OnResultButtonClick;
                _resultButtons[item] = result;
            }
        }

        private async void OnResultButtonClick(object sender, MouseEventArgs e)
        {
            if (sender is GpsResultItem item && _resultButtons.TryGetValue(item, out var result))
            {
                await CopyResultLinkAsync(result);
            }
        }

        private async Task CopyResultLinkAsync(PoiSearchResult result)
        {
            string clipboardText = result.ChatLink;
            await ClipboardUtil.WindowsClipboardService.SetTextAsync(clipboardText);

            _statusLabel.Text = $"Copied the chat link for {result.Name}.";
            Logger.Debug($"Copied clipboard text for '{result.Name}': {clipboardText}");
        }

        private async Task CopyNameAsync()
        {
            string currentCharacterName = _cachedCharacterName
                ?? GameService.Gw2Mumble.PlayerCharacter?.Name?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentCharacterName))
            {
                _statusLabel.Text = "Could not detect your active character name yet.";
                Logger.Debug("Copy Name requested, but active character name was unavailable.");
                return;
            }

            await ClipboardUtil.WindowsClipboardService.SetTextAsync(currentCharacterName);
            _statusLabel.Text = "Copied Name: " + currentCharacterName;
            Logger.Debug("Copied Name: " + currentCharacterName);

            int feedbackToken = ++_copyNameFeedbackToken;
            _copyNameButton.Text = "Copied";
            await Task.Delay(5000);
            if (_copyNameFeedbackToken == feedbackToken)
            {
                _copyNameButton.Text = "Copy Name";
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
                Logger.Info($"POI index loaded from cache with {_poiIndex.Count} entries.");
                return;
            }

            Logger.Info("POI cache unavailable. Rebuilding index from API data.");
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

            Logger.Info($"Building POI index from {floorTargets.Count} continent-floor combinations.");

            foreach (var floorTarget in floorTargets)
            {
                if (floorTarget.FloorId <= 0)
                {
                    Logger.Debug($"Skipping unsupported floor id {floorTarget.FloorId} on continent {floorTarget.ContinentId}.");
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
                    Logger.Warn(ex, $"Failed to index continent {floorTarget.ContinentId} floor {floorTarget.FloorId}.");
                }
            }

            _poiIndex = poiIndex
                .GroupBy(result => result.ChatLink)
                .Select(group => group.First())
                .ToList();

            _queryCache.Clear();
            SavePoiIndexToDisk(_poiIndex);
            Logger.Info($"POI index ready with {_poiIndex.Count} searchable entries.");
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
            string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
            return Path.Combine(cacheFolder, PoiCacheFileName);
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
                Logger.Debug($"POI cache saved: {cachePath} ({poiIndex.Count} entries).");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write POI index cache.");
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
                    Logger.Debug($"POI cache not found at: {cachePath}");
                    return false;
                }

                string[] lines = System.IO.File.ReadAllLines(cachePath, Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    Logger.Debug($"POI cache file is empty or header-only: {cachePath}");
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

                Logger.Debug($"POI cache read from: {cachePath}");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to read POI index cache.");
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
            if (_searchTextBox != null)
            {
                _searchTextBox.EnterPressed -= OnSearchTextBoxEnterPressed;
            }

            if (_searchButton != null)
            {
                _searchButton.Click -= OnSearchButtonClick;
            }

            if (_clearSearchButton != null)
            {
                _clearSearchButton.Click -= OnClearSearchButtonClick;
            }

            if (_clearCacheButton != null)
            {
                _clearCacheButton.Click -= OnClearCacheButtonClick;
            }

            if (_copyNameButton != null)
            {
                _copyNameButton.Click -= OnCopyNameButtonClick;
            }

            foreach (GpsResultItem item in _resultButtons.Keys.ToList())
            {
                item.Click -= OnResultButtonClick;
            }

            _resultButtons.Clear();

            if (_cornerIcon != null)
            {
                _cornerIcon.Click -= OnCornerIconClick;
                _cornerIcon.Dispose();
                _cornerIcon = null;
            }

            if (_boundOpenWindowKeybind != null)
            {
                _boundOpenWindowKeybind.Activated -= OnOpenWindowKeybindActivated;
                _boundOpenWindowKeybind.BindingChanged -= OnOpenWindowKeybindBindingChanged;
                _boundOpenWindowKeybind = null;
            }

            _topBackgroundPanel?.Dispose();
            _resultsFlowPanel?.Dispose();
            _window?.Dispose();
            _windowBackgroundTexture = null;
            _moduleIconTexture = null;
        }
    }
}
