using System.ComponentModel;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TyriasGPS.Controls
{
    internal class GpsActionButton : Control
    {
        private const float ANIM_DURATION = 0.12f;

        private static readonly Color _idleBackground = new Color(22, 30, 48, 230);
        private static readonly Color _hoverBackground = new Color(35, 55, 95, 245);
        private static readonly Color _pressBackground = new Color(52, 86, 140, 255);
        private static readonly Color _idleTextColor = new Color(230, 238, 250, 255);
        private static readonly Color _hoverTextColor = Color.White;
        private static readonly Color _borderColor = new Color(60, 95, 145, 190);

        private static readonly Color _accentIdleBackground = new Color(62, 40, 18, 232);
        private static readonly Color _accentHoverBackground = new Color(90, 57, 22, 248);
        private static readonly Color _accentPressBackground = new Color(122, 77, 29, 255);
        private static readonly Color _accentBorderColor = new Color(181, 119, 47, 195);

        private string _text;
        private Glide.Tween _hoverIn;
        private Glide.Tween _hoverOut;
        private Glide.Tween _pressOut;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public float HoverProgress { get; set; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public float PressProgress { get; set; }

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value, true);
        }

        public GpsActionButtonStyle Style { get; set; }

        protected override void OnMouseEntered(MouseEventArgs e)
        {
            _hoverOut?.Pause();
            _hoverIn = Animation.Tweener.Tween(this, new { HoverProgress = 1f }, ANIM_DURATION);
            base.OnMouseEntered(e);
        }

        protected override void OnMouseLeft(MouseEventArgs e)
        {
            _hoverIn?.Pause();
            _hoverOut = Animation.Tweener.Tween(this, new { HoverProgress = 0f }, ANIM_DURATION);
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
            bool accent = Style == GpsActionButtonStyle.Accent;

            Color idleBg = accent ? _accentIdleBackground : _idleBackground;
            Color hoverBg = accent ? _accentHoverBackground : _hoverBackground;
            Color pressBg = accent ? _accentPressBackground : _pressBackground;
            Color border = accent ? _accentBorderColor : _borderColor;

            Color bg = Color.Lerp(idleBg, hoverBg, t);
            bg = Color.Lerp(bg, pressBg, p);
            Color textColor = Color.Lerp(_idleTextColor, _hoverTextColor, t + p * 0.4f);

            int pressOffset = p > 0.01f ? 1 : 0;

            if (!Enabled)
            {
                bg *= 0.55f;
                textColor *= 0.65f;
            }

            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, bounds, bg);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel,
                new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), border);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel,
                new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), border);

            spriteBatch.DrawStringOnCtrl(this, Text ?? string.Empty,
                Content.DefaultFont14,
                new Rectangle(bounds.X, bounds.Y + pressOffset, bounds.Width, bounds.Height),
                textColor, false, true, 1, HorizontalAlignment.Center);
        }
    }
}
