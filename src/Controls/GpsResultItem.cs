using System.ComponentModel;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TyriasGPS.Controls
{
    internal class GpsResultItem : Control
    {
        private const int ICON_SIZE = 18;
        private const int ICON_PADDING = 10;
        private const int TEXT_LEFT = ICON_PADDING + ICON_SIZE + 8;
        private const int ACCENT_WIDTH = 3;
        private const float ANIM_DURATION = 0.15f;

        private static readonly Color _idleBackground = new Color(22, 30, 48, 220);
        private static readonly Color _hoverBackground = new Color(35, 55, 95, 240);
        private static readonly Color _pressBackground = new Color(52, 86, 140, 255);
        private static readonly Color _accentColor = new Color(70, 150, 230, 255);
        private static readonly Color _idleTextColor = new Color(210, 218, 230, 255);
        private static readonly Color _hoverTextColor = new Color(240, 245, 255, 255);
        private static readonly Color _separatorColor = new Color(40, 55, 80, 120);

        private AsyncTexture2D _icon;
        private string _text;
        private Glide.Tween _animIn;
        private Glide.Tween _animOut;
        private Glide.Tween _pressOut;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public float HoverProgress { get; set; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public float PressProgress { get; set; }

        public AsyncTexture2D Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value, true);
        }

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value, true);
        }

        public GpsResultItem()
        {
            this.Size = new Point(452, 34);
        }

        protected override void OnMouseEntered(MouseEventArgs e)
        {
            _animOut?.Pause();
            _animIn = Animation.Tweener.Tween(this, new { HoverProgress = 1f }, ANIM_DURATION);
            base.OnMouseEntered(e);
        }

        protected override void OnMouseLeft(MouseEventArgs e)
        {
            _animIn?.Pause();
            _animOut = Animation.Tweener.Tween(this, new { HoverProgress = 0f }, ANIM_DURATION);
            base.OnMouseLeft(e);
        }

        protected override void OnClick(MouseEventArgs e)
        {
            PressProgress = 1f;
            _pressOut?.Pause();
            _pressOut = Animation.Tweener.Tween(this, new { PressProgress = 0f }, ANIM_DURATION);
            base.OnClick(e);
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            float t = HoverProgress;
            float p = PressProgress;

            // Background - smooth transition from dark navy to medium blue, flash on press
            Color bg = Color.Lerp(_idleBackground, _hoverBackground, t);
            bg = Color.Lerp(bg, _pressBackground, p);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, bounds, bg);

            // Left accent bar - fades in and grows on hover
            if (t > 0.01f)
            {
                int barWidth = (int)(ACCENT_WIDTH * t);
                if (barWidth > 0)
                {
                    Color barColor = _accentColor * t;
                    spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel,
                        new Rectangle(bounds.X, bounds.Y + 2, barWidth, bounds.Height - 4), barColor);
                }
            }

            // Bottom separator
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel,
                new Rectangle(bounds.X + ICON_PADDING, bounds.Bottom - 1, bounds.Width - ICON_PADDING * 2, 1), _separatorColor);

            // Icon
            if (_icon != null)
            {
                int iconLeft = bounds.X + ICON_PADDING + (int)(2 * t);
                spriteBatch.DrawOnCtrl(this, _icon,
                    new Rectangle(iconLeft, bounds.Y + (bounds.Height - ICON_SIZE) / 2, ICON_SIZE, ICON_SIZE));
            }

            // Text
            int textLeft = bounds.X + TEXT_LEFT + (int)(2 * t);
            Color textColor = Color.Lerp(_idleTextColor, _hoverTextColor, t);
            spriteBatch.DrawStringOnCtrl(this, _text ?? string.Empty,
                Content.DefaultFont14,
                new Rectangle(textLeft, bounds.Y, bounds.Width - textLeft - 8, bounds.Height),
                textColor,
                false, true);
        }
    }
}
