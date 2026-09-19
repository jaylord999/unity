using UnityEngine;
using UnityEngine.UI;

namespace MysticMap
{
    /// <summary>
    /// The magic bar: a small uGUI gauge built entirely from code (no prefabs, no font assets
    /// needed - the project keeps its look procedural).
    ///
    /// It shows the charge as five coloured segments, one per magic level, with a tick at every
    /// threshold. Segments dim until their level is reached, then light up in that level's
    /// colour while the next one fills; the thin strip above the last threshold is the
    /// overcharge window. The level name, school and the damage a release would deal right now
    /// are written next to it, and the result of the last cast flashes for a moment.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Magic Charge HUD")]
    public class MagicChargeHUD : MonoBehaviour
    {
        [Header("Binding")]
        [Tooltip("The magic this bar shows (auto-filled from the scene).")]
        public SlimeMagic magic;

        [Header("Layout")]
        public float barWidth = 560f;
        public float barHeight = 26f;
        public float bottomMargin = 46f;
        [Tooltip("Seconds the bar takes to fade in / out.")]
        public float fadeSpeed = 7f;
        [Tooltip("Keep the last cast result on screen for this long.")]
        public float flashDuration = 1.6f;
        public bool showHints = true;
        public bool showDamage = true;

        [Header("Colours")]
        public Color panelColor = new Color(0.04f, 0.04f, 0.09f, 0.62f);
        public Color borderColor = new Color(0.55f, 0.6f, 0.85f, 0.45f);
        public Color emptyColor = new Color(0.18f, 0.18f, 0.24f, 0.85f);
        public Color textColor = new Color(0.92f, 0.95f, 1f, 1f);

        CanvasGroup _group;
        RectTransform _barRect;
        Image _barBackground;
        Image[] _segments;
        Image[] _pips;
        Image _overcharge;
        Text _nameText;
        Text _damageText;
        Text _hintText;

        float _alpha;
        int _segmentCount = -1;
        float _overchargeLimit;

        static Font _font;
        static Sprite _white;

        void Awake()
        {
            if (magic == null) magic = FindFirstObjectByType<SlimeMagic>();
        }

        void Start()
        {
            Build();
        }

        void OnDestroy()
        {
            if (_white != null) { Destroy(_white.texture); Destroy(_white); _white = null; }
        }

        // =====================================================================
        //  Building the bar (once, from code)
        // =====================================================================
        void Build()
        {
            if (_group != null) return;

            int count = Mathf.Max(1, magic != null ? magic.LevelCount : 1);

            var canvasGo = new GameObject("Magic Bar Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // ---- panel -------------------------------------------------------
            RectTransform panel = NewRect("Magic Bar", canvasGo.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, Mathf.Max(0f, bottomMargin));
            panel.sizeDelta = new Vector2(Mathf.Max(160f, barWidth) + 28f, 92f);

            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            Image frame = NewImage("Frame", panel, borderColor);
            Stretch(frame.rectTransform);

            Image backdrop = NewImage("Backdrop", panel, panelColor);
            Stretch(backdrop.rectTransform, new Vector2(-2f, -2f));

            // ---- texts -------------------------------------------------------
            _nameText = NewText("Level", panel, TextAnchor.MiddleLeft,
                                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -6f),
                                new Vector2(420f, 24f), 20);
            _damageText = NewText("Damage", panel, TextAnchor.MiddleRight,
                                  new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -6f),
                                  new Vector2(300f, 24f), 20);

            _hintText = NewText("Hint", panel, TextAnchor.LowerLeft,
                                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 5f),
                                new Vector2(460f, 18f), 14);
            _hintText.color = new Color(textColor.r, textColor.g, textColor.b, 0.65f);
            if (showHints) _hintText.text = "HOLD  left mouse  -  RELEASE  to fire  -  FULL BAR  =  maximum charge";

            // ---- the bar itself ---------------------------------------------
            float width = Mathf.Max(160f, barWidth);
            float height = Mathf.Max(10f, barHeight);

            _barBackground = NewImage("Track", panel, emptyColor);
            _barRect = _barBackground.rectTransform;
            _barRect.anchorMin = _barRect.anchorMax = new Vector2(0f, 0f);
            _barRect.pivot = new Vector2(0f, 0f);
            _barRect.anchoredPosition = new Vector2(16f, 28f);
            _barRect.sizeDelta = new Vector2(width, height);

            // Each level gets a dim track plus a bright fill, so a reached-but-not-charged
            // level is visible while the current one fills up over it.
            _segments = new Image[count];
            float lastThreshold = 0f;

            for (int i = 0; i < count; i++)
            {
                float from = i == 0 ? 0f : ThresholdRatio(i);        // 0..1 positions on the bar
                float to = i == count - 1 ? 1f : ThresholdRatio(i + 1);
                if (to <= from + 0.001f) to = Mathf.Min(1f, from + 0.05f);
                lastThreshold = i == count - 1 ? from : lastThreshold;

                Color tint = LevelTint(i);

                Image track = NewImage("Segment " + i, _barRect, Dim(tint));
                track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(0f, 0f);
                track.rectTransform.pivot = new Vector2(0f, 0f);
                track.rectTransform.anchoredPosition = new Vector2(from * width, 0f);
                track.rectTransform.sizeDelta = new Vector2((to - from) * width, height);

                Image fill = NewImage("Fill " + i, track.rectTransform, tint);
                Stretch(fill.rectTransform);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 0f;

                _segments[i] = fill;
            }

            _overchargeLimit = lastThreshold;

            // ---- threshold ticks --------------------------------------------
            for (int i = 0; i < count; i++)
            {
                float ratio = i == 0 ? 0f : ThresholdRatio(i);

                Image tick = NewImage("Tick " + i, _barRect, borderColor);
                tick.rectTransform.anchorMin = tick.rectTransform.anchorMax = new Vector2(0f, 0f);
                tick.rectTransform.pivot = new Vector2(0.5f, 0f);
                tick.rectTransform.anchoredPosition = new Vector2(ratio * width, -3f);
                tick.rectTransform.sizeDelta = new Vector2(2f, height + 6f);
            }

            // ---- the overcharge window above the last threshold --------------
            _overcharge = NewImage("Overcharge", _barRect, Color.white);
            _overcharge.rectTransform.anchorMin = _overcharge.rectTransform.anchorMax = new Vector2(0f, 0f);
            _overcharge.rectTransform.pivot = new Vector2(0f, 0f);
            _overcharge.rectTransform.anchoredPosition = new Vector2(_overchargeLimit * width, height + 5f);
            _overcharge.rectTransform.sizeDelta = new Vector2(Mathf.Max(2f, (1f - _overchargeLimit) * width), 4f);
            _overcharge.type = Image.Type.Filled;
            _overcharge.fillMethod = Image.FillMethod.Horizontal;
            _overcharge.fillOrigin = (int)Image.OriginHorizontal.Left;
            _overcharge.fillAmount = 0f;

            // ---- one pip per level -------------------------------------------
            _pips = new Image[count];
            for (int i = 0; i < count; i++)
            {
                float ratio = i == 0 ? 0f : ThresholdRatio(i);

                Image pip = NewImage("Pip " + i, panel, Dim(LevelTint(i)));
                pip.rectTransform.anchorMin = pip.rectTransform.anchorMax = new Vector2(0f, 0f);
                pip.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                pip.rectTransform.anchoredPosition = new Vector2(16f + ratio * width, 28f + height + 12f);
                pip.rectTransform.sizeDelta = new Vector2(9f, 9f);
                pip.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _pips[i] = pip;
            }

            _segmentCount = count;
        }

        // =====================================================================
        //  Keeping it up to date
        // =====================================================================
        void Update()
        {
            if (magic == null)
            {
                magic = FindFirstObjectByType<SlimeMagic>();
                if (magic == null) return;
            }

            if (_group == null || _segmentCount != Mathf.Max(1, magic.LevelCount))
            {
                // First run, or the level table was rebuilt by the editor tool: start the bar over.
                if (_group != null) Destroy(_group.gameObject);
                _group = null;
                _segments = null;
                _pips = null;
                _overcharge = null;
                _nameText = null;
                _damageText = null;
                _hintText = null;
                _barRect = null;
                _barBackground = null;
                _segmentCount = -1;
                Build();
                if (_group == null) return;
            }

            float dt = Time.deltaTime;
            bool wantVisible = magic.IsCharging || magic.LastCastAge < flashDuration || magic.CooldownLeft > 0f;
            _alpha = Mathf.MoveTowards(_alpha, wantVisible ? 1f : 0f, Mathf.Max(0.01f, fadeSpeed) * dt);
            _group.alpha = _alpha;
            if (_alpha <= 0.001f) return;

            int count = _segments != null ? _segments.Length : 0;
            float charge = magic.ChargeNormalized;

            for (int i = 0; i < count; i++)
            {
                Image fill = _segments[i];
                if (fill == null) continue;

                float from = i == 0 ? 0f : magic.ThresholdRatio(i);
                float to = i == count - 1 ? 1f : magic.ThresholdRatio(i + 1);
                float span = Mathf.Max(0.001f, to - from);

                bool reached = magic.ReachedLevel > i;
                fill.fillAmount = Mathf.Clamp01((charge - from) / span);
                fill.color = reached ? LevelTint(i) : Dim(LevelTint(i));

                if (_pips != null && i < _pips.Length && _pips[i] != null)
                    _pips[i].color = reached ? LevelTint(i) : Dim(LevelTint(i));
            }

            if (_overcharge != null)
            {
                _overcharge.fillAmount = magic.OverchargeFraction;
                _overcharge.color = LevelTint(Mathf.Max(0, count - 1));
            }

            UpdateTexts();
        }

        void UpdateTexts()
        {
            if (_nameText == null) return;

            if (!magic.IsCharging && magic.LastCastAge < flashDuration)
            {
                _nameText.text = "CAST  " + NameOf(magic.LastCastLevel);
                _nameText.color = LevelTint(Mathf.Max(0, magic.LastCastLevel - 1));
                if (showDamage) _damageText.text = magic.LastCastDamage.ToString("0") + " DMG";
            }
            else
            {
                int level = Mathf.Max(1, magic.ReachedLevel);
                string school = magic.CurrentSchool;
                _nameText.text = "LV " + level + "  " + magic.CurrentName +
                                 (string.IsNullOrEmpty(school) ? string.Empty : "  -  " + school);
                _nameText.color = magic.ReachedLevel > 0 ? magic.CurrentTint : textColor;

                if (showDamage)
                {
                    string damage = "DMG " + magic.DamageNow.ToString("0");
                    if (magic.OverchargeFraction >= 0.999f)
                        damage = "MAX CHARGE   " + damage + "   +" +
                                 (magic.OverchargeFraction * 100f).ToString("0") + "%";
                    else if (magic.OverchargeFraction > 0.001f)
                        damage += "   +" + (magic.OverchargeFraction * 100f).ToString("0") + "% overcharge";
                    else if (magic.CooldownLeft > 0.01f)
                        damage = "COOLDOWN  " + magic.CooldownLeft.ToString("0.0") + "s";

                    _damageText.text = damage;
                    _damageText.color = magic.OverchargeFraction >= 0.999f
                        ? Color.Lerp(textColor, LevelTint(Mathf.Max(0, level - 1)), 0.75f)
                        : textColor;
                }
            }

            if (showDamage && magic.OverchargeFraction < 0.999f) _damageText.color = textColor;
        }

        // =====================================================================
        //  Tools
        // =====================================================================
        float ThresholdRatio(int level) => magic != null ? magic.ThresholdRatio(level) : 0f;

        Color LevelTint(int index)
        {
            MagicSpellLevel lv = magic != null ? magic.LevelAt(index + 1) : null;
            return lv != null ? lv.tint : textColor;
        }

        string NameOf(int level)
        {
            MagicSpellLevel lv = magic != null ? magic.LevelAt(level) : null;
            return lv != null ? lv.name : "-";
        }

        static Color Dim(Color color) => new Color(color.r, color.g, color.b, 0.28f);

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect, Vector2 padding = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = padding;
        }

        static Text NewText(string name, Transform parent, TextAnchor anchor, Vector2 anchorMin,
                            Vector2 anchorMax, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();

            text.font = ResolveFont();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMax.y);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return text;
        }

        /// <summary>A 4x4 white sprite, shared by every element of the bar.</summary>
        static Sprite WhiteSprite()
        {
            if (_white != null) return _white;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            texture.name = "MagicBarWhite";

            _white = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            _white.name = "MagicBarWhite";
            return _white;
        }

        /// <summary>
        /// The built-in font. Unity 6 ships "LegacyRuntime.ttf"; older editor versions had
        /// "Arial.ttf". If neither is there the bar simply runs without text.
        /// </summary>
        static Font ResolveFont()
        {
            if (_font != null) return _font;

            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { _font = null; } }
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Segoe UI", 16);
            return _font;
        }
    }
}
