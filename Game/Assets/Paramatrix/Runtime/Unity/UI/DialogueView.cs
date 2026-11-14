using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Shows conversations and monologues (Architecture 0.3 §2.2, Р10).
    /// Conversation (normal mode): the speaker's avatar with the line's emotion and a comic bubble stand with their bottoms
    /// on the location's conversation line; the avatar is over the speaker and looks where the figure looks; the bubble is
    /// on that side and is shifted inside the screen at an edge, never moved to the other side. The text is typed letter
    /// by letter; advancing while it types shows it whole (<see cref="InputRouter"/>).
    /// Monologue: a bubble over the speaker's head, no avatar, no name; one per speaker; it goes out after
    /// <see cref="MonologueSeconds"/> or when the speaker says the next line.
    /// Choice (Р12): the numbered options "1. [Да]" in the hero's bubble by his avatar (normal mode) or in the novel's panel;
    /// an option is taken by its number key or a click on it (<see cref="ChoiceAt"/>, <see cref="InputRouter"/>).
    /// Novel (Р12): <see cref="NovelScreen"/> over the world, its backdrop taken from the location without its figures.
    /// The UI is built in code at <see cref="Bind"/>; sizes are in grid pixels, two canvas units each (1920×1080 canvas),
    /// except the avatar: a picture in screen pixels, one texel per canvas unit, all the same size (<see cref="AvatarWidth"/>).
    /// </summary>
    [DefaultExecutionOrder(1100)] // after WorldCamera (1000): bubbles are placed by this frame's camera, never the last one
    public sealed class DialogueView : MonoBehaviour, IDialogueView
    {
        /// <summary>How long a monologue bubble stays, seconds, until the settings change it (Р10).</summary>
        public const float DefaultMonologueSeconds = 7f;

        private float _monologueSeconds = DefaultMonologueSeconds;

        /// <summary>
        /// How long a monologue bubble stays, seconds. A player setting: the settings screen (not made yet) sets it;
        /// a new value applies to bubbles shown after it.
        /// </summary>
        public float MonologueSeconds
        {
            get => _monologueSeconds;
            set => _monologueSeconds = value > 0f ? value : throw new System.ArgumentOutOfRangeException(nameof(value), value, "A monologue must stay for some time.");
        }

        public const float LettersPerSecond = 40f;

        /// <summary>
        /// Every avatar of every character is exactly this size, Full HD screen pixels = canvas units (Р10, test size:
        /// half the hero's 229×270 card cut). An avatar is a picture in screen pixels, not on the world grid: one texel per
        /// canvas unit.
        /// </summary>
        public const int AvatarWidth = 115;

        /// <inheritdoc cref="AvatarWidth"/>
        public const int AvatarHeight = 135;

        /// <summary>The height an avatar takes above the conversation line, in grid pixels (rounded up).</summary>
        public const int AvatarHeightGridPixels = (AvatarHeight + 1) / 2;

        /// <summary>Every novel picture is exactly this size, Full HD screen pixels (Р12): the partner in the middle of the screen.</summary>
        public const int NovelPictureWidth = 630;

        /// <inheritdoc cref="NovelPictureWidth"/>
        public const int NovelPictureHeight = 880;

        private const float CanvasUnitsPerGridPixel = 2f;
        private const int MaxTextWidth = 300;   // grid pixels
        private const int Padding = 7;          // grid pixels between the outline and the text
        private const int AvatarGap = 4;        // grid pixels between the avatar and the bubble
        private const int HeadGap = 6;          // grid pixels between a head and a speech bubble over it
        private const int ThoughtGap = BubbleArt.TrailHeight + 2; // a thought's trail of circles fits above the head
        private const int ScreenMargin = 4;     // grid pixels kept free at the screen edges
        private const float FontSize = 26f;     // canvas units

        [SerializeField]
        private Camera worldCamera;

        private RepresentationRegistry _representations;
        private IDiagnosticsSink _diagnostics;
        private ContentManifest _manifest;
        private TMP_FontAsset _font;
        private BubbleArt _art;

        private Bubble _conversation;
        private Image _avatar;
        private NovelScreen _novel;
        private DialogueLineView? _line;
        private DialogueChoiceView _choices;
        private EntityId? _speaker;     // normal mode: whose avatar and bubble stand on the line (the hero for a choice)
        private TMP_Text _typing;       // the text being typed: the bubble's or the novel panel's
        private float _typed;
        private bool _placementReported;

        // By speaker ID (ordinal): never enumerated for behaviour that depends on order.
        private readonly SortedDictionary<string, Monologue> _monologues = new SortedDictionary<string, Monologue>(System.StringComparer.Ordinal);

        public DialogueLineView? CurrentLine => _line;

        /// <summary>The hero's choice on screen, or null.</summary>
        public DialogueChoiceView CurrentChoices => _choices;

        public bool IsConversationShown => _conversation != null && _conversation.Root.gameObject.activeSelf;

        /// <summary>The whole text of the conversation line, typed or not.</summary>
        public string ConversationText => _conversation.Text.text;

        public bool IsTyping => _typing != null && _typing.maxVisibleCharacters < _typing.text.Length;

        public int VisibleLetters => (_typing ?? _conversation.Text).maxVisibleCharacters;

        /// <summary>The novel's screen is up (or fading in).</summary>
        public bool IsNovelShown => _novel != null && _novel.IsShown;

        public Sprite NovelPicture => _novel.Picture;

        /// <summary>The partner's picture fading out while her emotion changes (null when no change is running).</summary>
        public Sprite NovelPreviousPicture => _novel.PreviousPicture;

        /// <summary>The partner's tint (shadow → light, Р12).</summary>
        public Color NovelPictureColour => _novel.PictureColour;

        public string NovelName => _novel.Name.text;

        /// <summary>The novel panel's whole text: the line, or the options.</summary>
        public string NovelText => _novel.Text.text;

        /// <summary>The novel's dreamy backdrop (null when no novel is up).</summary>
        public Texture NovelDream => _novel.Dream;

        public Sprite AvatarSprite => _avatar.sprite;

        /// <summary>The avatar is mirrored: the speaker looks left.</summary>
        public bool AvatarLooksLeft => _avatar.rectTransform.localScale.x < 0f;

        public Rect AvatarScreenRect => ScreenRect(_avatar.rectTransform);

        public Rect ConversationScreenRect => ScreenRect(_conversation.Root);

        public int MonologueCount => _monologues.Count;

        public void Bind(RepresentationRegistry representations, IDiagnosticsSink diagnostics, TMP_FontAsset font, ContentManifest manifest)
        {
            _representations = representations;
            _diagnostics = diagnostics;
            _font = font;
            _manifest = manifest;
            _art = new BubbleArt();

            _avatar = new GameObject("Avatar", typeof(RectTransform)).AddComponent<Image>();
            _avatar.rectTransform.SetParent(transform, false);
            _avatar.rectTransform.anchorMin = _avatar.rectTransform.anchorMax = Vector2.zero;
            _avatar.rectTransform.pivot = new Vector2(0.5f, 0f);
            _avatar.raycastTarget = false;
            _avatar.gameObject.SetActive(false);

            _conversation = new Bubble(transform, "Conversation", _art, _font, thought: false);
            _novel = new NovelScreen(transform, _font);
            Hide();
        }

        // ---------- conversation ----------

        public void Show(DialogueLineView line)
        {
            _line = line;
            _choices = null;
            if (line.Mode == DialogueMode.Novel)
            {
                HideConversation();
                EnterNovel(line.Partner.Value, line.PartnerEmotion);
                _novel.Name.text = line.SpeakerName;
                StartTyping(_novel.Text, line.Text);
                return;
            }

            _novel.Leave();
            ShowConversation(line.Speaker, line.Emotion, line.Text);
            StartTyping(_conversation.Text, null);
        }

        public void ShowChoices(DialogueChoiceView choices)
        {
            _line = null;
            _choices = choices;
            _typing = null;
            var text = OptionsText(choices.Labels);
            if (choices.Mode == DialogueMode.Novel)
            {
                HideConversation();
                EnterNovel(choices.Partner.Value, choices.PartnerEmotion);
                _novel.Name.text = choices.SpeakerName;
                _novel.Text.text = text;
                _novel.Text.maxVisibleCharacters = int.MaxValue;
                return;
            }

            _novel.Leave();
            ShowConversation(choices.Speaker, Emotions.Default, text);
            _conversation.Text.maxVisibleCharacters = int.MaxValue;
        }

        public void Hide()
        {
            _line = null;
            _choices = null;
            _typing = null;
            HideConversation();
            _novel.Leave();
        }

        /// <summary>Shows the whole line at once (the player advanced while it was typing).</summary>
        public void CompleteTyping()
        {
            if (_typing != null)
            {
                _typed = _typing.text.Length;
                _typing.maxVisibleCharacters = _typing.text.Length;
            }
        }

        /// <summary>The option under a screen point (a click), from 0; -1 when there is no choice or no option there.</summary>
        public int ChoiceAt(Vector2 screen)
        {
            if (_choices == null)
            {
                return -1;
            }

            var text = _choices.Mode == DialogueMode.Novel ? _novel.Text : _conversation.Text;
            var link = TMP_TextUtilities.FindIntersectingLink(text, screen, null); // overlay canvas: no camera
            return link < 0 ? -1 : int.Parse(text.textInfo.linkInfo[link].GetLinkID(), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The options one per line, "1. [Да]", each a link that a click finds (<see cref="ChoiceAt"/>).</summary>
        private static string OptionsText(IReadOnlyList<string> labels)
        {
            var lines = new List<string>(labels.Count);
            for (var i = 0; i < labels.Count; i++)
            {
                lines.Add($"<link=\"{i}\">{DialogueChoiceView.Format(i, labels[i])}</link>");
            }

            return string.Join("\n", lines);
        }

        private void ShowConversation(EntityId speaker, string emotion, string text)
        {
            _speaker = speaker;
            _placementReported = false;
            _conversation.Root.gameObject.SetActive(true);
            _conversation.SetText(text);

            var sprite = _manifest.FindAvatar(speaker.Value, emotion);
            if (sprite == null)
            {
                Report(DiagnosticCode.AvatarMissing, $"{speaker} has no avatar '{emotion}': run Paramatrix → Collect Content, or draw it.");
            }

            _avatar.sprite = sprite;
            _avatar.gameObject.SetActive(sprite != null);
            if (sprite != null)
            {
                _avatar.rectTransform.sizeDelta = sprite.rect.size; // screen pixels at Full HD (AvatarWidth x AvatarHeight)
            }

            PlaceConversation();
        }

        private void HideConversation()
        {
            _speaker = null;
            _conversation.Root.gameObject.SetActive(false);
            _avatar.gameObject.SetActive(false);
        }

        /// <summary>Types <paramref name="text"/> letter by letter; null keeps the text already set.</summary>
        private void StartTyping(TMP_Text target, string text)
        {
            if (text != null)
            {
                target.text = text;
            }

            _typing = target;
            _typed = 0f;
            target.maxVisibleCharacters = 0;
        }

        /// <summary>Opens the novel's screen once per novel, and shows the partner's picture by the emotion.</summary>
        private void EnterNovel(EntityId partner, string emotion)
        {
            if (!_novel.IsShown)
            {
                _novel.Enter(CaptureDream());
            }

            var picture = _manifest.FindNovelPicture(partner.Value, emotion);
            if (picture == null)
            {
                Report(DiagnosticCode.NovelPictureMissing, $"{partner} has no novel picture '{emotion}': run Paramatrix → Collect Content, or draw it.");
            }

            _novel.ShowPicture(picture);
        }

        /// <summary>
        /// The location as the world camera sees it now, without its figures, objects and near layers (only the backdrop
        /// and far layers stay), made dreamy (<see cref="Dream"/>).
        /// </summary>
        private Texture2D CaptureDream()
        {
            var hidden = new List<Renderer>();
            void Hide(Component owner)
            {
                foreach (var renderer in owner.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        hidden.Add(renderer);
                    }
                }
            }

            foreach (var view in _representations.AllViews)
            {
                Hide(view);
            }

            foreach (var layer in FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None))
            {
                if (layer.Follow < 0f)
                {
                    Hide(layer);
                }
            }

            var target = RenderTexture.GetTemporary(PixelGrid.Width, PixelGrid.Height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = worldCamera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                worldCamera.targetTexture = target;
                worldCamera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(PixelGrid.Width, PixelGrid.Height, TextureFormat.RGBA32, false);
                picture.ReadPixels(new Rect(0, 0, PixelGrid.Width, PixelGrid.Height), 0, 0);
                picture.Apply(false);
                var dream = Dream.Make(picture.GetPixels32(), PixelGrid.Width, PixelGrid.Height);
                Destroy(picture);
                return dream;
            }
            finally
            {
                worldCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                foreach (var renderer in hidden)
                {
                    renderer.enabled = true;
                }
            }
        }

        // ---------- monologues ----------

        public void Say(EntityId speaker, string text)
        {
            var view = SpeakerView(speaker);
            if (!_monologues.TryGetValue(speaker.Value, out var monologue))
            {
                // The hero's monologue is his thought: a trail of circles. Anyone else speaks into the air: a speech tail.
                var thought = view is CharacterView character && character.IsPlayer;
                monologue = new Monologue(speaker, new Bubble(transform, (thought ? "Thought " : "Monologue ") + speaker.Value, _art, _font, thought));
                _monologues.Add(speaker.Value, monologue);
            }

            if (view != null)
            {
                // Measured once: walking frames and turning change what is drawn, the bubble must not jump with them.
                monologue.HeadHeight = view.TopY - view.transform.position.y;
            }

            monologue.Bubble.SetText(text);
            monologue.Bubble.Text.maxVisibleCharacters = int.MaxValue;
            monologue.Remaining = MonologueSeconds;
            if (!PlaceMonologue(monologue))
            {
                // The logic checked the speaker is in the location; no shown figure here is a scene defect (Р0).
                Report(DiagnosticCode.MonologueFailed, $"{speaker} says a monologue but has no shown representation in the scene.");
                Remove(monologue);
            }
        }

        public void ClearMonologues()
        {
            foreach (var monologue in new List<Monologue>(_monologues.Values))
            {
                Remove(monologue);
            }
        }

        public bool IsMonologueShown(EntityId speaker) => _monologues.ContainsKey(speaker.Value);

        public void HideMonologue(EntityId speaker)
        {
            if (_monologues.TryGetValue(speaker.Value, out var monologue))
            {
                Remove(monologue);
            }
        }

        /// <summary>The text of a speaker's monologue bubble, or null when it has none.</summary>
        public string MonologueText(EntityId speaker) => _monologues.TryGetValue(speaker.Value, out var m) ? m.Bubble.Text.text : null;

        public Rect MonologueScreenRect(EntityId speaker) => ScreenRect(_monologues[speaker.Value].Bubble.Root);

        /// <summary>The speaker's monologue is a thought (the hero): a trail of circles, not a speech tail.</summary>
        public bool MonologueIsThought(EntityId speaker) => _monologues[speaker.Value].Bubble.IsThought;

        // ---------- frame ----------

        private void Update()
        {
            if (_typing != null && _typed < _typing.text.Length)
            {
                _typed += Time.unscaledDeltaTime * LettersPerSecond; // the world is paused during a conversation
                _typing.maxVisibleCharacters = Mathf.Min(Mathf.FloorToInt(_typed), _typing.text.Length);
            }

            _novel?.Tick(Time.unscaledDeltaTime);

            if (_monologues.Count == 0)
            {
                return;
            }

            var expired = new List<Monologue>();
            foreach (var monologue in _monologues.Values)
            {
                monologue.Remaining -= Time.deltaTime;
                if (monologue.Remaining <= 0f)
                {
                    expired.Add(monologue);
                }
            }

            foreach (var monologue in expired)
            {
                Remove(monologue);
            }
        }

        private void LateUpdate()
        {
            // Figures and the camera have moved: bubbles follow them.
            if (_speaker.HasValue)
            {
                PlaceConversation();
            }

            var gone = new List<Monologue>();
            foreach (var monologue in _monologues.Values)
            {
                if (!PlaceMonologue(monologue))
                {
                    gone.Add(monologue); // the speaker is no longer shown (left, location unloading)
                }
            }

            foreach (var monologue in gone)
            {
                Remove(monologue);
            }
        }

        private void OnDestroy() => _art?.Dispose();

        // ---------- layout ----------

        private void PlaceConversation()
        {
            var view = SpeakerView(_speaker.Value);
            var root = _representations.SceneRoot;
            if (view == null || root == null || root.DialogueLine <= 0)
            {
                if (!_placementReported)
                {
                    _placementReported = true; // once per line, not every frame
                    Report(view == null ? DiagnosticCode.UnknownReference : DiagnosticCode.DialogueLineMissing, view == null
                        ? $"{_speaker} speaks in a conversation but has no shown representation in the scene."
                        : "The location has no conversation line (LocationRoot.dialogueLine); run Paramatrix → Validate Project.");
                }

                return;
            }

            // Bubble and avatar stand side by side on the line, the gap between them over the speaker's position: the bubble
            // on the side the speaker looks at, its tail by the speaker; the avatar behind his head, looking where he looks
            // (sprites are drawn looking right). At a screen edge both shift inside together; the bubble never flips.
            var bottom = worldCamera.transform.position.y - worldCamera.orthographicSize;
            var anchor = (Vector2)worldCamera.WorldToScreenPoint(new Vector3(view.transform.position.x, bottom + PixelGrid.Units(root.DialogueLine), 0f));
            var scale = ScreenPerCanvasUnit;
            var looksLeft = view is CharacterView character && character.Walker.Facing == Facing.Left;
            var bubble = _conversation.Root;
            var width = bubble.rect.width * scale;
            var avatarWidth = _avatar.gameObject.activeSelf ? _avatar.rectTransform.rect.width * scale : 0f;
            var margin = ScreenMargin * CanvasUnitsPerGridPixel * scale;
            var halfGap = AvatarGap * CanvasUnitsPerGridPixel * scale / 2f;

            float bubbleLeft, avatarLeft;
            if (looksLeft)
            {
                bubbleLeft = anchor.x - halfGap - width;
                avatarLeft = anchor.x + halfGap;
            }
            else
            {
                avatarLeft = anchor.x - halfGap - avatarWidth;
                bubbleLeft = anchor.x + halfGap;
            }

            var groupLeft = Mathf.Min(bubbleLeft, avatarLeft);
            var groupRight = Mathf.Max(bubbleLeft + width, avatarLeft + avatarWidth);
            var shift = groupLeft < margin ? margin - groupLeft : groupRight > Screen.width - margin ? Screen.width - margin - groupRight : 0f;
            bubbleLeft = Mathf.Round(bubbleLeft + shift);
            avatarLeft = Mathf.Round(avatarLeft + shift);

            _avatar.rectTransform.position = new Vector2(avatarLeft + (avatarWidth / 2f), Mathf.Round(anchor.y)); // pivot: bottom middle
            _avatar.rectTransform.localScale = new Vector3(looksLeft ? -1f : 1f, 1f, 1f);
            bubble.pivot = Vector2.zero;
            bubble.position = new Vector2(bubbleLeft, Mathf.Round(anchor.y));
            _conversation.PointTail(looksLeft ? TailSide.Right : TailSide.Left);
        }

        /// <summary>False when the speaker has no shown figure.</summary>
        private bool PlaceMonologue(Monologue monologue)
        {
            var view = SpeakerView(monologue.Speaker);
            if (view == null)
            {
                return false;
            }

            // Over the figure's feet point (its position: whole grid pixels, the same whichever way it looks) at the head
            // height measured when the line was said, so walking frames and turning do not shake the bubble.
            var scale = ScreenPerCanvasUnit;
            var feet = view.transform.position;
            var anchor = (Vector2)worldCamera.WorldToScreenPoint(new Vector3(feet.x, feet.y + monologue.HeadHeight, 0f));
            var bubble = monologue.Bubble.Root;
            var width = bubble.rect.width * scale;
            var height = bubble.rect.height * scale;
            var margin = ScreenMargin * CanvasUnitsPerGridPixel * scale;
            var gap = (monologue.Bubble.IsThought ? ThoughtGap : HeadGap) * CanvasUnitsPerGridPixel * scale;
            var left = Mathf.Clamp(anchor.x - (width / 2f), margin, Mathf.Max(margin, Screen.width - margin - width));
            var y = Mathf.Min(anchor.y + gap, Screen.height - margin - height);
            bubble.pivot = Vector2.zero;
            bubble.position = new Vector2(Mathf.Round(left), Mathf.Round(y)); // whole screen pixels: no shimmer
            monologue.Bubble.PointTail(TailSide.Center);
            return true;
        }

        private void Remove(Monologue monologue)
        {
            _monologues.Remove(monologue.Speaker.Value);
            Destroy(monologue.Bubble.Root.gameObject);
        }

        private float ScreenPerCanvasUnit => transform.lossyScale.x;

        private EntityView SpeakerView(EntityId speaker)
        {
            var view = _representations?.Find(speaker);
            return view != null && view.IsShown ? view : null;
        }

        private void Report(DiagnosticCode code, string message) =>
            _diagnostics.Report(new Diagnostic(code, Severity.Error, message, SourceLocation.None));

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var minX = Mathf.Min(corners[0].x, corners[2].x);
            var maxX = Mathf.Max(corners[0].x, corners[2].x);
            return Rect.MinMaxRect(minX, corners[0].y, maxX, corners[2].y);
        }

        // ---------- pieces ----------

        private enum TailSide
        {
            Left,
            Center,
            Right,
        }

        private sealed class Monologue
        {
            public Monologue(EntityId speaker, Bubble bubble)
            {
                Speaker = speaker;
                Bubble = bubble;
            }

            public EntityId Speaker { get; }

            public Bubble Bubble { get; }

            public float Remaining { get; set; }

            /// <summary>Head top over the figure's position, world units, measured when the line was said.</summary>
            public float HeadHeight { get; set; }
        }

        /// <summary>A comic bubble sized to its text: up to <see cref="MaxTextWidth"/> wide, then the text wraps.</summary>
        private sealed class Bubble
        {
            private readonly RectTransform _tail;

            public Bubble(Transform parent, string name, BubbleArt art, TMP_FontAsset font, bool thought)
            {
                IsThought = thought;
                var body = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
                body.sprite = art.Body; // thought or speech: the same body, only what hangs under it differs
                body.type = Image.Type.Sliced;
                body.pixelsPerUnitMultiplier = 1f / CanvasUnitsPerGridPixel; // one texel = one grid pixel
                body.raycastTarget = false;
                Root = body.rectTransform;
                Root.SetParent(parent, false);
                Root.anchorMin = Root.anchorMax = Vector2.zero;

                var tail = new GameObject("Tail", typeof(RectTransform)).AddComponent<Image>();
                tail.sprite = thought ? art.Trail : art.Tail;
                tail.raycastTarget = false;
                _tail = tail.rectTransform;
                _tail.SetParent(Root, false);
                _tail.anchorMin = _tail.anchorMax = Vector2.zero;
                _tail.pivot = thought ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
                _tail.sizeDelta = thought
                    ? new Vector2(BubbleArt.TrailWidth, BubbleArt.TrailHeight) * CanvasUnitsPerGridPixel
                    : new Vector2(BubbleArt.TailWidth, BubbleArt.TailHeight) * CanvasUnitsPerGridPixel;

                Text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                Text.rectTransform.SetParent(Root, false);
                Text.rectTransform.anchorMin = Vector2.zero;
                Text.rectTransform.anchorMax = Vector2.one;
                var inset = Padding * CanvasUnitsPerGridPixel;
                Text.rectTransform.offsetMin = new Vector2(inset, inset);
                Text.rectTransform.offsetMax = new Vector2(-inset, -inset);
                Text.font = font;
                Text.fontSize = FontSize;
                Text.color = BubbleArt.InkColor;
                Text.alignment = TextAlignmentOptions.TopLeft;
                Text.textWrappingMode = TextWrappingModes.Normal;
                Text.raycastTarget = false;
            }

            public RectTransform Root { get; }

            public TextMeshProUGUI Text { get; }

            /// <summary>A thought (the hero's monologue): a trail of circles under it instead of a speech tail.</summary>
            public bool IsThought { get; }

            /// <summary>Sets the text and fits the bubble to it, in whole grid pixels.</summary>
            public void SetText(string text)
            {
                Text.text = text;
                var max = MaxTextWidth * CanvasUnitsPerGridPixel;
                var width = Mathf.Min(Text.GetPreferredValues(text, max, 0f).x, max);
                var height = Text.GetPreferredValues(text, width, 0f).y;
                var inset = 2f * Padding * CanvasUnitsPerGridPixel;
                Root.sizeDelta = new Vector2(Snap(width + inset), Snap(height + inset));
            }

            /// <summary>The tail hangs under the bottom edge, pointing down toward the speaker.</summary>
            public void PointTail(TailSide side)
            {
                if (IsThought)
                {
                    // The trail of circles hangs under the middle of the bubble, toward the thinker's head.
                    _tail.localScale = Vector3.one;
                    _tail.anchoredPosition = new Vector2(Snap(Root.rect.width / 2f), -CanvasUnitsPerGridPixel); // a pixel apart
                    return;
                }

                var corner = (BubbleArt.BodyBorder + 1) * CanvasUnitsPerGridPixel;
                var top = CanvasUnitsPerGridPixel; // the tail's top row covers the body's bottom outline
                switch (side)
                {
                    case TailSide.Left:
                        _tail.localScale = Vector3.one;
                        _tail.anchoredPosition = new Vector2(corner, top);
                        break;
                    case TailSide.Right:
                        _tail.localScale = new Vector3(-1f, 1f, 1f); // tip toward the right
                        _tail.anchoredPosition = new Vector2(Root.rect.width - corner, top);
                        break;
                    default:
                        _tail.localScale = Vector3.one;
                        _tail.anchoredPosition = new Vector2(Snap(Root.rect.width / 2f), top);
                        break;
                }
            }

            private static float Snap(float canvasUnits) =>
                Mathf.Ceil(canvasUnits / CanvasUnitsPerGridPixel) * CanvasUnitsPerGridPixel;
        }
    }
}
