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
        private readonly Gw2Client _publicGw2Client = new Gw2Client();
        private SettingEntry<string> _locationSearch;
        private CornerIcon _cornerIcon;
        private StandardWindow _window;
        private TextBox _searchTextBox;
        private StandardButton _searchButton;
        private Label _resultLabel;

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
                var allMaps = await _publicGw2Client.WebApi.V2.Maps.AllAsync();
                var maps = allMaps.ToList();
                LogHelper.Log($"Fetched {maps.Count} maps from the public API.");

                var selectedMap = maps
                    .Where(m => !string.IsNullOrWhiteSpace(m.Name)
                                && m.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(m => m.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(m => m.Name)
                    .FirstOrDefault();

                if (selectedMap == null)
                {
                    _resultLabel.Text = "No maps found matching the query.";
                    LogHelper.Log("No maps matched query: " + query);
                    return;
                }

                LogHelper.Log($"Using map '{selectedMap.Name}' (Id={selectedMap.Id}, Region={selectedMap.RegionId}, Floor={selectedMap.DefaultFloor}).");

                var floor = await _publicGw2Client.WebApi.V2.Continents[selectedMap.ContinentId].Floors[selectedMap.DefaultFloor].GetAsync();
                var map = floor.Regions.Values
                    .SelectMany(region => region.Maps.Values)
                    .FirstOrDefault(m => m.Id == selectedMap.Id);

                var waypoints = (map?.PointsOfInterest?.Values ?? Enumerable.Empty<ContinentFloorRegionMapPoi>())
                    .Where(poi => poi.Type.ToString().Equals("Waypoint", StringComparison.OrdinalIgnoreCase)
                                  && !string.IsNullOrWhiteSpace(poi.Name)
                                  && !string.IsNullOrWhiteSpace(poi.ChatLink))
                    .ToList();

                LogHelper.Log($"Fetched {waypoints.Count} POIs for map '{selectedMap.Name}'.");

                if (waypoints.Count == 0)
                {
                    _resultLabel.Text = $"No waypoints found on map '{selectedMap.Name}'.";
                    LogHelper.Log($"Search completed for query: {query}, found 0 waypoints on map {selectedMap.Name}");
                    return;
                }

                var topWaypoint = waypoints.First();
                _resultLabel.Text = $"{selectedMap.Name}: {topWaypoint.Name} {topWaypoint.ChatLink}";
                LogHelper.Log($"Search completed for query: {query}, found {waypoints.Count} waypoints on map {selectedMap.Name}");
            }
            catch (Exception ex)
            {
                _resultLabel.Text = "Search failed. Check logs for details.";
                LogHelper.LogException(ex, "Search request failed");
            }
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
