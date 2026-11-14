using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The novel's screen (Р12), built in code by <see cref="DialogueView"/>: over the world, the location's backdrop as a
    /// dreamy picture (blurred, more colourful, without its figures and objects), the partner's large picture in the middle
    /// and a half-transparent panel at the bottom with the speaker's name and the line or the hero's options.
    /// Coming in: the dream fades in with a soft glow, then the partner comes out of the shadow — a dark silhouette appears
    /// and the light comes onto her. Going out: she goes back into the shadow — darkens to a silhouette and melts away —
    /// then the dream fades back to the world. When her emotion changes, the new picture fades in over the old one while
    /// the old one fades out (<see cref="CrossfadeSeconds"/>). The fades run on unscaled time (the world is paused).
    /// Sizes are canvas units = Full HD pixels.
    /// </summary>
    internal sealed class NovelScreen
    {
        public const float FadeInSeconds = 1.4f;
        public const float FadeOutSeconds = 1.1f;

        /// <summary>How long one picture of the partner takes to change into another (a new emotion).</summary>
        public const float CrossfadeSeconds = 0.5f;

        /// <summary>The longest step one frame moves the fades.</summary>
        private const float MaxStep = 1f / 30f;

        // Coming in (seconds from the start): the dream, then her silhouette, then the light on her.
        private const float DreamIn = 0.8f;
        private const float SilhouetteFrom = 0.4f, SilhouetteIn = 0.5f;
        private const float LightFrom = 0.7f, LightIn = 0.7f;

        // Going out: the light leaves her, the silhouette melts, then the dream goes.
        private const float DarkOut = 0.4f;
        private const float MeltFrom = 0.3f, MeltOut = 0.4f;
        private const float DreamFrom = 0.5f, DreamOut = 0.6f;

        private const float PanelSideMargin = 200f;
        private const float PanelBottom = 28f;
        private const float PanelHeight = 250f;
        private const float PanelPadding = 26f;

        private static readonly Color PanelColour = new Color32(16, 12, 30, 200);
        private static readonly Color NameColour = new Color32(255, 214, 150, 255);
        private static readonly Color TextColour = new Color32(244, 238, 255, 255);
        private static readonly Color GlowColour = new Color32(255, 226, 246, 255);

        private readonly GameObject _root;
        private readonly CanvasGroup _group;
        private readonly RawImage _background;
        private readonly Image _picture;
        private readonly Image _previous;  // the picture being replaced, under the new one while they cross-fade
        private readonly Image _glow;
        private float _time;            // seconds since the fade started
        private int _direction;         // 1 coming in, -1 going out, 0 still
        private float _change;          // seconds since the picture changed; CrossfadeSeconds or more: no change running
        private Texture2D _dreamy;

        public NovelScreen(Transform parent, TMP_FontAsset font)
        {
            _root = new GameObject("Novel", typeof(RectTransform));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            _group = _root.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _background = new GameObject("Dream", typeof(RectTransform)).AddComponent<RawImage>();
            _background.rectTransform.SetParent(rect, false);
            Stretch(_background.rectTransform);
            _background.raycastTarget = false;

            _previous = PartnerImage(rect, "Partner (previous)");
            _previous.enabled = false;
            _picture = PartnerImage(rect, "Partner");
            _change = CrossfadeSeconds;

            var panel = new GameObject("Panel", typeof(RectTransform)).AddComponent<Image>();
            panel.rectTransform.SetParent(rect, false);
            panel.rectTransform.anchorMin = new Vector2(0f, 0f);
            panel.rectTransform.anchorMax = new Vector2(1f, 0f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0f);
            panel.rectTransform.offsetMin = new Vector2(PanelSideMargin, PanelBottom);
            panel.rectTransform.offsetMax = new Vector2(-PanelSideMargin, PanelBottom + PanelHeight);
            panel.color = PanelColour;
            panel.raycastTarget = false;

            Name = Label(panel.rectTransform, "Name", font, 30f, FontStyles.Bold, NameColour);
            Name.rectTransform.offsetMin = new Vector2(PanelPadding, PanelHeight - PanelPadding - 40f);
            Name.rectTransform.offsetMax = new Vector2(-PanelPadding, -PanelPadding);

            Text = Label(panel.rectTransform, "Text", font, 30f, FontStyles.Normal, TextColour);
            Text.rectTransform.offsetMin = new Vector2(PanelPadding, PanelPadding);
            Text.rectTransform.offsetMax = new Vector2(-PanelPadding, -PanelPadding - 50f);
            Text.textWrappingMode = TextWrappingModes.Normal;

            _glow = new GameObject("Glow", typeof(RectTransform)).AddComponent<Image>();
            _glow.rectTransform.SetParent(rect, false);
            Stretch(_glow.rectTransform);
            _glow.color = new Color(GlowColour.r, GlowColour.g, GlowColour.b, 0f);
            _glow.raycastTarget = false;

            _root.SetActive(false);
        }

        /// <summary>The novel is on screen (or fading in); false from the moment it starts fading out.</summary>
        public bool IsShown => _direction > 0 || (_direction == 0 && _root.activeSelf);

        public TextMeshProUGUI Name { get; }

        /// <summary>The line or the options; a line is typed letter by letter by <see cref="DialogueView"/>.</summary>
        public TextMeshProUGUI Text { get; }

        public Sprite Picture => _picture.sprite;

        /// <summary>The partner's tint: black and transparent in the shadow, white and opaque in the light.</summary>
        public Color PictureColour => _picture.color;

        /// <summary>The picture fading out under the new one while her emotion changes; null when no change is running.</summary>
        public Sprite PreviousPicture => _previous.enabled ? _previous.sprite : null;

        public Texture Dream => _background.texture;

        /// <summary>Opens the novel over the world with the given dreamy backdrop (owned by this screen from now).</summary>
        public void Enter(Texture2D dreamy)
        {
            Forget();
            _dreamy = dreamy;
            _background.texture = dreamy;
            // A new novel starts from no picture: its first one comes out of the shadow, not cross-fading from the last novel's.
            _picture.sprite = null;
            _picture.enabled = false;
            EndChange();
            _root.SetActive(true);
            _time = 0f;
            _direction = 1;
            Apply();
        }

        /// <summary>Back to the world: the novel fades out. Nothing happens when it is not shown.</summary>
        public void Leave()
        {
            if (IsShown)
            {
                _time = 0f;
                _direction = -1;
                Apply();
            }
        }

        /// <summary>
        /// Shows the partner's picture. A different picture while one is already shown (her emotion changed) cross-fades:
        /// the old one stays under the new one and fades out as the new one fades in. The same picture changes nothing.
        /// </summary>
        public void ShowPicture(Sprite picture)
        {
            if (picture == _picture.sprite)
            {
                return;
            }

            if (_picture.sprite != null && picture != null)
            {
                _previous.sprite = _picture.sprite;
                _previous.enabled = true;
                _change = 0f;
            }
            else
            {
                EndChange();
            }

            _picture.sprite = picture;
            _picture.enabled = picture != null;
            Apply();
        }

        public void Tick(float unscaledDelta)
        {
            // A long frame (the dream is captured and blurred in the frame the novel opens) must not skip a fade: at most
            // a thirtieth of a second counts per frame, so the player always sees her come out of the shadow.
            var step = Mathf.Min(unscaledDelta, MaxStep);
            if (_change < CrossfadeSeconds)
            {
                _change += step;
                if (_change >= CrossfadeSeconds)
                {
                    EndChange();
                }

                Apply();
            }

            if (_direction == 0)
            {
                return;
            }

            _time += step;
            Apply();
            if (_direction > 0 && _time >= FadeInSeconds)
            {
                _direction = 0;
                Apply();
            }
            else if (_direction < 0 && _time >= FadeOutSeconds)
            {
                _direction = 0;
                _root.SetActive(false);
                Forget();
            }
        }

        /// <summary>
        /// The dream's fade with its "fairy" glow (a soft bright veil that rises and falls while it comes in), and the
        /// partner's shadow: her picture tinted from black (a silhouette) to its own colours, and its transparency.
        /// </summary>
        private void Apply()
        {
            float dream, silhouette, light, glow = 0f;
            if (_direction > 0)
            {
                dream = Phase(_time, 0f, DreamIn);
                glow = Mathf.Sin(dream * Mathf.PI) * 0.65f;
                silhouette = Phase(_time, SilhouetteFrom, SilhouetteIn);
                light = Phase(_time, LightFrom, LightIn);
            }
            else if (_direction < 0)
            {
                light = 1f - Phase(_time, 0f, DarkOut);
                silhouette = 1f - Phase(_time, MeltFrom, MeltOut);
                dream = 1f - Phase(_time, DreamFrom, DreamOut);
            }
            else
            {
                dream = silhouette = light = 1f; // standing still on screen
            }

            _group.alpha = dream;
            _glow.color = new Color(GlowColour.r, GlowColour.g, GlowColour.b, glow);
            // A change of emotion: the new picture rises over the old one and the old one fades a little later (t squared
            // behind), so the two together never let the dream show through her.
            var change = Phase(_change, 0f, CrossfadeSeconds);
            _picture.color = new Color(light, light, light, silhouette * change);
            _previous.color = new Color(light, light, light, silhouette * (1f - change * change));
        }

        private void EndChange()
        {
            _change = CrossfadeSeconds;
            _previous.enabled = false;
            _previous.sprite = null;
        }

        private static Image PartnerImage(RectTransform parent, string name)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            image.rectTransform.pivot = new Vector2(0.5f, 0f);
            image.rectTransform.sizeDelta = new Vector2(DialogueView.NovelPictureWidth, DialogueView.NovelPictureHeight);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>0 before <paramref name="from"/>, 1 after <paramref name="from"/> + <paramref name="length"/>, smooth between.</summary>
        private static float Phase(float time, float from, float length) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - from) / length));

        private void Forget()
        {
            if (_dreamy != null)
            {
                Object.Destroy(_dreamy);
                _dreamy = null;
            }

            _background.texture = null;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, TMP_FontAsset font, float size, FontStyles style, Color colour)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.rectTransform.SetParent(parent, false);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
