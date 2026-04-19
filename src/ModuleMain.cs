using System;
using System.ComponentModel.Composition;
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
    [Export(typeof(Module))]
    public class TyriasGPSModule : Module
    {
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
        }

        protected override void Initialize()
        {
        }

        protected override Task LoadAsync()
        {
            LogHelper.Log("Module loaded.");

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

        private void OnSearchButtonClick(object sender, MouseEventArgs e)
        {
            string query = _searchTextBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
                return;

            _resultLabel.Text = "No maps found matching the query.";
        }

        protected override void Unload()
        {
            _searchButton.Click -= OnSearchButtonClick;
            _window?.Dispose();
        }
    }
}
