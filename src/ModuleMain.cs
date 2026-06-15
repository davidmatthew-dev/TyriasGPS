using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
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
        private const string PoiCacheFileName = "poi-index-cache.json";
        private const string WikiSearchCacheFileName = "wiki-search-cache.json";
        private const string PoiOverridesFileName = "poi-overrides.json";
        private const string NpcOverridesFileName = "npc-overrides.json";
        private const string WikiPrefix = "/wiki ";
        private const string ModuleVersionString = "1.9.0";
        private static readonly string[] ManagedCacheFiles = new[] { PoiCacheFileName, WikiSearchCacheFileName, PoiOverridesFileName, NpcOverridesFileName };
        private static readonly string[] ObsoleteCacheFiles = new[] { "poi-index-cache.csv" };
        private const int CompactStatusY = 72;
        private const int CompactStatusHeight = 28;
        private const int CompactCacheSourceY = 100;
        private const int CompactResultsY = 148;
        private const int CompactResultsHeight = 416;
        private const int ExpandedStatusY = 76;
        private const int ExpandedStatusHeight = 44;
        private const int ExpandedCacheSourceY = 124;
        private const int ExpandedResultsY = 172;
        private const int ExpandedResultsHeight = 392;

        private static readonly string[] ResultsUsageBullets = new string[]
        {
            "How to use:",
            "- Type a POI, map, location, or NPC name into the search box",
            "- Click Search",
            "- Open a whisper window",
            "- Click Copy Name to copy your character name",
            "- Click a result to copy the chat link or command",
            "- Paste the link into chat"
        };

        private sealed class PoiSearchResult
        {
            public string Name { get; set; }

            public string ChatLink { get; set; }

            public string ClipboardText { get; set; }

            public string MapName { get; set; }

            public string RegionName { get; set; }

            public string Type { get; set; }

            public string IconUrl { get; set; }

            public string[] Aliases { get; set; }
        }

        private sealed class PoiCacheDocument
        {
            public int Version { get; set; }

            public string ModuleVersion { get; set; }

            public List<PoiSearchResult> Results { get; set; }
        }

        private sealed class WikiSearchCacheDocument
        {
            public int Version { get; set; }

            public string ModuleVersion { get; set; }

            public List<WikiSearchCacheEntry> Queries { get; set; }
        }

        private sealed class WikiSearchCacheEntry
        {
            public string Query { get; set; }

            public DateTime CachedAtUtc { get; set; }

            public List<PoiSearchResult> Results { get; set; }
        }

        private sealed class LocalSearchEntry
        {
            public string Name { get; set; }

            public string ChatLink { get; set; }

            public string ClipboardText { get; set; }

            public string MapName { get; set; }

            public string RegionName { get; set; }

            public string Type { get; set; }

            public string IconUrl { get; set; }

            public string[] Aliases { get; set; }
        }

        private sealed class LocalOverridesDocument
        {
            public int Version { get; set; }

            public string ModuleVersion { get; set; }

            public List<LocalSearchEntry> Entries { get; set; }
        }

        private readonly Gw2Client _publicGw2Client = new Gw2Client();
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly object _poiIndexLock = new object();
        private readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue
        };

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
        private GpsActionButton _searchWikiButton;
        private Label _versionLabel;
        private AsyncTexture2D _windowBackgroundTexture;
        private AsyncTexture2D _moduleIconTexture;
        private Panel _topBackgroundPanel;

        private Task _poiIndexLoadTask;
        private string _lastNoResultQuery;
        private int _copyNameFeedbackToken;
        private int _clearSearchFeedbackToken;
        private int _clearCacheFeedbackToken;
        private int _searchInProgress;
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
                PlaceholderText = "Search location or NPC name"
            };

            _searchButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Search",
                Location = new Point(340, 8),
                Size = new Point(148, 28)
            };
            _searchButton.Click += OnSearchButtonClick;
            _searchTextBox.EnterPressed += OnSearchTextBoxEnterPressed;

            _clearSearchButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Clear Search",
                Location = new Point(340, 40),
                Size = new Point(148, 28),
                BasicTooltipText = "Clear search box and results."
            };
            _clearSearchButton.Click += OnClearSearchButtonClick;

            _searchWikiButton = new GpsActionButton
            {
                Parent = _window,
                Text = "Search Wiki",
                Location = new Point(340, 72),
                Size = new Point(148, 28),
                Style = GpsActionButtonStyle.Accent,
                BasicTooltipText = "Open the wiki in your browser for this search.",
                Visible = false
            };
            _searchWikiButton.Click += OnSearchWikiButtonClick;

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
                Location = new Point(5, CompactStatusY),
                Size = new Point(330, CompactStatusHeight),
                WrapText = true,
                Text = "Search for a location to show matching results."
            };

            RebuildResultsPanel();

            _cacheSourceLabel = new Label
            {
                Parent = _window,
                Location = new Point(5, CompactCacheSourceY),
                Size = new Point(484, 20),
                WrapText = false,
                TextColor = Microsoft.Xna.Framework.Color.LightGray,
                Text = "Results source: Waiting for first search."
            };

            SetStatusText("Search for a location to show matching results.");

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
            ClearObsoleteCachesIfVersionChanged();
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
            await RunSearchAsync();
        }

        private async void OnClearSearchButtonClick(object sender, MouseEventArgs e)
        {
            _searchTextBox.Text = string.Empty;
            _resultsFlowPanel.ClearChildren();
            _resultButtons.Clear();
            _lastNoResultQuery = null;
            if (_searchWikiButton != null) _searchWikiButton.Visible = false;
            SetStatusText("Search cleared. Enter a new search term.");
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

            _clearCacheButton.Enabled = false;
            
            try
            {
                Logger.Info("Clear Cache: regenerating default override files...");
                EnsureLocalOverrideFilesExist();
                Logger.Info("Clear Cache: triggering immediate index rebuild...");
                await EnsurePoiIndexReadyAsync();
                SetStatusText("Cache cleared and index rebuilt from fresh API data and defaults.");
                Logger.Info("Clear Cache: index rebuild complete and saved to disk.");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Clear Cache: failed during forced rebuild.");
                SetStatusText("Cache cleared but rebuild failed. Check logs.");
            }

            int feedbackToken = ++_clearCacheFeedbackToken;
            _clearCacheButton.Text = "Cache Cleared";
            _clearCacheButton.Enabled = true;
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

                string wikiCachePath = GetWikiSearchCachePath();
                if (System.IO.File.Exists(wikiCachePath))
                {
                    System.IO.File.Delete(wikiCachePath);
                    Logger.Debug("Clear Cache used: removed wiki cache file at " + wikiCachePath + ".");
                }
                else
                {
                    Logger.Debug("Clear Cache used: no wiki cache file found at " + wikiCachePath + ".");
                }

                string poiOverridesPath = GetPoiOverridesPath();
                if (System.IO.File.Exists(poiOverridesPath))
                {
                    System.IO.File.Delete(poiOverridesPath);
                    Logger.Debug("Clear Cache used: removed POI override file at " + poiOverridesPath + ".");
                }

                string npcOverridesPath = GetNpcOverridesPath();
                if (System.IO.File.Exists(npcOverridesPath))
                {
                    System.IO.File.Delete(npcOverridesPath);
                    Logger.Debug("Clear Cache used: removed NPC override file at " + npcOverridesPath + ".");
                }

                UpdateCacheSourceLabel("Results source: Cache cleared. Next search will rebuild index.");
                SetStatusText("Cache cleared. Next search will rebuild POI index.");
                Logger.Debug($"Clear Cache used: cleared {queryCacheCount} query-cache entries and reset {poiCount} indexed POIs.");
            }
            catch (Exception ex)
            {
                SetStatusText("Cache clear failed. Check logs for details.");
                Logger.Warn(ex, "Clear Cache action failed.");
            }
        }

        private async void OnCopyNameButtonClick(object sender, MouseEventArgs e)
        {
            await CopyNameAsync();
        }

        private async void OnSearchWikiButtonClick(object sender, MouseEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastNoResultQuery))
            {
                Logger.Debug("Search Wiki clicked but no query was stored.");
                return;
            }

            string query = _lastNoResultQuery;
            Logger.Debug("Search Wiki clicked for query: " + query);

            _searchWikiButton.Text = "Opening...";
            _searchWikiButton.Enabled = false;

            try
            {
                string url = "https://wiki.guildwars2.com/wiki/Special:Search?search=" + Uri.EscapeDataString(query);
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                Logger.Debug("Opened wiki in browser: " + url);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to open wiki in browser for query: " + query);
                SetStatusText("Could not open browser. Check logs for details.");
            }

            await Task.Delay(2000);
            _searchWikiButton.Text = "Search Wiki";
            _searchWikiButton.Enabled = true;
        }

        private async Task RunSearchAsync()
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                SetStatusText("Enter a search term before searching.");
                return;
            }

            if (Interlocked.Exchange(ref _searchInProgress, 1) == 1)
            {
                Logger.Debug("Search ignored: another search is already running.");
                return;
            }

            Logger.Debug("Searching for location: " + query);
            SetStatusText($"Searching for: {query}");
            UpdateCacheSourceLabel("Results source: Searching...");
            _searchButton.Enabled = false;
            _searchButton.Text = "Searching...";
            var searchingStateStartedAt = DateTime.UtcNow;
            await Task.Yield();

            try
            {
                await EnsurePoiIndexReadyAsync();

                if (IsBankingHubQuery(query))
                {
                    var bankingResults = await FindBankingHubPoiResultsAsync();
                    UpdateCacheSourceLabel("Results source: Banking hub location lookup.");

                    if (bankingResults.Count > 0)
                    {
                        SaveResolvedBankingOverrides(bankingResults);
                        if (_searchWikiButton != null) _searchWikiButton.Visible = false;
                        RenderSearchResults(bankingResults);
                        SetStatusText($"Showing {bankingResults.Count} locations for \"{query}\".");
                        Logger.Info($"Banking query '{query}' returned {bankingResults.Count} location results.");
                        return;
                    }

                    RebuildResultsPanel(showUsageText: false);
                    new Label
                    {
                        Parent = _resultsFlowPanel,
                        Width = 452,
                        AutoSizeHeight = true,
                        WrapText = true,
                        TextColor = Microsoft.Xna.Framework.Color.LightGray,
                        Text = "No banking hub locations could be mapped to POIs."
                    };

                    _lastNoResultQuery = query;
                    if (_searchWikiButton != null) _searchWikiButton.Visible = true;
                    SetStatusText("No mapped banking hub POIs found. Use Search Wiki for direct lookup.");
                    Logger.Debug("Banking query returned no mapped POI locations.");
                    return;
                }

                bool fromQueryCache;
                var results = FindMatches(query, out fromQueryCache).Take(25).ToList();
                UpdateSearchResultsSourceLabel(query, fromQueryCache);

                if (results.Count == 0)
                {
                    var wikiLocationResults = await FindWikiLocationResultsAsync(query);
                    if (wikiLocationResults.Count > 0)
                    {
                        RenderSearchResults(wikiLocationResults);
                        SetStatusText($"No API results found for \"{query}\". Showing wiki page locations.");
                        _lastNoResultQuery = query;
                        if (_searchWikiButton != null) _searchWikiButton.Visible = true;
                        Logger.Info($"No API results found for '{query}'. Added {wikiLocationResults.Count} location results from wiki page data.");
                        return;
                    }

                    RebuildResultsPanel(showUsageText: false);
                    new Label
                    {
                        Parent = _resultsFlowPanel,
                        Width = 452,
                        AutoSizeHeight = true,
                        WrapText = true,
                        TextColor = Microsoft.Xna.Framework.Color.LightGray,
                        Text = $"No API results found for \"{query}\"."
                    };
                    new Label
                    {
                        Parent = _resultsFlowPanel,
                        Width = 452,
                        AutoSizeHeight = true,
                        WrapText = true,
                        TextColor = Microsoft.Xna.Framework.Color.LightGray,
                        Text = "Click Search Wiki above to look it up on the wiki."
                    };
                    _lastNoResultQuery = query;
                    if (_searchWikiButton != null) _searchWikiButton.Visible = true;
                    SetStatusText($"No API results found for \"{query}\".");
                    Logger.Debug("No entries matched query: " + query);
                    return;
                }

                if (_searchWikiButton != null) _searchWikiButton.Visible = false;
                SetStatusText($"Found {results.Count} results, sorting...");
                RenderSearchResults(results);
                SetStatusText($"Found {results.Count} matches.");
                Logger.Info($"Search completed for query: {query}, found {results.Count} matches.");
            }
            catch (Exception ex)
            {
                string errorSummary = string.IsNullOrWhiteSpace(ex.Message)
                    ? "unknown error"
                    : ex.Message.Trim();
                SetStatusText($"Search failed: {errorSummary}");
                RebuildResultsPanel();
                Logger.Warn(ex, $"Search request failed for query '{query}'.");
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
                Interlocked.Exchange(ref _searchInProgress, 0);
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
                Location = new Point(5, CompactResultsY),
                Size = new Point(484, CompactResultsHeight),
                Title = "Results",
                ShowBorder = true,
                BackgroundColor = new Microsoft.Xna.Framework.Color(14, 20, 36, 210),
                CanScroll = true,
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                ControlPadding = new Vector2(0f, 4f),
                OuterControlPadding = new Vector2(8f, 8f)
            };

            UpdateTopLayoutForStatusText(_statusLabel?.Text);

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
                    BasicTooltipText = BuildResultTooltipText(result),
                    Icon = _moduleIconTexture
                };

                item.Click += OnResultButtonClick;
                _resultButtons[item] = result;
            }
        }

        private static string BuildResultTooltipText(PoiSearchResult result)
        {
            string copyValue = string.IsNullOrWhiteSpace(result?.ClipboardText)
                ? result?.ChatLink
                : result.ClipboardText;

            string name = string.IsNullOrWhiteSpace(result?.Name)
                ? "this result"
                : result.Name;

            if (!string.IsNullOrWhiteSpace(result?.ChatLink)
                && string.Equals(copyValue?.Trim(), result.ChatLink?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return $"Click to copy the chat link for {name}. Use /w <name> first, then paste it.";
            }

            if (!string.IsNullOrWhiteSpace(copyValue)
                && copyValue.Trim().StartsWith(WikiPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return $"Click to copy the wiki command for {name}.";
            }

            return $"Click to copy the value for {name}.";
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
            string clipboardText = string.IsNullOrWhiteSpace(result.ClipboardText)
                ? result.ChatLink
                : result.ClipboardText;

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                SetStatusText($"No copy value configured for {result.Name}.");
                Logger.Debug($"No copy value configured for '{result.Name}'.");
                return;
            }

            await ClipboardUtil.WindowsClipboardService.SetTextAsync(clipboardText);

            SetStatusText($"Copied value for {result.Name}.");
            Logger.Debug($"Copied clipboard text for '{result.Name}': {clipboardText}");
        }

        private async Task CopyNameAsync()
        {
            string currentCharacterName = _cachedCharacterName
                ?? GameService.Gw2Mumble.PlayerCharacter?.Name?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(currentCharacterName))
            {
                SetStatusText("Could not detect your active character name yet.");
                Logger.Debug("Copy Name requested, but active character name was unavailable.");
                return;
            }

            await ClipboardUtil.WindowsClipboardService.SetTextAsync(currentCharacterName);
            SetStatusText("Copied Name: " + currentCharacterName);
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
            EnsureLocalOverrideFilesExist();

            if (TryLoadPoiIndexFromDisk(out var cachedPoiIndex))
            {
                var localEntries = LoadLocalSearchEntries();
                if (localEntries.Count > 0)
                {
                    cachedPoiIndex.AddRange(localEntries);
                    cachedPoiIndex = cachedPoiIndex
                        .GroupBy(GetResultKey)
                        .Select(group => group.First())
                        .ToList();
                    Logger.Info($"Merged {localEntries.Count} POI/NPC entries into cached index.");
                }

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
            EnsureLocalOverrideFilesExist();
            
            var poiIndex = new List<PoiSearchResult>();

            var localEntries = LoadLocalSearchEntries();
            if (localEntries.Count > 0)
            {
                poiIndex.AddRange(localEntries);
                Logger.Info($"Added {localEntries.Count} POI/NPC entries from JSON overrides.");
            }

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
                                    ClipboardText = poi.ChatLink.Trim(),
                                    MapName = map.Name?.Trim() ?? string.Empty,
                                    RegionName = region.Name?.Trim() ?? string.Empty,
                                    Type = poi.Type.ToString(),
                                    IconUrl = poi.Icon?.Url?.AbsoluteUri,
                                    Aliases = Array.Empty<string>()
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
                .GroupBy(GetResultKey)
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

        private void SetStatusText(string text)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.Text = text ?? string.Empty;
            UpdateTopLayoutForStatusText(_statusLabel.Text);
        }

        private void UpdateTopLayoutForStatusText(string statusText)
        {
            bool expanded = ShouldExpandStatusLayout(statusText);
            int statusY = expanded ? ExpandedStatusY : CompactStatusY;
            int statusHeight = expanded ? ExpandedStatusHeight : CompactStatusHeight;
            int cacheSourceY = expanded ? ExpandedCacheSourceY : CompactCacheSourceY;
            int resultsY = expanded ? ExpandedResultsY : CompactResultsY;
            int resultsHeight = expanded ? ExpandedResultsHeight : CompactResultsHeight;

            _statusLabel.Location = new Point(5, statusY);
            _statusLabel.Size = new Point(330, statusHeight);

            if (_cacheSourceLabel != null)
            {
                _cacheSourceLabel.Location = new Point(5, cacheSourceY);
            }

            if (_resultsFlowPanel != null)
            {
                _resultsFlowPanel.Location = new Point(5, resultsY);
                _resultsFlowPanel.Size = new Point(484, resultsHeight);
            }
        }

        private bool ShouldExpandStatusLayout(string statusText)
        {
            if (string.IsNullOrWhiteSpace(statusText))
            {
                return false;
            }

            if (statusText.Contains("\n"))
            {
                return true;
            }

            string compactText = Regex.Replace(statusText, "\\s+", " ").Trim();
            if (compactText.Length <= 58)
            {
                return false;
            }

            try
            {
                var font = _statusLabel?.Font ?? GameService.Content.DefaultFont14;
                float maxWidth = (_statusLabel?.Width ?? 330) - 8;
                return font.MeasureString(compactText).Width > maxWidth;
            }
            catch
            {
                return compactText.Length > 58;
            }
        }

        private string GetPoiCachePath()
        {
            string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
            return Path.Combine(cacheFolder, PoiCacheFileName);
        }

        private string GetWikiSearchCachePath()
        {
            string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
            return Path.Combine(cacheFolder, WikiSearchCacheFileName);
        }

        private string GetPoiOverridesPath()
        {
            string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
            return Path.Combine(cacheFolder, PoiOverridesFileName);
        }

        private string GetNpcOverridesPath()
        {
            string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
            return Path.Combine(cacheFolder, NpcOverridesFileName);
        }

        private void ClearObsoleteCachesIfVersionChanged()
        {
            try
            {
                string currentModuleVersion = ModuleVersionString;
                string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");

                bool versionMismatch = false;

                foreach (string fileName in ManagedCacheFiles)
                {
                    string filePath = Path.Combine(cacheFolder, fileName);
                    if (!System.IO.File.Exists(filePath))
                    {
                        continue;
                    }

                    try
                    {
                        string json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            versionMismatch = true;
                            break;
                        }

                        var cacheDoc = _jsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (cacheDoc == null || !cacheDoc.ContainsKey("ModuleVersion"))
                        {
                            versionMismatch = true;
                            break;
                        }

                        string cachedVersion = cacheDoc["ModuleVersion"] as string;
                        if (cachedVersion != currentModuleVersion)
                        {
                            versionMismatch = true;
                            break;
                        }
                    }
                    catch
                    {
                        versionMismatch = true;
                        break;
                    }
                }

                if (versionMismatch)
                {
                    Logger.Info($"Module version changed to {currentModuleVersion}. Clearing managed cache files.");
                    foreach (string fileName in ManagedCacheFiles)
                    {
                        string filePath = Path.Combine(cacheFolder, fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            try
                            {
                                System.IO.File.Delete(filePath);
                                Logger.Debug($"Deleted cache file: {filePath}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn(ex, $"Failed to delete cache file: {filePath}");
                            }
                        }
                    }
                }

                foreach (string obsoleteFileName in ObsoleteCacheFiles)
                {
                    string filePath = Path.Combine(cacheFolder, obsoleteFileName);
                    if (System.IO.File.Exists(filePath))
                    {
                        try
                        {
                            System.IO.File.Delete(filePath);
                            Logger.Info($"Removed obsolete cache file: {filePath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn(ex, $"Failed to delete obsolete cache file: {filePath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to check and clear obsolete caches.");
            }
        }

        private void SavePoiIndexToDisk(IReadOnlyCollection<PoiSearchResult> poiIndex)
        {
            try
            {
                string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
                string cachePath = Path.Combine(cacheFolder, PoiCacheFileName);
                
                if (!System.IO.Directory.Exists(cacheFolder))
                {
                    System.IO.Directory.CreateDirectory(cacheFolder);
                    Logger.Debug($"Created cache directory: {cacheFolder}");
                }

                var cacheDocument = new PoiCacheDocument
                {
                    Version = 1,
                    ModuleVersion = ModuleVersionString,
                    Results = poiIndex.ToList()
                };

                string json = PreserveLiteralWaypointLinks(_jsonSerializer.Serialize(cacheDocument));
                System.IO.File.WriteAllText(cachePath, FormatJson(json), Encoding.UTF8);
                Logger.Info($"POI cache saved: {cachePath} ({poiIndex.Count} entries).");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write POI index cache.");
            }
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            var builder = new StringBuilder(json.Length + 128);
            var indentLevel = 0;
            var inString = false;
            var escapeNext = false;

            foreach (char ch in json)
            {
                if (escapeNext)
                {
                    builder.Append(ch);
                    escapeNext = false;
                    continue;
                }

                if (ch == '\\')
                {
                    builder.Append(ch);
                    if (inString)
                    {
                        escapeNext = true;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    builder.Append(ch);
                    continue;
                }

                if (inString)
                {
                    builder.Append(ch);
                    continue;
                }

                if (ch == '{' || ch == '[')
                {
                    builder.Append(ch);
                    builder.AppendLine();
                    indentLevel++;
                    builder.Append(new string(' ', indentLevel * 2));
                    continue;
                }

                if (ch == '}' || ch == ']')
                {
                    builder.AppendLine();
                    indentLevel = Math.Max(0, indentLevel - 1);
                    builder.Append(new string(' ', indentLevel * 2));
                    builder.Append(ch);
                    continue;
                }

                if (ch == ',')
                {
                    builder.Append(ch);
                    builder.AppendLine();
                    builder.Append(new string(' ', indentLevel * 2));
                    continue;
                }

                if (ch == ':')
                {
                    builder.Append(": ");
                    continue;
                }

                if (!char.IsWhiteSpace(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string PreserveLiteralWaypointLinks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            return Regex.Replace(json, @"\\u0026", "&", RegexOptions.IgnoreCase);
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

                string json = System.IO.File.ReadAllText(cachePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var cacheDocument = _jsonSerializer.Deserialize<PoiCacheDocument>(json);
                if (cacheDocument?.Results == null || cacheDocument.Results.Count == 0)
                {
                    return false;
                }

                foreach (var cachedResult in cacheDocument.Results)
                {
                    if (string.IsNullOrWhiteSpace(cachedResult?.Name))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(cachedResult.ClipboardText))
                    {
                        cachedResult.ClipboardText = string.IsNullOrWhiteSpace(cachedResult.ChatLink)
                            ? WikiPrefix + cachedResult.Name
                            : cachedResult.ChatLink;
                    }

                    if (cachedResult.Aliases == null)
                    {
                        cachedResult.Aliases = Array.Empty<string>();
                    }

                    poiIndex.Add(cachedResult);
                }

                poiIndex = poiIndex
                    .GroupBy(GetResultKey)
                    .Select(group => group.First())
                    .ToList();

                if (poiIndex.Count == 0)
                {
                    return false;
                }

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

        private static string GetResultKey(PoiSearchResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ChatLink))
            {
                return "chat:" + result.ChatLink.Trim();
            }

            return "name:" + (result.Name ?? string.Empty).Trim().ToLowerInvariant();
        }

        private void EnsureLocalOverrideFilesExist()
        {
            EnsureLocalOverrideFile(GetPoiOverridesPath(), BuildDefaultPoiOverrideEntries());
            EnsureLocalOverrideFile(GetNpcOverridesPath(), BuildDefaultNpcOverrideEntries());
        }

        private void EnsureLocalOverrideFile(string filePath, IReadOnlyCollection<LocalSearchEntry> defaultEntries)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    return;
                }

                var document = new LocalOverridesDocument
                {
                    Version = 1,
                    ModuleVersion = ModuleVersionString,
                    Entries = defaultEntries.ToList()
                };

                string json = PreserveLiteralWaypointLinks(_jsonSerializer.Serialize(document));
                System.IO.File.WriteAllText(filePath, FormatJson(json), Encoding.UTF8);
                Logger.Info($"Created override file: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to create override file: {filePath}");
            }
        }

        private List<PoiSearchResult> LoadLocalSearchEntries()
        {
            var localEntries = new List<PoiSearchResult>();

            localEntries.AddRange(ReadLocalEntries(GetPoiOverridesPath()));
            localEntries.AddRange(ReadLocalEntries(GetNpcOverridesPath()));

            return localEntries;
        }

        private List<LocalSearchEntry> ReadLocalOverrideEntries(string filePath)
        {
            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    return new List<LocalSearchEntry>();
                }

                string json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<LocalSearchEntry>();
                }

                var overrideDocument = _jsonSerializer.Deserialize<LocalOverridesDocument>(json);
                if (overrideDocument?.Entries != null)
                {
                    return overrideDocument.Entries;
                }

                return _jsonSerializer.Deserialize<List<LocalSearchEntry>>(json) ?? new List<LocalSearchEntry>();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to read override entries from {filePath}.");
                return new List<LocalSearchEntry>();
            }
        }

        private void SaveLocalOverrideEntries(string filePath, IReadOnlyCollection<LocalSearchEntry> entries)
        {
            try
            {
                string folder = Path.GetDirectoryName(filePath) ?? DirectoryUtil.RegisterDirectory("Tyrias-GPS");
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                var document = new LocalOverridesDocument
                {
                    Version = 1,
                    ModuleVersion = ModuleVersionString,
                    Entries = entries?.ToList() ?? new List<LocalSearchEntry>()
                };

                string json = PreserveLiteralWaypointLinks(_jsonSerializer.Serialize(document));
                System.IO.File.WriteAllText(filePath, FormatJson(json), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to save override entries to {filePath}.");
            }
        }

        private void SaveResolvedBankingOverrides(IReadOnlyCollection<PoiSearchResult> resolvedResults)
        {
            if (resolvedResults == null || resolvedResults.Count == 0)
            {
                return;
            }

            try
            {
                string npcOverridesPath = GetNpcOverridesPath();
                var existingEntries = ReadLocalOverrideEntries(npcOverridesPath);

                existingEntries = existingEntries
                    .Where(entry => !(entry?.Name ?? string.Empty).StartsWith("Banking Hub - ", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var dedupedResults = resolvedResults
                    .Where(result => !string.IsNullOrWhiteSpace(result.ChatLink))
                    .GroupBy(result => result.ChatLink.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                foreach (var result in dedupedResults)
                {
                    existingEntries.Add(new LocalSearchEntry
                    {
                        Name = "Banking Hub - " + (string.IsNullOrWhiteSpace(result.MapName) ? "Unknown" : result.MapName.Trim()),
                        ChatLink = result.ChatLink.Trim(),
                        ClipboardText = result.ChatLink.Trim(),
                        MapName = result.MapName?.Trim() ?? string.Empty,
                        RegionName = result.RegionName?.Trim() ?? string.Empty,
                        Type = "NpcTrader",
                        Aliases = new[] { "bank", "trading post", "merchant", "tp" }
                    });
                }

                SaveLocalOverrideEntries(npcOverridesPath, existingEntries);
                Logger.Info($"Saved {dedupedResults.Count} banking hub waypoint entries to npc overrides.");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to save banking hub override entries.");
            }
        }

        private IEnumerable<PoiSearchResult> ReadLocalEntries(string filePath)
        {
            var convertedEntries = new List<PoiSearchResult>();

            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    return convertedEntries;
                }

                string json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return convertedEntries;
                }

                List<LocalSearchEntry> entries = null;

                var overrideDocument = _jsonSerializer.Deserialize<LocalOverridesDocument>(json);
                if (overrideDocument?.Entries != null && overrideDocument.Entries.Count > 0)
                {
                    entries = overrideDocument.Entries;
                }
                else
                {
                    // Backward compatibility for old array-only override files.
                    entries = _jsonSerializer.Deserialize<List<LocalSearchEntry>>(json) ?? new List<LocalSearchEntry>();
                }

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry?.Name))
                    {
                        continue;
                    }

                    if (string.Equals(entry.Name.Trim(), "Mistlock Sanctuary", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ChatLink = "[&BPMJAAA=]";
                        entry.ClipboardText = "[&BPMJAAA=]";
                    }

                    string normalizedClipboardText = string.IsNullOrWhiteSpace(entry.ClipboardText)
                        ? (string.IsNullOrWhiteSpace(entry.ChatLink) ? WikiPrefix + entry.Name.Trim() : entry.ChatLink.Trim())
                        : entry.ClipboardText.Trim();

                    convertedEntries.Add(new PoiSearchResult
                    {
                        Name = entry.Name.Trim(),
                        ChatLink = entry.ChatLink?.Trim(),
                        ClipboardText = normalizedClipboardText,
                        MapName = entry.MapName?.Trim() ?? string.Empty,
                        RegionName = entry.RegionName?.Trim() ?? string.Empty,
                        Type = entry.Type?.Trim() ?? "Manual",
                        IconUrl = entry.IconUrl?.Trim(),
                        Aliases = entry.Aliases ?? Array.Empty<string>()
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load entries from {filePath}.");
            }

            return convertedEntries;
        }

        private static IReadOnlyCollection<LocalSearchEntry> BuildDefaultPoiOverrideEntries()
        {
            return new[]
            {
                new LocalSearchEntry
                {
                    Name = "Mistlock Sanctuary",
                    ChatLink = "[&BPMJAAA=]",
                    ClipboardText = "[&BPMJAAA=]",
                    MapName = "Fractals of the Mists",
                    RegionName = "Special",
                    Type = "ManualWaypoint",
                    Aliases = new[] { "mistlock", "mistlock sanctuary", "mistlock pass" }
                }
            };
        }

        private static IReadOnlyCollection<LocalSearchEntry> BuildDefaultNpcOverrideEntries()
        {
            return Array.Empty<LocalSearchEntry>();
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
            if (_queryCache.Count >= 200)
            {
                _queryCache.Clear();
            }
            _queryCache[normalizedQuery] = matches;
            return matches;
        }

        private static bool IsBankingHubQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            var normalizedQuery = query.Trim().ToLowerInvariant();
            return normalizedQuery.Contains("bank")
                || normalizedQuery.Contains("trading post")
                || normalizedQuery == "tp"
                || normalizedQuery.Contains("merchant");
        }

        private async Task<List<PoiSearchResult>> FindBankingHubPoiResultsAsync()
        {
            var hubWaypointResults = await FindMajorHubWaypointResultsAsync();

            return hubWaypointResults
                .Take(25)
                .ToList();
        }

        private async Task<List<PoiSearchResult>> FindMajorHubWaypointResultsAsync()
        {
            string[] majorHubMaps =
            {
                "Lion's Arch",
                "Divinity's Reach",
                "The Grove",
                "Hoelbrak",
                "Rata Sum",
                "Black Citadel",
                "Amnoon",
                "Arborstone",
                "Wizard's Tower",
                "Eye of the North"
            };

            var mapTasks = majorHubMaps
                .Select(FindBestBankingWaypointForMapAsync)
                .ToArray();

            var mapResults = await Task.WhenAll(mapTasks);

            return mapResults
                .Where(result => result != null)
                .GroupBy(GetResultKey)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<PoiSearchResult> FindBestBankingWaypointForMapAsync(string mapName)
        {
            var mapWaypoints = _poiIndex
                .Where(result =>
                    !string.IsNullOrWhiteSpace(result.ChatLink)
                    && string.Equals(result.MapName, mapName, StringComparison.OrdinalIgnoreCase)
                    && IsWaypointType(result.Type))
                .OrderBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bankWikiResults = await FindWikiLocationResultsAsync(mapName + " bank");
            var tradingPostWikiResults = await FindWikiLocationResultsAsync(mapName + " trading post");

            var mapWikiWaypoints = bankWikiResults
                .Concat(tradingPostWikiResults)
                .Where(result =>
                    !string.IsNullOrWhiteSpace(result.ChatLink)
                    && string.Equals(result.MapName, mapName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(GetResultKey)
                .Select(group => group.First())
                .ToList();

            if (mapWikiWaypoints.Count > 0)
            {
                var preferredWikiWaypoint = mapWikiWaypoints.FirstOrDefault(result =>
                    (result.Name ?? string.Empty).IndexOf("trader", StringComparison.OrdinalIgnoreCase) >= 0
                    || (result.Name ?? string.Empty).IndexOf("bank", StringComparison.OrdinalIgnoreCase) >= 0
                    || (result.Name ?? string.Empty).IndexOf("trading post", StringComparison.OrdinalIgnoreCase) >= 0);

                if (preferredWikiWaypoint != null)
                {
                    return preferredWikiWaypoint;
                }

                return mapWikiWaypoints.First();
            }

            return mapWaypoints.FirstOrDefault();
        }

        private static bool IsWaypointType(string type)
        {
            return !string.IsNullOrWhiteSpace(type)
                && type.IndexOf("waypoint", StringComparison.OrdinalIgnoreCase) >= 0;
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
            var aliases = string.Join(" ", result.Aliases ?? Array.Empty<string>()).ToLowerInvariant();
            var searchableText = string.Join(" ", name, mapName, regionName, type, aliases);

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

        private async Task<List<PoiSearchResult>> FindWikiLocationResultsAsync(string query)
        {
            var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return new List<PoiSearchResult>();
            }

            if (TryLoadWikiSearchResultsFromDisk(normalizedQuery, out var cachedWikiResults))
            {
                Logger.Debug("Loaded wiki results from disk cache for query: " + query);
                return cachedWikiResults;
            }

            var locations = await GetWikiPageLocationsAsync(query);
            if (locations.Count == 0)
            {
                return new List<PoiSearchResult>();
            }

            var matches = new List<PoiSearchResult>();
            foreach (var location in locations)
            {
                var normalizedLocation = location.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normalizedLocation))
                {
                    continue;
                }

                var bestMatch = QueryPoiIndex(normalizedLocation)
                    .FirstOrDefault(result => !string.IsNullOrWhiteSpace(result.ChatLink) || !string.IsNullOrWhiteSpace(result.ClipboardText));

                if (bestMatch != null)
                {
                    matches.Add(bestMatch);
                }
                else
                {
                    matches.Add(new PoiSearchResult
                    {
                        Name = location.Trim(),
                        ChatLink = string.Empty,
                        ClipboardText = WikiPrefix + location.Trim(),
                        MapName = "Wiki",
                        RegionName = "Wiki Search",
                        Type = "WikiLocation",
                        IconUrl = null,
                        Aliases = Array.Empty<string>()
                    });
                }
            }

            var finalResults = matches
                .GroupBy(GetResultKey)
                .Select(group => group.First())
                .Take(25)
                .ToList();

            if (finalResults.Count > 0)
            {
                SaveWikiSearchResultsToDisk(normalizedQuery, finalResults);
            }

            return finalResults;
        }

        private bool TryLoadWikiSearchResultsFromDisk(string normalizedQuery, out List<PoiSearchResult> results)
        {
            results = new List<PoiSearchResult>();

            try
            {
                string cachePath = GetWikiSearchCachePath();
                if (!System.IO.File.Exists(cachePath))
                {
                    return false;
                }

                string json = System.IO.File.ReadAllText(cachePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var cacheDocument = _jsonSerializer.Deserialize<WikiSearchCacheDocument>(json);
                if (cacheDocument?.Queries == null || cacheDocument.Queries.Count == 0)
                {
                    return false;
                }

                var cacheEntry = cacheDocument.Queries
                    .FirstOrDefault(entry => string.Equals(entry.Query, normalizedQuery, StringComparison.OrdinalIgnoreCase));

                if (cacheEntry?.Results == null || cacheEntry.Results.Count == 0)
                {
                    return false;
                }

                if ((DateTime.UtcNow - cacheEntry.CachedAtUtc).TotalDays > 7)
                {
                    return false;
                }

                foreach (var cachedResult in cacheEntry.Results)
                {
                    if (string.IsNullOrWhiteSpace(cachedResult?.Name))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(cachedResult.ClipboardText))
                    {
                        cachedResult.ClipboardText = string.IsNullOrWhiteSpace(cachedResult.ChatLink)
                            ? WikiPrefix + cachedResult.Name
                            : cachedResult.ChatLink;
                    }

                    if (cachedResult.Aliases == null)
                    {
                        cachedResult.Aliases = Array.Empty<string>();
                    }

                    results.Add(cachedResult);
                }

                results = results
                    .GroupBy(GetResultKey)
                    .Select(group => group.First())
                    .ToList();

                return results.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read wiki search cache.");
                results = new List<PoiSearchResult>();
                return false;
            }
        }

        private void SaveWikiSearchResultsToDisk(string normalizedQuery, IReadOnlyCollection<PoiSearchResult> results)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery) || results == null || results.Count == 0)
            {
                return;
            }

            try
            {
                string cacheFolder = DirectoryUtil.RegisterDirectory("Tyrias-GPS");
                string cachePath = Path.Combine(cacheFolder, WikiSearchCacheFileName);
                
                if (!System.IO.Directory.Exists(cacheFolder))
                {
                    System.IO.Directory.CreateDirectory(cacheFolder);
                    Logger.Debug($"Created cache directory: {cacheFolder}");
                }

                WikiSearchCacheDocument cacheDocument = null;

                if (System.IO.File.Exists(cachePath))
                {
                    string existingJson = System.IO.File.ReadAllText(cachePath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(existingJson))
                    {
                        cacheDocument = _jsonSerializer.Deserialize<WikiSearchCacheDocument>(existingJson);
                    }
                }

                if (cacheDocument == null)
                {
                    cacheDocument = new WikiSearchCacheDocument
                    {
                        Version = 1,
                        ModuleVersion = ModuleVersionString,
                        Queries = new List<WikiSearchCacheEntry>()
                    };
                }

                if (cacheDocument.Queries == null)
                {
                    cacheDocument.Queries = new List<WikiSearchCacheEntry>();
                }

                cacheDocument.Queries.RemoveAll(entry => string.Equals(entry.Query, normalizedQuery, StringComparison.OrdinalIgnoreCase));
                cacheDocument.Queries.Add(new WikiSearchCacheEntry
                {
                    Query = normalizedQuery,
                    CachedAtUtc = DateTime.UtcNow,
                    Results = results.ToList()
                });

                string json = PreserveLiteralWaypointLinks(_jsonSerializer.Serialize(cacheDocument));
                System.IO.File.WriteAllText(cachePath, FormatJson(json), Encoding.UTF8);
                Logger.Info($"Wiki search cache saved: {cachePath} ({cacheDocument.Queries.Count} queries).");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write wiki search cache.");
            }
        }

        private async Task<List<string>> GetWikiPageLocationsAsync(string query)
        {
            try
            {
                var encodedTitle = Uri.EscapeDataString(query.Replace(' ', '_'));
                var url = "https://wiki.guildwars2.com/api.php?action=query&prop=revisions&rvprop=content&rvslots=main&redirects=1&format=json&formatversion=2&titles=" + encodedTitle;
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "TyriasGPS/1.9.0 (wiki-location-lookup)");
                    request.Headers.TryAddWithoutValidation("Accept", "application/json");

                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Logger.Warn("Wiki API request failed with status code " + (int)response.StatusCode + " for query: " + query);
                            return new List<string>();
                        }

                        var json = await response.Content.ReadAsStringAsync();

                        var root = _jsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (root == null || !root.TryGetValue("query", out var queryNode) || !(queryNode is Dictionary<string, object> queryData))
                        {
                            return new List<string>();
                        }

                        if (!queryData.TryGetValue("pages", out var pagesNode) || !(pagesNode is ArrayList pagesArray) || pagesArray.Count == 0)
                        {
                            return new List<string>();
                        }

                        if (!(pagesArray[0] is Dictionary<string, object> pageData) || !pageData.TryGetValue("revisions", out var revisionsNode) || !(revisionsNode is ArrayList revisionsArray) || revisionsArray.Count == 0)
                        {
                            return new List<string>();
                        }

                        if (!(revisionsArray[0] is Dictionary<string, object> revisionData) || !revisionData.TryGetValue("slots", out var slotsNode) || !(slotsNode is Dictionary<string, object> slotsData))
                        {
                            return new List<string>();
                        }

                        if (!slotsData.TryGetValue("main", out var mainSlotNode) || !(mainSlotNode is Dictionary<string, object> mainSlotData) || !mainSlotData.TryGetValue("content", out var contentNode))
                        {
                            return new List<string>();
                        }

                        var wikiText = contentNode as string;
                        if (string.IsNullOrWhiteSpace(wikiText))
                        {
                            return new List<string>();
                        }

                        var locationValues = new List<string>();
                        var locationMatches = Regex.Matches(wikiText, @"^\|\s*location\d*\s*=\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        foreach (Match match in locationMatches)
                        {
                            if (match.Groups.Count > 1)
                            {
                                locationValues.Add(match.Groups[1].Value);
                            }
                        }

                        var cleanedLocations = new List<string>();
                        foreach (var value in locationValues)
                        {
                            var cleanValue = value;
                            cleanValue = Regex.Replace(cleanValue, @"<ref[^>]*>.*?</ref>", string.Empty, RegexOptions.IgnoreCase);
                            cleanValue = Regex.Replace(cleanValue, @"<ref[^/]*/>", string.Empty, RegexOptions.IgnoreCase);
                            cleanValue = Regex.Replace(cleanValue, @"\[\[([^\]|]+)\|([^\]]+)\]\]", "$2");
                            cleanValue = Regex.Replace(cleanValue, @"\[\[([^\]]+)\]\]", "$1");
                            cleanValue = Regex.Replace(cleanValue, @"<br\s*/?>", ";", RegexOptions.IgnoreCase);
                            cleanValue = Regex.Replace(cleanValue, @"\{\{.*?\}\}", string.Empty);
                            cleanValue = Regex.Replace(cleanValue, @"<[^>]+>", string.Empty);

                            foreach (var token in cleanValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var trimmed = token.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    cleanedLocations.Add(trimmed);
                                }
                            }
                        }

                        return cleanedLocations
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read location values from wiki page data for query: " + query);
                return new List<string>();
            }
        }

        protected override void Unload()
        {
            if (_searchButton != null)
            {
                _searchButton.Click -= OnSearchButtonClick;
            }

            if (_searchTextBox != null)
            {
                _searchTextBox.EnterPressed -= OnSearchTextBoxEnterPressed;
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

            if (_searchWikiButton != null)
            {
                _searchWikiButton.Click -= OnSearchWikiButtonClick;
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
