using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;

namespace TyriasGPS
{
    public class TyriasGPSModule : Module
    {
        private SettingEntry<string> _locationSearch;
        private CornerIcon _cornerIcon;
        private StandardWindow _window;
        private TextBox _searchTextBox;
        private StandardButton _searchButton;
        private Label _resultLabel;
        private List<string> _mapNames = new List<string>();

        public TyriasGPSModule(ModuleParameters moduleParameters) : base(moduleParameters)
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

            _window = new StandardWindow(windowTexture, new Rectangle(40, 26, 280, 200), new Rectangle(50, 36, 260, 170))
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

        private void OnSearchButtonClick(object sender, MouseEventArgs e)
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
                return;

            var results = _mapNames.Where(m => m.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (results.Count == 0)
            {
                _resultLabel.Text = "No maps found matching the query.";
                return;
            }

            _resultLabel.Text = results.First();
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
