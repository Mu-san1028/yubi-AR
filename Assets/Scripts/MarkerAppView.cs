using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Object = UnityEngine.Object;

namespace Yubi
{
    // Runtime UI keeps the same controls in the Editor and a built player.
    public sealed class MarkerAppView : IDisposable
    {
        private static readonly Color Ink = new Color(.035f, .06f, .10f, .96f);
        private static readonly Color Surface = new Color(.10f, .15f, .21f);
        private static readonly Color Muted = new Color(.70f, .77f, .84f);
        private static readonly Color Mint = new Color(.40f, .94f, .76f);
        private static readonly Color Error = new Color(1f, .57f, .55f);
        private static readonly Color[] MarkerColors = {
            new Color(.36f,.82f,.53f), new Color(1f,.49f,.49f), new Color(.40f,.68f,1f),
            new Color(.79f,.58f,1f), new Color(1f,.82f,.29f), Color.white
        };
        private readonly TMP_FontAsset font;
        private readonly Sprite rounded;
        private readonly Texture2D roundTexture;
        private readonly Texture2D[] previews = new Texture2D[6];
        private readonly Func<int, string> label;
        private readonly RectTransform safe;
        private readonly List<RectTransform> centeredCards = new List<RectTransform>();
        private readonly List<RectTransform> effectObstacles = new List<RectTransform>();
        private readonly GameObject menu, hud, communication, practice, guide, answerButtons, resultButtons;
        private readonly TextMeshProUGUI modeTitle, status, message, communicationHint, communicationBadge;
        private readonly TextMeshProUGUI progress, question, feedback, instruction, practiceHint;
        private readonly TextMeshProUGUI hintButtonLabel;
        private readonly Image[] chips = new Image[6], dots = new Image[6];
        private readonly TextMeshProUGUI[] chipLabels = new TextMeshProUGUI[6];
        private readonly RawImage[] hintImages = new RawImage[2];
        private readonly Image progressFill;
        private readonly RectTransform questionStage;
        private readonly TextMeshProUGUI questionCaption;
        private readonly Button nextButton, hintButton, skipButton, reviewButton;
        private readonly Action<bool> startPractice;
        private bool lastSingles;
        private string previousMessage = "";
        private Rect lastSafeArea;
        private Vector2 lastScreen;
        public GameObject Root { get; private set; }
        public TMP_FontAsset DisplayFont { get { return font; } }
        public RectTransform EffectParent { get { return safe; } }
        public RectTransform[] EffectObstacles { get { return effectObstacles.ToArray(); } }

        public MarkerAppView(Transform parent, Font displayFont, Func<int, string> markerLabel, MarkerMessageRule[] rules,
            Action showMenu, Action startCommunication, Action<bool> beginPractice, Action toggleGuide,
            Action toggleHint, Action next, Action skip, Action review, Texture2D[] markerPreviews = null, Action startTown = null)
        {
            label = markerLabel; startPractice = beginPractice;
            font = CreateDisplayFont(displayFont);
            roundTexture = MakeRoundedTexture();
            rounded = Sprite.Create(roundTexture, new Rect(0,0,256,256), new Vector2(.5f,.5f), 400, 0,
                SpriteMeshType.FullRect, new Vector4(80,80,80,80));
            string[] names = { "1_green_0", "2_red_0", "3_blue_0", "4_purple_0", "5_yellow_0", "idmarker" };
            for (int i = 0; i < previews.Length; i++)
                previews[i] = markerPreviews != null && i < markerPreviews.Length && markerPreviews[i] != null ?
                    markerPreviews[i] : Resources.Load<Texture2D>("MarkerPreviews/" + names[i]);

            Root = new GameObject("MarkerMessageCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Root.transform.SetParent(parent, false);
            Canvas canvas = Root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080,900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safe = RectObject(Root.transform, "SafeArea"); Stretch(safe);
            EnsureEventSystem();

            hud = RectObject(safe, "ModeHUD").gameObject; Stretch((RectTransform)hud.transform);
            RectTransform toolbar = Card(hud.transform, "Toolbar", 60, true, 20);
            modeTitle = Text(toolbar, "ModeTitle", "", 26, 0, 60, TextAnchor.MiddleLeft);
            modeTitle.rectTransform.anchorMax = new Vector2(.55f,1);
            ButtonAt(toolbar, "GuideButton", "対応表", toggleGuide, .56f,.76f,8,44,22);
            ButtonAt(toolbar, "MenuButton", "モード変更", showMenu, .77f,.98f,8,44,22);
            RectTransform recognition = Card(hud.transform, "Recognition", 146, true, 94);
            status = Text(recognition,"Status","認識中：なし",20,10,32,TextAnchor.MiddleLeft);
            status.color = Muted;
            for (int i = 0; i < 6; i++)
            {
                float column = i % 3;
                chips[i] = Image(recognition,"Marker" + (i+1),Surface);
                Place(chips[i].rectTransform,column/3,(column+1)/3,52+(i/3)*42,34,12,12);
                dots[i] = Image(chips[i].transform,"Color",MarkerColors[i]);
                Place(dots[i].rectTransform,0,0,11,12,12,-24);
                chipLabels[i] = Text(chips[i].transform,"Label",label(i),20,0,34,TextAnchor.MiddleLeft,34,10);
            }

            communication = RectObject(hud.transform,"Communication").gameObject;
            Stretch((RectTransform)communication.transform);
            RectTransform normalCard = CenterCard(communication.transform,"CommunicationWordStage",180);
            DockWordStage(normalCard,180,128);
            Text(normalCard,"Caption","伝える言葉",22,16,32,TextAnchor.MiddleLeft).color = Muted;
            communicationBadge = Text(normalCard,"Badge","待機中",20,16,32,TextAnchor.MiddleRight);
            communicationBadge.rectTransform.anchorMin = new Vector2(.68f,1);
            message = Text(normalCard,"Message","マーカーを見せてください",112,48,122,TextAnchor.MiddleCenter);
            message.fontStyle = FontStyles.Bold;
            message.fontSizeMin = 40;
            RectTransform normalFooter = Card(communication.transform,"CommunicationControls",100,false,20);
            communicationHint = Text(normalFooter,"Hint","1枚ずつ、または2枚同時にカメラへ",22,26,48,TextAnchor.MiddleCenter);
            communicationHint.color = Muted;

            practice = RectObject(hud.transform,"Practice").gameObject;
            Stretch((RectTransform)practice.transform);
            RectTransform questionCard = CenterCard(practice.transform,"PracticeWordStage",280);
            questionStage = questionCard;
            questionCaption = Text(questionCard,"Caption","この言葉をマーカーで伝えてみよう",22,16,32,TextAnchor.MiddleLeft);
            questionCaption.color = Muted;
            progress = Text(questionCard,"Progress","",22,16,32,TextAnchor.MiddleRight);
            progress.rectTransform.anchorMin = new Vector2(.76f,1);
            question = Text(questionCard,"Question","",112,50,180,TextAnchor.MiddleCenter);
            question.fontStyle = FontStyles.Bold;
            question.fontSizeMin = 40;
            RectTransform practiceCard = Card(practice.transform,"PracticeControls",280,false,20);
            feedback = Text(practiceCard,"Feedback","",32,8,56,TextAnchor.MiddleCenter);
            instruction = Text(practiceCard,"Instruction","",22,70,50,TextAnchor.MiddleCenter);
            instruction.color = Muted;
            RectTransform hintRow = RectObject(practiceCard,"HintRow"); Place(hintRow,0,1,126,60,28,28);
            practiceHint = Text(hintRow,"HintText","",20,0,60,TextAnchor.MiddleCenter,0,0);
            for (int i = 0; i < 2; i++) hintImages[i] = Preview(hintRow,"HintMarker"+i,null,12+i*70,0,60);
            answerButtons = RectObject(practiceCard,"AnswerButtons").gameObject; Stretch((RectTransform)answerButtons.transform);
            hintButton = ButtonAt(answerButtons.transform,"HintButton","ヒント",toggleHint,.025f,.33f,198,64,24);
            hintButtonLabel = hintButton.GetComponentInChildren<TextMeshProUGUI>();
            skipButton = ButtonAt(answerButtons.transform,"SkipButton","あとで練習",skip,.335f,.65f,198,64,24);
            nextButton = ButtonAt(answerButtons.transform,"NextButton","次の問題へ",next,.655f,.975f,198,64,24,true);
            resultButtons = RectObject(practiceCard,"ResultButtons").gameObject; Stretch((RectTransform)resultButtons.transform);
            reviewButton = ButtonAt(resultButtons.transform,"ReviewButton","苦手な言葉を復習",review,.025f,.495f,198,64,24,true);
            ButtonAt(resultButtons.transform,"RestartButton","もう一度練習",()=>startPractice(lastSingles),.505f,.975f,198,64,24);
            Image track = Image(practiceCard,"ProgressTrack",Surface); Place(track.rectTransform,0,1,270,4,30,30);
            progressFill = Image(track.transform,"Fill",Mint); Stretch(progressFill.rectTransform);

            menu = FullPanel("ModeSelection");
            RectTransform menuCard = CenterCard(menu.transform,"Welcome",748);
            Text(menuCard,"AppName","Sure手話",64,26,80,TextAnchor.MiddleCenter).fontStyle = FontStyles.Bold;
            Text(menuCard,"Title","６種類のマーカーの組み合わせで気持ちを伝えよう！",32,112,54,TextAnchor.MiddleCenter);
            Image practiceChoice = Image(menuCard,"PracticeChoice",Surface); Place(practiceChoice.rectTransform,0,1,198,202,32,32);
            Text(practiceChoice.transform,"Title","練習モード",30,14,44,TextAnchor.MiddleCenter).fontStyle = FontStyles.Bold;
            Text(practiceChoice.transform,"Description","出された言葉に対応するマーカーを出そう！",22,65,40,TextAnchor.MiddleCenter).color = Muted;
            ButtonAt(practiceChoice.transform,"Singles","1枚から（6問）",()=>startPractice(true),.025f,.495f,124,62,24,true);
            ButtonAt(practiceChoice.transform,"All","組み合わせも（16問）",()=>startPractice(false),.505f,.975f,124,62,24,true);
            ButtonAt(menuCard,"CommunicationChoice","本番モード",startCommunication,0,1,424,88,30);
            if (startTown != null) ButtonAt(menuCard,"TownChoice","まちで会話チャレンジ · 90秒",startTown,0,1,530,88,28,true);
            ButtonAt(menuCard,"GuideChoice","対応表を見て覚える",toggleGuide,.15f,.85f,650,60,24);

            guide = FullPanel("MarkerGuide");
            RectTransform guideCard = CenterCard(guide.transform,"GuideCard",790);
            Text(guideCard,"Title","言葉とマーカーの対応表",32,24,52,TextAnchor.MiddleLeft).rectTransform.anchorMax = new Vector2(.76f,1);
            ButtonAt(guideCard,"CloseGuide","閉じる",toggleGuide,.77f,.98f,24,50,24);
            Text(guideCard,"Explanation","色と番号を組み合わせて伝えます。左右の順番は自由です。",21,88,44,TextAnchor.MiddleLeft).color = Muted;
            BuildGuide(guideCard,rules);
            Text(guideCard,"Footer","上下にスクロールできます / 登録のない組み合わせは出題しません",19,741,32,TextAnchor.MiddleCenter).color = Muted;
            ShowMode(MarkerAppMode.Menu); ShowGuide(false); UpdateSafeArea();
        }

        public void ShowMode(MarkerAppMode mode)
        {
            menu.SetActive(mode == MarkerAppMode.Menu); hud.SetActive(mode == MarkerAppMode.Communication || mode == MarkerAppMode.Practice);
            communication.SetActive(mode == MarkerAppMode.Communication); practice.SetActive(mode == MarkerAppMode.Practice);
            modeTitle.text = mode == MarkerAppMode.Practice ? "練習モード" : "本番モード";
            previousMessage = "";
            // Clear stale content before the next camera frame.
            message.text = "マーカーを見せてください";
        }

        public void ShowGuide(bool open)
        {
            guide.SetActive(open);

            if (open)
            {
                guide.transform.SetAsLastSibling();
            }
        }
        public void SetPracticeCourse(bool singlesOnly) { lastSingles = singlesOnly; }

        public void UpdateSafeArea()
        {
            Rect area = Screen.safeArea; Vector2 screen = new Vector2(Screen.width,Screen.height);
            if (screen.x <= 0 || screen.y <= 0) return;
            if (area != lastSafeArea || screen != lastScreen)
            {
                safe.anchorMin = new Vector2(area.xMin/screen.x,area.yMin/screen.y);
                safe.anchorMax = new Vector2(area.xMax/screen.x,area.yMax/screen.y);
                lastSafeArea = area; lastScreen = screen;
                Canvas.ForceUpdateCanvases();
            }
            foreach (RectTransform card in centeredCards)
                card.sizeDelta = new Vector2(Mathf.Min(1000,Mathf.Max(300,safe.rect.width-48)),card.sizeDelta.y);
        }

        public void UpdateRecognition(int mask, string text, bool simulated)
        {
            status.text = text; status.color = simulated ? new Color(1f,.82f,.29f) : Muted;
            for (int i = 0; i < chips.Length; i++)
            {
                bool tracked = (mask & (1 << i)) != 0;
                chips[i].color = tracked ? new Color(.10f,.29f,.25f) : Surface;
                dots[i].color = tracked ? MarkerColors[i] : new Color(.38f,.44f,.51f);
                chipLabels[i].text = label(i) + (tracked ? " ●" : "");
                chipLabels[i].color = tracked ? Color.white : Muted;
            }
        }

        public void UpdateCommunication(string word, int mask, bool tooMany, bool confirming)
        {
            bool hasWord = !string.IsNullOrEmpty(word);
            communicationBadge.text = hasWord ? "表示中" : mask == 0 ? "待機中" : tooMany ? "2枚まで" : confirming ? "確認中" : "未登録";
            communicationBadge.color = hasWord ? Mint : Muted;
            message.text = hasWord ? word : mask == 0 ? "マーカーを見せてください" : tooMany ? "マーカーは2枚までです" : confirming ? "読み取り中…" : "この組み合わせは未登録です";
            message.fontSizeMax = hasWord ? 112 : 46;
            Color color = hasWord ? Color.white : Muted;
            if (hasWord && word != previousMessage) color.a = .3f;
            else color.a = Mathf.MoveTowards(message.color.a,1f,Time.unscaledDeltaTime*7);
            message.color = color; previousMessage = word;
            communicationHint.text = mask == 0 ? "1枚ずつ、または2枚同時にカメラへ" : tooMany ? "見せるマーカーを1枚か2枚にしてください" : confirming ? "そのまま少し待ってください" : hasWord ? "見せるマーカーを変えると、言葉が切り替わります" : "対応表から別の組み合わせを試してください";
        }

        public void UpdatePractice(MarkerPracticeSession session, bool showHint, Func<int,string> describe, string submittedWord)
        {
            bool complete = session.Complete, correct = session.Feedback == PracticeFeedback.Correct;
            bool compact = correct || session.Feedback == PracticeFeedback.Reading;
            if (compact) DockWordStage(questionStage,96,308);
            else
            {
                questionStage.anchorMin = questionStage.anchorMax = questionStage.pivot = new Vector2(.5f,.5f);
                questionStage.anchoredPosition = Vector2.zero;
                questionStage.sizeDelta = new Vector2(questionStage.sizeDelta.x,280);
            }
            Place(question.rectTransform,0,1,compact ? 36 : 50,compact ? 52 : 180,24,24);
            Place(questionCaption.rectTransform,0,.76f,compact ? 4 : 16,compact ? 28 : 32,24,24);
            Place(progress.rectTransform,.76f,1,compact ? 4 : 16,compact ? 28 : 32,24,24);
            answerButtons.SetActive(!complete); resultButtons.SetActive(complete);
            nextButton.interactable = correct; hintButton.interactable = skipButton.interactable = !correct;
            reviewButton.interactable = session.ReviewCount > 0;
            progress.text = complete ? "終了" : session.Number + " / " + session.Total;
            question.text = complete ? "練習おつかれさま！" : session.Current.Message;
            question.fontSizeMax = complete ? 56 : 112;
            feedback.color = correct || complete ? Mint : session.Feedback == PracticeFeedback.Incorrect ? Error : Muted;
            feedback.text = complete ? "○ 正解 " + session.CorrectCount + " / " + session.Total :
                correct ? "○ 正解！" : session.Feedback == PracticeFeedback.Incorrect ? "× もう一度やってみよう" :
                session.Feedback == PracticeFeedback.Reading ? "読み取り中…" : "マーカーで答えてください";
            instruction.text = complete ? "ヒントなしで一発正解：" + session.FirstTryCount + "問 / あとで練習：" + session.SkippedCount + "問" :
                correct ? "合っています！「次の問題へ」を押してください" : session.Feedback == PracticeFeedback.WaitingForClear ? "まず、すべてのマーカーをカメラから隠してください" :
                session.Feedback == PracticeFeedback.Incorrect ? (string.IsNullOrEmpty(submittedWord) ? "この組み合わせは登録されていません。" : "今の答え：「"+submittedWord+"」") + "\n一度すべて隠してから、もう一度見せてください" :
                "合うマーカーを1枚か2枚見せて、少し止めてください";
            int answerMask = !complete && (showHint || correct) ? session.Current.Mask : 0;
            practiceHint.text = complete ? (session.ReviewCount == 0 ? "すべてヒントなしで一発正解できました！" : "間違えた・ヒントを使った・あとに回した言葉を復習できます（"+session.ReviewCount+"問）") :
                answerMask != 0 ? describe(answerMask) : "覚えていなければ「ヒント」を押せます";
            hintButtonLabel.text = showHint ? "ヒントを閉じる" : "ヒント";
            int imageIndex = 0;
            for (int i = 0; i < 6 && imageIndex < 2; i++)
            {
                if ((answerMask & (1 << i)) == 0) continue;
                hintImages[imageIndex].texture = previews[i]; hintImages[imageIndex].gameObject.SetActive(true); imageIndex++;
            }
            for (int i = imageIndex; i < 2; i++) hintImages[i].gameObject.SetActive(false);
            practiceHint.rectTransform.offsetMin = new Vector2(imageIndex == 0 ? 0 : 164, -60);
            float done = complete ? 1 : (session.Number-1+(correct ? 1 : 0))/(float)Math.Max(1,session.Total);
            progressFill.rectTransform.anchorMax = new Vector2(done,1);
        }

        private void BuildGuide(RectTransform parent, MarkerMessageRule[] rules)
        {
            RectTransform scrollRect = RectObject(parent,"GuideScroll"); Place(scrollRect,0,1,148,575,24,24);
            ScrollRect scroll = scrollRect.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            Image viewportImage = Image(scrollRect,"Viewport",Color.clear); Stretch(viewportImage.rectTransform);
            viewportImage.raycastTarget = true; viewportImage.gameObject.AddComponent<RectMask2D>();
            RectTransform content = RectObject(viewportImage.transform,"Content");
            content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1);
            int row = 0; var seen = new HashSet<int>();
            if (rules != null) foreach (MarkerMessageRule rule in rules)
            {
                if (rule == null || !rule.TryGetMask(out int mask) || !seen.Add(mask) || string.IsNullOrWhiteSpace(rule.message)) continue;
                Image card = Image(content,"Rule"+mask,Surface); Place(card.rectTransform,0,1,row*114,104,0,0);
                int previewIndex = 0; string names = "";
                for (int i = 0; i < 6; i++) if ((mask & (1<<i)) != 0)
                {
                    Preview(card.transform,"Marker"+i,previews[i],14+previewIndex*76,20,64); previewIndex++;
                    names += (names.Length == 0 ? "" : " ＋ ") + label(i);
                }
                Text(card.transform,"Markers",names,19,12,32,TextAnchor.MiddleLeft,180,16).color = Muted;
                Text(card.transform,"Word",rule.message,30,46,48,TextAnchor.MiddleLeft,180,16);
                row++;
            }
            content.sizeDelta = new Vector2(0,Mathf.Max(575,row*114)); content.anchoredPosition = Vector2.zero;
            scroll.viewport = viewportImage.rectTransform; scroll.content = content;
            scroll.scrollSensitivity = 35;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            GameObject events = new GameObject("MarkerUIEventSystem",typeof(EventSystem)); events.transform.SetParent(Root.transform,false);
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
        }

        private GameObject FullPanel(string name)
        {
            Image panel = Image(safe,name,Ink); Stretch(panel.rectTransform); panel.raycastTarget = true; return panel.gameObject;
        }

        private RectTransform CenterCard(Transform parent,string name,float height)
        {
            Image panel = Image(parent,name,Ink); RectTransform rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(1000,height);
            effectObstacles.Add(rect);
            centeredCards.Add(rect); return rect;
        }

        private static void DockWordStage(RectTransform rect,float height,float bottom)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,0);
            rect.anchoredPosition = new Vector2(0,bottom);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x,height);
        }

        private RectTransform Card(Transform parent,string name,float height,bool top,float margin)
        {
            Image panel = Image(parent,name,Ink); RectTransform rect = panel.rectTransform;
            effectObstacles.Add(rect);
            float y = top ? 1 : 0; rect.anchorMin = new Vector2(0,y); rect.anchorMax = new Vector2(1,y); rect.pivot = new Vector2(.5f,y);
            rect.offsetMin = new Vector2(24,top ? -margin-height : margin);
            rect.offsetMax = new Vector2(-24,top ? -margin : margin+height); return rect;
        }

        private Image Image(Transform parent,string name,Color color)
        {
            RectTransform rect = RectObject(parent,name); Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = rounded; image.type = UnityEngine.UI.Image.Type.Sliced; image.color = color; image.raycastTarget = false; return image;
        }

        private TextMeshProUGUI Text(Transform parent,string name,string value,int size,float top,float height,TextAnchor alignment,float left=24,float right=24)
        {
            RectTransform rect = RectObject(parent,name); Place(rect,0,1,top,height,left,right);
            // Assign the Japanese font before TMP's Awake tries to load a default font.
            rect.gameObject.SetActive(false);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment == TextAnchor.MiddleLeft ? TextAlignmentOptions.MidlineLeft :
                alignment == TextAnchor.MiddleRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Midline;
            text.raycastTarget = false; text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Truncate;
            text.enableAutoSizing = true; text.fontSizeMin = Math.Min(size,18); text.fontSizeMax = size;
            text.extraPadding = true;
            rect.gameObject.SetActive(true); return text;
        }

        private Button ButtonAt(Transform parent,string name,string title,Action click,float min,float max,float top,float height,int size,bool primary=false)
        {
            Image image = Image(parent,name,primary ? Mint : Surface); Place(image.rectTransform,min,max,top,height,20,20); image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(()=>click());
            ColorBlock colors = button.colors; colors.highlightedColor = new Color(.85f,.94f,1f);
            colors.pressedColor = new Color(.65f,.75f,.80f); colors.disabledColor = new Color(.48f,.48f,.48f,.75f); button.colors = colors;
            TextMeshProUGUI text = Text(image.transform,"Text",title,size,0,height,TextAnchor.MiddleCenter,12,12); text.fontStyle = FontStyles.Bold;
            if (primary) text.color = Ink; return button;
        }

        private RawImage Preview(Transform parent,string name,Texture texture,float left,float top,float size)
        {
            RectTransform rect = RectObject(parent,name); rect.anchorMin = rect.anchorMax = new Vector2(0,1); rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(left,-top); rect.sizeDelta = new Vector2(size,size);
            RawImage image = rect.gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false; return image;
        }

        private static RectTransform RectObject(Transform parent,string name)
        {
            var obj = new GameObject(name,typeof(RectTransform)); obj.transform.SetParent(parent,false); return (RectTransform)obj.transform;
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        private static void Place(RectTransform rect,float min,float max,float top,float height,float left=0,float right=0)
        {
            rect.anchorMin = new Vector2(min,1); rect.anchorMax = new Vector2(max,1); rect.pivot = new Vector2(.5f,1);
            rect.offsetMin = new Vector2(left,-top-height); rect.offsetMax = new Vector2(-right,-top);
        }
        private static Texture2D MakeRoundedTexture()
        {
            // Four samples per UI unit, with a one-unit antialiased edge.
            var texture = new Texture2D(256,256,TextureFormat.RGBA32,false);
            texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[256*256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x+.5f-128)-48,0), dy = Mathf.Max(Mathf.Abs(y+.5f-128)-48,0);
                pixels[y*256+x] = new Color(1,1,1,Mathf.Clamp01((80-Mathf.Sqrt(dx*dx+dy*dy))/4+.5f));
            }
            texture.SetPixels(pixels); texture.Apply(false,true); return texture;
        }
        private static TMP_FontAsset CreateDisplayFont(Font displayFont)
        {
            // SDF glyphs stay sharp as the Canvas scales; only the characters used are added to the atlas.
            TMP_FontAsset asset = displayFont != null ? TMP_FontAsset.CreateFontAsset(displayFont) : null;
            if (asset == null)
            {
                string[] installed = Font.GetOSInstalledFontNames();
                string[] preferred = { "Yu Gothic", "Yu Gothic UI", "Meiryo", "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans JP" };
                foreach (string family in preferred)
                {
                    if (!Array.Exists(installed,name => name == family)) continue;
                    asset = TMP_FontAsset.CreateFontAsset(family,"Regular");
                    if (asset != null) break;
                }
            }
            if (asset == null) throw new InvalidOperationException("日本語フォントが見つかりません。MarkerMessageController の Display Font に日本語フォントを設定してください。");
            asset.name = "Yubi Japanese SDF";
            foreach (Texture2D atlas in asset.atlasTextures) atlas.filterMode = FilterMode.Bilinear;
            return asset;
        }
        public void Dispose()
        {
            Release(font); // TMP also releases its generated atlas textures and material.
            Release(rounded); Release(roundTexture);
        }
        private static void Release(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
