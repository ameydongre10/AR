using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ARLab.AR;
using ARLab.Core;
using ARLab.Electronics;
using ARLab.Laboratory;
using ARLab.Simulation;

namespace ARLab.UI
{
    /// <summary>
    /// The whole student-facing panel set, driven from events rather than polling. Text
    /// content lives in the inspector; this only decides what each field says.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LabHud : MonoBehaviour
    {
        [Header("Status")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text arStateText;
        [SerializeField] private Text trackingText;
        [SerializeField] private Text netSummaryText;

        [Header("Notification")]
        [SerializeField] private GameObject notificationPanel;
        [SerializeField] private Text notificationText;
        [SerializeField] private Image notificationIcon;
        [SerializeField] private float notificationSeconds = 4f;

        [Header("Component library")]
        [SerializeField] private GameObject libraryPanel;
        [SerializeField] private Transform libraryList;
        [SerializeField] private GameObject libraryButtonPrefab;
        [SerializeField] private GameObject libraryCategoryPrefab;

        [Header("Inspector")]
        [SerializeField] private GameObject inspectorPanel;
        [SerializeField] private Text inspectorTitle;
        [SerializeField] private Text inspectorBody;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Button deleteButton;

        [Header("Validation")]
        [SerializeField] private GameObject validationPanel;
        [SerializeField] private Text validationHeadline;
        [SerializeField] private Transform validationList;
        [SerializeField] private GameObject validationRowPrefab;

        [Header("Tutorial")]
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private Text tutorialTitle;
        [SerializeField] private Text tutorialGoal;
        [SerializeField] private Text tutorialHint;
        [SerializeField] private Text tutorialProgress;
        [SerializeField] private Button tutorialSkip;

        [Header("Truth table")]
        [SerializeField] private GameObject truthTablePanel;
        [SerializeField] private Text truthTableText;

        private readonly List<string> _inspectorLines = new List<string>(16);
        private LaboratoryManager _lab;
        private TutorialController _tutorial;
        private GameManager _gameManager;
        private float _notificationTimer;
        private bool _subscribed;
        private bool _uiBuilt;

        private static readonly Color Info = new Color(0.30f, 0.70f, 0.95f);
        private static readonly Color Success = new Color(0.30f, 0.90f, 0.50f);
        private static readonly Color Warning = new Color(0.98f, 0.78f, 0.25f);
        private static readonly Color Error = new Color(0.95f, 0.35f, 0.32f);

        public void Configure(GameManager gm, LaboratoryManager lab, TutorialController tutorial)
        {
            _gameManager = gm;
            _lab = lab;
            _tutorial = tutorial;
            BuildRuntimeUi();
            Subscribe();
            BuildLibrary();
        }

        private void OnEnable()
        {
            if (_lab != null) Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            EventBus.Subscribe<NotificationEvent>(OnNotification);
            EventBus.Subscribe<SimulationResultEvent>(OnSimulation);
            EventBus.Subscribe<ValidationResultEvent>(OnValidation);
            EventBus.Subscribe<SelectionEvent>(OnSelection);
            EventBus.Subscribe<TutorialAdvancedEvent>(OnTutorialStep);
            EventBus.Subscribe<TutorialCompletedEvent>(OnTutorialDone);
            if (_tutorial != null) _tutorial.StepChanged += OnTutorialStepChanged;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            _subscribed = false;
            EventBus.Unsubscribe<NotificationEvent>(OnNotification);
            EventBus.Unsubscribe<SimulationResultEvent>(OnSimulation);
            EventBus.Unsubscribe<ValidationResultEvent>(OnValidation);
            EventBus.Unsubscribe<SelectionEvent>(OnSelection);
            EventBus.Unsubscribe<TutorialAdvancedEvent>(OnTutorialStep);
            EventBus.Unsubscribe<TutorialCompletedEvent>(OnTutorialDone);
            if (_tutorial != null) _tutorial.StepChanged -= OnTutorialStepChanged;
        }

        private void Update()
        {
            if (_notificationTimer > 0f)
            {
                _notificationTimer -= Time.unscaledDeltaTime;
                if (_notificationTimer <= 0f && notificationPanel != null) notificationPanel.SetActive(false);
            }

            RefreshNetSummary();
            RefreshArStatus();
        }

        /// <summary>
        /// AR state is polled rather than evented: ARSession.state is static, so there is no
        /// instance to raise an event from, and the values only change a few times a second.
        /// </summary>
        private void RefreshArStatus()
        {
            if (arStateText == null) return;
            if (Time.unscaledTime - _arRefreshAt < 0.25f) return;
            _arRefreshAt = Time.unscaledTime;

            var session = FindAnyObjectByType<ARLab.AR.ARSessionController>();
            if (session == null) return;

            arStateText.text = $"AR: {ArStateText.For(_machineState())}";
            string label = ARLab.AR.ARSessionController.TrackingLabel(session.Session);
            string reason = ARLab.AR.ARSessionController.TrackingReason(session.Session);
            trackingText.text = string.IsNullOrEmpty(reason) ? $"Tracking: {label}" : $"Tracking: {reason}";
        }

        private ArAppState _machineState()
            => _gameManager != null && _gameManager.Machine != null
                ? _gameManager.Machine.Current
                : ArAppState.Initializing;

        private float _arRefreshAt;

        // ------------------------------------------------------------------ procedural UI

        private static Font _uiFont;

        /// <summary>
        /// LegacyRuntime.ttf is the only built-in font from 2022 onwards; the old Arial name
        /// was removed. Resolved once and reused so nothing allocates a font per label.
        /// </summary>
        private static Font UiFont()
        {
            if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_uiFont == null) _uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _uiFont;
        }

        private static readonly Color PanelBg = new Color(0.06f, 0.08f, 0.11f, 0.88f);
        private static readonly Color PanelSoft = new Color(0.10f, 0.13f, 0.18f, 0.82f);
        private static readonly Color ButtonBg = new Color(0.16f, 0.22f, 0.32f, 0.95f);
        private static readonly Color TextMain = new Color(0.92f, 0.95f, 0.99f);
        private static readonly Color TextDim = new Color(0.62f, 0.68f, 0.78f);

        /// <summary>
        /// Builds the whole panel set from primitives. A scene therefore needs no hand-wired
        /// canvas: the field assignments below are the single definition of the layout, and a
        /// scene that already has panels keeps those instead.
        /// </summary>
        public void BuildRuntimeUi()
        {
            if (_uiBuilt) return;
            if (statusText != null) return;   // a hand-built scene already provided the fields
            _uiBuilt = true;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            RectTransform root = (RectTransform)transform;

            // ---- status bar
            RectTransform status = MakePanel(root, "StatusBar", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -190f), new Vector2(0f, 0f), PanelBg);
            arStateText = MakeText(status, "ARState", "AR: starting", 34, TextAnchor.MiddleLeft, TextMain);
            arStateText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            arStateText.rectTransform.anchorMax = new Vector2(0.34f, 1f);
            arStateText.rectTransform.offsetMin = new Vector2(24f, 0f);
            arStateText.rectTransform.offsetMax = new Vector2(0f, -8f);

            trackingText = MakeText(status, "Tracking", "Tracking: —", 28, TextAnchor.MiddleLeft, TextDim);
            trackingText.rectTransform.anchorMin = new Vector2(0.34f, 0.5f);
            trackingText.rectTransform.anchorMax = new Vector2(0.68f, 1f);
            trackingText.rectTransform.offsetMin = new Vector2(0f, 0f);
            trackingText.rectTransform.offsetMax = new Vector2(0f, -8f);

            netSummaryText = MakeText(status, "NetSummary", "0 nets", 28, TextAnchor.MiddleRight, TextDim);
            netSummaryText.rectTransform.anchorMin = new Vector2(0.68f, 0f);
            netSummaryText.rectTransform.anchorMax = new Vector2(1f, 1f);
            netSummaryText.rectTransform.offsetMin = Vector2.zero;
            netSummaryText.rectTransform.offsetMax = new Vector2(-24f, -8f);

            // ---- notification
            var notificationRt = MakePanel(root, "Notification", new Vector2(0.06f, 1f), new Vector2(0.94f, 1f),
                new Vector2(0f, -400f), new Vector2(0f, -210f), new Color(0.10f, 0.13f, 0.20f, 0.94f));
            notificationPanel = notificationRt.gameObject;
            notificationText = MakeText(notificationRt, "Message", "", 30, TextAnchor.MiddleLeft, TextMain);
            Place(notificationText.rectTransform, Vector2.zero, Vector2.one, new Vector2(64f, 0f), new Vector2(-28f, 0f));
            var iconRt = MakePanel(notificationRt, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(18f, -20f), new Vector2(46f, 20f), new Color(0.30f, 0.70f, 0.95f, 0.35f));
            notificationIcon = iconRt.GetComponent<Image>();
            notificationPanel.SetActive(false);

            // ---- component library
            var libraryRt = MakePanel(root, "Library", new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(24f, 240f), new Vector2(560f, -210f), PanelBg);
            libraryPanel = libraryRt.gameObject;
            MakeHeader(libraryRt, "Component Library", 36);

            var scrollRt = MakePanel(libraryRt, "Scroll", Vector2.zero, Vector2.one,
                new Vector2(12f, 12f), new Vector2(-12f, -70f), new Color(0f, 0f, 0f, 0f));
            var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            var viewport = MakePanel(scrollRt, "Viewport", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0f));
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var contentRt = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            contentRt.SetParent(viewport, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            var contentFitter = contentRt.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var contentLayout = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 6f;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;

            scroll.content = contentRt;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            libraryList = contentRt;

            // Row prefabs are built hidden and then instantiated by BuildLibrary.
            libraryCategoryPrefab = MakeLibraryRowPrefab("CategoryRow", 132f, TextDim, false);
            libraryButtonPrefab = MakeLibraryRowPrefab("ComponentRow", 150f, TextMain, true);

            // ---- inspector
            var inspectorRt = MakePanel(root, "Inspector", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-560f, 240f), new Vector2(-24f, -210f), PanelBg);
            inspectorPanel = inspectorRt.gameObject;
            MakeHeader(inspectorRt, "Inspector", 36);
            inspectorTitle = MakeText(inspectorRt, "Title", "Nothing selected", 32, TextAnchor.UpperLeft, TextMain);
            Place(inspectorTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -180f), new Vector2(-24f, -76f));
            inspectorBody = MakeText(inspectorRt, "Body", "", 26, TextAnchor.UpperLeft, TextDim);
            Place(inspectorBody.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(24f, 150f), new Vector2(-24f, -186f));

            var inspectorActions = MakePanel(inspectorRt, "InspectorActions",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 16f), new Vector2(-24f, 140f),
                new Color(0f, 0f, 0f, 0f));
            toggleButton = MakeButton(inspectorActions, "Toggle", "Toggle switch", 0f, 0.5f, 30,
                () => { if (_gameManager != null) _gameManager.OnToggleSelected(); });
            deleteButton = MakeButton(inspectorActions, "Delete", "Delete", 0.5f, 1f, 30,
                () => { if (_gameManager != null) _gameManager.OnDeleteSelected(); });
            inspectorPanel.SetActive(false);

            // ---- action bar
            RectTransform actions = MakePanel(root, "ActionBar", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 24f), new Vector2(-24f, 220f), PanelBg);
            // Nine actions, each spanning one ninth of the bar so the row fills its width.
            MakeButton(actions, "Library", "Library", 0f / 9f, 1f / 9f, 32, OnToggleLibraryAction);
            MakeButton(actions, "Place", "Place", 1f / 9f, 2f / 9f, 32, OnPlace);
            MakeButton(actions, "Power", "Power", 2f / 9f, 3f / 9f, 32, () => { if (_gameManager != null) _gameManager.OnTogglePower(); });
            MakeButton(actions, "Cancel", "Cancel wire", 3f / 9f, 4f / 9f, 32, () => { if (_gameManager != null) _gameManager.OnCancelWire(); });
            MakeButton(actions, "Evaluate", "Evaluate", 4f / 9f, 5f / 9f, 32, () => { if (_gameManager != null) _gameManager.OnEvaluate(); });
            MakeButton(actions, "TruthTable", "Truth table", 5f / 9f, 6f / 9f, 32, OnToggleTruthTable);
            MakeButton(actions, "Reset", "Reset", 6f / 9f, 7f / 9f, 32, () => { if (_gameManager != null) _gameManager.OnReset(); });
            MakeButton(actions, "Inspector", "Inspector", 7f / 9f, 8f / 9f, 32, OnToggleInspector);
            MakeButton(actions, "Help", "Help", 8f / 9f, 9f / 9f, 32, OnToggleTutorial);

            // ---- tutorial
            var tutorialRt = MakePanel(root, "Tutorial", new Vector2(0.06f, 0f), new Vector2(0.94f, 0f),
                new Vector2(0f, 240f), new Vector2(0f, 560f), PanelSoft);
            tutorialPanel = tutorialRt.gameObject;
            tutorialTitle = MakeText(tutorialRt, "Title", "", 32, TextAnchor.UpperLeft, Info);
            Place(tutorialTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(28f, -78f), new Vector2(-28f, -16f));
            tutorialGoal = MakeText(tutorialRt, "Goal", "", 30, TextAnchor.UpperLeft, TextMain);
            Place(tutorialGoal.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(28f, 92f), new Vector2(-28f, -84f));
            tutorialHint = MakeText(tutorialRt, "Hint", "", 26, TextAnchor.UpperLeft, TextDim);
            Place(tutorialHint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(28f, 88f), new Vector2(-28f, -168f));
            tutorialProgress = MakeText(tutorialRt, "Progress", "", 24, TextAnchor.LowerLeft, TextDim);
            Place(tutorialProgress.rectTransform, new Vector2(0f, 0f), new Vector2(0.75f, 0f),
                new Vector2(28f, 8f), new Vector2(0f, 44f));
            tutorialSkip = MakeButton(tutorialRt, "Skip", "Skip step", 0.75f, 1f, 26, OnSkipTutorialStep);

            // ---- validation + truth table
            var validationRt = MakePanel(root, "Validation", new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.85f),
                Vector2.zero, Vector2.zero, new Color(0.05f, 0.07f, 0.10f, 0.97f));
            validationPanel = validationRt.gameObject;
            validationHeadline = MakeText(validationRt, "Headline", "Validation", 40, TextAnchor.UpperLeft, TextMain);
            Place(validationHeadline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(32f, -84f), new Vector2(-32f, -20f));

            var checkScroll = MakePanel(validationRt, "Checks", Vector2.zero, Vector2.one,
                new Vector2(32f, 96f), new Vector2(-32f, -92f), new Color(0f, 0f, 0f, 0f));
            var checkContentRt = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            checkContentRt.SetParent(checkScroll, false);
            checkContentRt.anchorMin = new Vector2(0f, 1f);
            checkContentRt.anchorMax = new Vector2(1f, 1f);
            checkContentRt.pivot = new Vector2(0.5f, 1f);
            checkContentRt.offsetMin = Vector2.zero;
            checkContentRt.offsetMax = Vector2.zero;
            var checkFitter = checkContentRt.gameObject.AddComponent<ContentSizeFitter>();
            checkFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var checkLayout = checkContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            checkLayout.spacing = 4f;
            checkLayout.childControlHeight = true;
            checkLayout.childForceExpandHeight = false;
            checkLayout.childControlWidth = true;
            checkScroll.gameObject.AddComponent<ScrollRect>().content = checkContentRt;
            validationList = checkContentRt;
            validationRowPrefab = MakeValidationRowPrefab();

            var validationTruth = MakeText(validationRt, "TruthTable", "", 26, TextAnchor.LowerLeft, TextDim);
            Place(validationTruth.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(32f, 16f), new Vector2(-32f, 92f));

            MakeButton(validationRt, "Close", "Close", 0.5f, 1f, 30, OnCloseValidation);
            validationPanel.SetActive(false);

            var truthTableRt = MakePanel(root, "TruthTable", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-320f, -300f), new Vector2(320f, 300f), new Color(0.05f, 0.07f, 0.10f, 0.97f));
            truthTablePanel = truthTableRt.gameObject;
            truthTableText = MakeText(truthTableRt, "Text", "", 28, TextAnchor.UpperLeft, TextMain);
            Place(truthTableText.rectTransform, Vector2.zero, Vector2.one, new Vector2(28f, 28f), new Vector2(-28f, -28f));
            truthTablePanel.SetActive(false);

            // The library is the only way to add parts, so it starts open.
            libraryPanel.SetActive(true);
        }

        /// <summary>True once the procedural panel set exists, for tooling and tests.</summary>
        public bool BuiltUi => _uiBuilt;

        /// <summary>
        /// Counts only *active* children, because a row that exists in the hierarchy but is
        /// inactive is not rendered and is therefore still missing from the UI.
        /// </summary>
        public int VisibleLibraryRowCount => CountActiveChildren(libraryList);

        public int VisibleValidationRowCount => CountActiveChildren(validationList);

        private static int CountActiveChildren(Transform parent)
        {
            if (parent == null) return 0;
            int n = 0;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).gameObject.activeSelf) n++;
            return n;
        }

        /// <summary>
        /// Marks a hidden row template. Templates carry a Button component so the instantiated
        /// rows inherit it, which would otherwise make them indistinguishable from real UI.
        /// </summary>
        [DisallowMultipleComponent]
        public sealed class RowTemplate : MonoBehaviour
        {
        }

        /// <summary>
        /// Places the rig in front of the camera. In a real AR session the student taps a
        /// surface instead; this keeps the flow reachable in the editor and on a device where
        /// no plane has been detected yet.
        /// </summary>
        private void OnPlace()
        {
            if (_gameManager == null) return;
            if (_gameManager.OnPlaceInFrontOfCamera()) libraryPanel.SetActive(false);
        }

        private void OnToggleLibraryAction() => TogglePanel(libraryPanel);

        private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
        }

        private static RectTransform MakePanel(Transform parent, string name, Vector2 aMin, Vector2 aMax,
            Vector2 oMin, Vector2 oMax, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, aMin, aMax, oMin, oMax);
            if (bg.a > 0f) go.AddComponent<Image>().color = bg;
            return rt;
        }

        private static Text MakeText(Transform parent, string name, string content, int size,
            TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = UiFont();
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = content;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        private static void MakeHeader(Transform parent, string label, int size)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -66f), new Vector2(-24f, -14f));
            var t = go.AddComponent<Text>();
            t.font = UiFont();
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleLeft;
            t.color = TextMain;
            t.text = label;
            t.raycastTarget = false;
        }

        private static Button MakeButton(Transform parent, string name, string label, float xMin, float xMax,
            int size, UnityEngine.Events.UnityAction onClick)
        {
            // xMin/xMax are normalised across the row's own width, so each row decides its own
            // column count and a button always spans the fraction it asks for. An earlier
            // version divided by a single global column count, which left every row filling
            // only part of its panel and stranded the right-hand side.
            const float pad = 10f;
            var bg = MakePanel(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, ButtonBg);
            Place(bg, new Vector2(xMin, 0f), new Vector2(xMax, 1f),
                new Vector2(pad, 0f), new Vector2(-pad, 0f));

            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg.GetComponent<Image>();
            if (onClick != null) button.onClick.AddListener(onClick);

            var t = MakeText(bg, "Label", label, size, TextAnchor.MiddleCenter, TextMain);
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f));
            return button;
        }

        private GameObject MakeLibraryRowPrefab(string name, float height, Color color, bool interactive)
        {
            var go = new GameObject(name, typeof(RectTransform));
            // Parented under the canvas so the template's lifetime follows this component;
            // an unparented template would be left behind at the scene root.
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(0f, height);
            if (interactive)
            {
                var img = go.AddComponent<Image>();
                img.color = new Color(0.13f, 0.18f, 0.26f, 0.9f);
                var button = go.AddComponent<Button>();
                button.targetGraphic = img;
            }
            else
            {
                go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            }

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            go.AddComponent<RowTemplate>();

            var title = MakeText(go.transform, "Title", "", 28, TextAnchor.MiddleLeft, color);
            Place(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(14f, 0f), new Vector2(-14f, 0f));
            // The blurb is a second line inside the same row.
            var sub = MakeText(go.transform, "Sub", "", 22, TextAnchor.MiddleLeft, TextDim);
            Place(sub.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.45f), new Vector2(14f, 0f), new Vector2(-14f, 0f));

            go.SetActive(false);
            return go;
        }

        private GameObject MakeValidationRowPrefab()
        {
            var go = new GameObject("ValidationRow", typeof(RectTransform));
            // Parented under the canvas so the template's lifetime follows this component.
            go.transform.SetParent(transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(0f, 96f);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 96f;
            layout.preferredHeight = 96f;
            go.AddComponent<RowTemplate>();
            var group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 16f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;

            var title = MakeText(go.transform, "Title", "", 30, TextAnchor.MiddleLeft, TextMain);
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredWidth = 460f;
            titleLe.flexibleWidth = 0f;

            var detail = MakeText(go.transform, "Detail", "", 26, TextAnchor.MiddleLeft, TextDim);
            var detailLe = detail.gameObject.AddComponent<LayoutElement>();
            detailLe.flexibleWidth = 1f;

            go.SetActive(false);
            return go;
        }

        // ------------------------------------------------------------------ panels

        private void OnNotification(NotificationEvent e)
        {
            if (notificationText != null) notificationText.text = e.Message;
            if (notificationIcon != null) notificationIcon.color = ColorFor(e.Severity);
            if (notificationPanel != null) notificationPanel.SetActive(true);
            _notificationTimer = e.Severity == NotificationSeverity.Error ? notificationSeconds * 2f : notificationSeconds;
        }

        private void OnSimulation(SimulationResultEvent e)
        {
            if (netSummaryText == null) return;
            netSummaryText.text = e.Converged
                ? $"Settled in {e.IterationCount} pass(es)"
                : e.Diagnostic;
        }

        private void OnSelection(SelectionEvent e)
        {
            bool has = e.Component != null;
            if (inspectorPanel != null && !has) inspectorPanel.SetActive(false);
            if (deleteButton != null) deleteButton.interactable = has;
            if (toggleButton != null) toggleButton.interactable = e.Component is InputSwitch;
            if (!has) return;

            if (inspectorTitle != null) inspectorTitle.text = e.Component.DisplayName;
            if (_lab != null)
            {
                _lab.FillInspector(_inspectorLines);
                if (inspectorBody != null) inspectorBody.text = string.Join("\n", _inspectorLines);
            }
        }

        private void OnTutorialStep(TutorialAdvancedEvent e) => RefreshTutorial();

        private void OnTutorialStepChanged(int index, TutorialController.Step step) => RefreshTutorial();

        private void OnTutorialDone(TutorialCompletedEvent _)
        {
            if (tutorialPanel != null) tutorialPanel.SetActive(false);
        }

        private void RefreshTutorial()
        {
            if (_tutorial == null) return;
            TutorialController.Step s = _tutorial.Current;
            bool show = _tutorial.IsRunning && s != null;
            if (tutorialPanel != null) tutorialPanel.SetActive(show);
            if (!show) return;

            if (tutorialTitle != null) tutorialTitle.text = $"Step {_tutorial.Index + 1}/{_tutorial.TotalSteps}: {s.Title}";
            if (tutorialGoal != null) tutorialGoal.text = s.Goal;
            if (tutorialHint != null) tutorialHint.text = s.Hint;
            if (tutorialProgress != null) tutorialProgress.text = $"Progress {_tutorial.Index}/{_tutorial.TotalSteps}";
        }

        private void RefreshNetSummary()
        {
            if (netSummaryText == null || _lab == null) return;
            ConnectionGraph g = _lab.Graph;
            int nets = 0, floating = 0, contended = 0;
            foreach (Net n in g.Nets)
            {
                nets++;
                if (n.IsContended) contended++;
                else if (n.IsFloating) floating++;
            }
            netSummaryText.text = $"{nets} nets · {floating} floating · {contended} contended";
        }

        // ------------------------------------------------------------------ validation

        private void OnValidation(ValidationResultEvent e)
        {
            if (validationPanel == null) return;
            validationPanel.SetActive(true);
            ExperimentReport r = e.Report;
            if (validationHeadline != null) validationHeadline.text = r.Headline;

            if (validationList != null && validationRowPrefab != null)
            {
                validationList.gameObject.SetActive(true);
                for (int i = validationList.childCount - 1; i >= 0; i--)
                    LabMaterials.Discard(validationList.GetChild(i).gameObject);

                for (int i = 0; i < r.Checks.Count; i++)
                    BuildValidationRow(r.Checks[i]);
            }

            ShowTruthTable(r.TruthTable);
        }

        /// <summary>
        /// Instantiates a row for one check. The row prefab is expected to contain a first
        /// Text (the glyph + title) and a second Text (the detail).
        /// </summary>
        private void BuildValidationRow(ValidationCheck check)
        {
            var go = InstantiateActive(validationRowPrefab, validationList);
            var labels = go.GetComponentsInChildren<Text>(true);
            if (labels.Length > 0)
            {
                labels[0].text = $"{check.Glyph}  {check.Title}";
                labels[0].color = ColorFor(check.Level);
            }
            if (labels.Length > 1) labels[1].text = check.Detail;
        }

        private void ShowTruthTable(TruthTableResult table)
        {
            if (truthTablePanel == null || truthTableText == null) return;
            if (table == null) { truthTablePanel.SetActive(false); return; }
            truthTablePanel.SetActive(true);
            truthTableText.text = table.ToPlainText();
        }

        // ------------------------------------------------------------------ library

        private void BuildLibrary()
        {
            if (libraryList == null || libraryButtonPrefab == null) return;

            // Rebuilt rather than appended, so calling this twice cannot duplicate rows.
            for (int i = libraryList.childCount - 1; i >= 0; i--)
                LabMaterials.Discard(libraryList.GetChild(i).gameObject);

            string category = null;
            for (int i = 0; i < LaboratoryManager.Catalog.Length; i++)
            {
                CatalogEntry entry = LaboratoryManager.Catalog[i];
                if (entry.Category != category)
                {
                    category = entry.Category;
                    if (libraryCategoryPrefab != null)
                    {
                        var header = InstantiateActive(libraryCategoryPrefab, libraryList);
                        var t = header.GetComponentInChildren<Text>();
                        if (t != null) t.text = category.ToUpperInvariant();
                    }
                }
                BuildLibraryButton(entry);
            }
        }

        private void BuildLibraryButton(CatalogEntry entry)
        {
            var go = InstantiateActive(libraryButtonPrefab, libraryList);
            var button = go.GetComponent<Button>();
            if (button == null) button = go.AddComponent<Button>();

            // The relay carries the catalog key so no per-button closure is allocated.
            var relay = go.GetComponent<LibraryButtonRelay>();
            if (relay == null) relay = go.AddComponent<LibraryButtonRelay>();
            relay.Bind(_gameManager, entry.Key, entry.DisplayName);
            button.onClick.AddListener(relay.Press);

            // Row prefabs carry a title and a sub-line, so both are filled here.
            var labels = go.GetComponentsInChildren<Text>(true);
            if (labels.Length > 0 && labels[0] != null) labels[0].text = entry.DisplayName;
            if (labels.Length > 1 && labels[1] != null) labels[1].text = entry.PartNumber;
            if (labels.Length > 2 && labels[2] != null) labels[2].text = entry.Blurb;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Instantiates a row template and activates the clone. Row templates are kept inactive
        /// so they never render, and Instantiate copies that inactive state, so a plain
        /// Instantiate would produce a row that is present in the hierarchy but invisible.
        /// </summary>
        private static GameObject InstantiateActive(GameObject template, Transform parent)
        {
            GameObject go = Instantiate(template, parent);
            go.SetActive(true);
            return go;
        }

        private static Color ColorFor(NotificationSeverity s)
        {
            switch (s)
            {
                case NotificationSeverity.Success: return Success;
                case NotificationSeverity.Warning: return Warning;
                case NotificationSeverity.Error: return Error;
                default: return Info;
            }
        }

        private static Color ColorFor(CheckLevel l)
        {
            switch (l)
            {
                case CheckLevel.Pass: return Success;
                case CheckLevel.Warn: return Warning;
                case CheckLevel.Fail: return Error;
                default: return Info;
            }
        }

        // ------------------------------------------------------------------ UI callbacks

        public void OnToggleLibrary() { if (libraryPanel != null) libraryPanel.SetActive(!libraryPanel.activeSelf); }
        public void OnToggleInspector() { TogglePanel(inspectorPanel); }
        public void OnToggleTutorial() { TogglePanel(tutorialPanel); }
        public void OnToggleTruthTable() { if (truthTablePanel != null) truthTablePanel.SetActive(!truthTablePanel.activeSelf); }
        public void OnCloseValidation() { if (validationPanel != null) validationPanel.SetActive(false); }
        public void OnSkipTutorialStep() { if (_tutorial != null) _tutorial.SkipStep(); }

        /// <summary>Flips a panel's visibility, tolerating an unbuilt or missing one.</summary>
        private static void TogglePanel(GameObject panel)
        {
            if (panel != null) panel.SetActive(!panel.activeSelf);
        }
    }

    /// <summary>Carries a catalog key on a button so the listener needs no closure.</summary>
    [DisallowMultipleComponent]
    public sealed class LibraryButtonRelay : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        private string _key;
        private string _label;

        public void Bind(GameManager gm, string key, string label)
        {
            gameManager = gm;
            _key = key;
            _label = label;
        }

        public void Bind(string key, string label)
        {
            _key = key;
            _label = label;
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        }

        public string Key => _key;

        public void Press()
        {
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogWarning($"[LabHud] No GameManager; cannot spawn {_label}.");
                return;
            }
            gameManager.OnComponentLibrarySpawn(_key);
        }
    }
}
