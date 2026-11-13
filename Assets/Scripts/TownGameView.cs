using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Yubi
{
    // Render the authored town separately while Vuforia keeps the real camera running.
    public sealed class TownGameView : IDisposable
    {
        private static readonly Color Ink = new Color(.055f,.10f,.16f);
        private static readonly Color Mint = new Color(.40f,.94f,.76f);
        private readonly GameObject root, world;
        private readonly Camera townCamera;
        private readonly RenderTexture texture;
        private readonly TMP_FontAsset font;
        private readonly Transform player;
        private readonly TMP_Text clock, title, speech, hint, status;
        private readonly GameObject conversationPanel;
        private readonly Button leave;
        private readonly GameObject results;
        private readonly TMP_Text resultText;
        private readonly Image progress;
        private readonly RawImage map;
        private readonly RectTransform recognizedMessagePanel;
        private readonly CanvasGroup recognizedMessageGroup;
        private readonly TMP_Text recognizedMessageText;
        private const float MessageRecognitionSeconds = .2f;
        private const float MessagePopupSeconds = 2.4f;
        private string messageCandidate = "", lastRecognizedMessage = "";
        private float messageCandidateSeconds, messagePopupTime = MessagePopupSeconds;
        private Vector3 travelStart;
        private bool wasWalking;
        private float animationTime;
        private Camera arCamera;
        private bool removedLayer;
        private const int TownLayer = 29;
        private Rect originalCameraRect;
        private bool changedCameraRect;     
        private static readonly Vector3 Origin = new Vector3(10000, 0, 10000);
        private readonly Vector3[] Places = new Vector3[TownConversationSession.Names.Length];
        private readonly Vector3[] destinations = new Vector3[TownConversationSession.Names.Length];
        private readonly Animator[] residentAnimators = new Animator[TownConversationSession.Names.Length];
        private static readonly int GreetTrigger = Animator.StringToHash("Greet");
        private static readonly int SuccessTrigger = Animator.StringToHash("Success");
        private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle_Stance_01");
        private TownPhase previousPhase = TownPhase.Exploring;
        private int previousResident = -1;
        private bool resetResidentAnimations = true;
        private Vector3 startPosition;
        private Quaternion startRotation = Quaternion.identity;

        public GameObject Root { get { return root; } }
        public Camera TownCamera { get { return townCamera; } }

        public TownGameView(Transform parent,
            TMP_FontAsset displayFont,
            Action menu,
            Action restart,
            Action toggleGuide,
            Action onLeave,
            GameObject townPrefab)
        {
            if (townPrefab == null) throw new ArgumentNullException(nameof(townPrefab));
            font = displayFont;
            root = Rect(parent,"TownChallenge").gameObject;
            Fill((RectTransform)root.transform);
            Image rootBackground = Paint(root, Color.clear);
            rootBackground.raycastTarget = false;
            RectTransform mapRect = Rect(root.transform, "TownMap");

            mapRect.anchorMin = new Vector2(0, 0);
            mapRect.anchorMax = new Vector2(0.5f, 1);

            // 上下の余白をなくす
            mapRect.offsetMin = Vector2.zero;
            mapRect.offsetMax = Vector2.zero;
            map = mapRect.gameObject.AddComponent<RawImage>(); map.raycastTarget = false;

            world = new GameObject("ConversationTownWorld"); world.transform.position = Origin;
            GameObject cameraObject = new GameObject("TownCamera",typeof(Camera)); cameraObject.transform.SetParent(world.transform,false);
            townCamera = cameraObject.GetComponent<Camera>();
            townCamera.orthographic = false;
            townCamera.fieldOfView = 65f;
            townCamera.transform.localPosition = new Vector3(0f,1.6f,-3f);
            townCamera.transform.localRotation = Quaternion.identity;
            townCamera.nearClipPlane = .1f; townCamera.farClipPlane = 500;
            townCamera.clearFlags = CameraClearFlags.SolidColor;
            townCamera.backgroundColor = new Color(.70f,.86f,.91f);
            townCamera.cullingMask = 1 << TownLayer;
            texture = new RenderTexture(1280,720,24) { name = "TownPreview", antiAliasing = 1 };
            texture.Create(); townCamera.targetTexture = texture; map.texture = texture;
            LoadTown(townPrefab);
            player = new GameObject("PlayerViewPoint").transform;
            player.SetParent(world.transform,false);

            TMP_Text heading = Text(root.transform,"Heading","町で会話チャレンジ！",30);
            heading.fontStyle = FontStyles.Bold;
            heading.color = Color.black;
            Place(heading.rectTransform, 0, .30f, 12, 42, 16, 0);
            clock = Text(root.transform,"Clock","",28);
            clock.fontStyle = FontStyles.Bold;
            clock.color = Color.black;
            Place(clock.rectTransform, .30f, .50f, 12, 42, 0, 16);
            Button back = Button(
                root.transform,
                "TownMenu",
                "メニュー",
                menu);

            RectTransform backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(0f, 0f);
            backRect.anchorMax = new Vector2(0.2f, 0f);
            backRect.pivot = Vector2.zero;
            backRect.offsetMin = new Vector2(16f, 16f);
            backRect.offsetMax = new Vector2(-4f, 76f);


            Button guideButton = Button(
                root.transform,
                "TownGuide",
                "対応表",
                toggleGuide);

            RectTransform guideRect = (RectTransform)guideButton.transform;
            guideRect.anchorMin = new Vector2(0.2f, 0f);
            guideRect.anchorMax = new Vector2(0.4f, 0f);
            guideRect.pivot = Vector2.zero;
            guideRect.offsetMin = new Vector2(4f, 16f);
            guideRect.offsetMax = new Vector2(-4f, 76f);
            RectTransform panel = Rect(root.transform,"Conversation");
            conversationPanel = panel.gameObject;
            panel.anchorMin = Vector2.zero; panel.anchorMax = new Vector2(.5f,0); panel.pivot = Vector2.zero;
            panel.anchoredPosition = new Vector2(0,88);
            panel.sizeDelta = new Vector2(0,294); Paint(panel.gameObject,new Color(.09f,.15f,.22f,.94f));
            title = Text(panel,"Speaker","",26); Place(title.rectTransform,0,.72f,12,40,24,0);
            leave = Button(panel,"LeaveConversation","別の人へ",onLeave); Place((RectTransform)leave.transform,.76f,1,12,42,0,20);
            speech = Text(panel,"Speech","",36); Place(speech.rectTransform,0,1,61,70,24,24);
            speech.fontStyle = FontStyles.Bold;
            hint = Text(panel,"Hint","",23); Place(hint.rectTransform,0,1,137,62,24,24); hint.color = Mint;
            status = Text(panel,"InputStatus","",19); Place(status.rectTransform,0,1,214,44,24,24);
            RectTransform bar = Rect(panel,"AnswerTrack"); Place(bar,0,1,276,6,24,24); Paint(bar.gameObject,Ink);
            RectTransform fill = Rect(bar,"AnswerProgress"); Fill(fill); progress = Paint(fill.gameObject,Mint);

            recognizedMessagePanel = Rect(root.transform,"RecognizedMessage");
            recognizedMessagePanel.anchorMin = new Vector2(.54f,.16f);
            recognizedMessagePanel.anchorMax = new Vector2(.96f,.16f);
            recognizedMessagePanel.pivot = new Vector2(.5f,.5f);
            recognizedMessagePanel.sizeDelta = new Vector2(0,110);
            Paint(recognizedMessagePanel.gameObject,new Color(.09f,.24f,.27f,.9f)).raycastTarget = false;
            recognizedMessageGroup = recognizedMessagePanel.gameObject.AddComponent<CanvasGroup>();
            recognizedMessageGroup.alpha = 0;
            recognizedMessageGroup.interactable = false;
            recognizedMessageGroup.blocksRaycasts = false;
            recognizedMessageText = Text(recognizedMessagePanel,"Message","",36);
            recognizedMessageText.fontStyle = FontStyles.Bold;
            recognizedMessageText.alignment = TextAlignmentOptions.Center;
            Fill(recognizedMessageText.rectTransform);
            recognizedMessageText.rectTransform.offsetMin = new Vector2(18,12);
            recognizedMessageText.rectTransform.offsetMax = new Vector2(-18,-12);
            recognizedMessagePanel.gameObject.SetActive(false);

            results = Rect(root.transform,"TownResults").gameObject; Fill((RectTransform)results.transform);
            Paint(results,new Color(.035f,.06f,.10f,.97f));
            RectTransform card = Rect(results.transform,"ResultCard");
            card.anchorMin = new Vector2(.05f,.5f); card.anchorMax = new Vector2(.95f,.5f);
            card.sizeDelta = new Vector2(0,450); card.pivot = new Vector2(.5f,.5f);
            TMP_Text finish = Text(card,"ResultTitle","チャレンジ終了！",48); Place(finish.rectTransform,0,1,0,70,12,12);
            finish.alignment = TextAlignmentOptions.Center;
            resultText = Text(card,"ResultScore","",36); Place(resultText.rectTransform,0,1,90,190,12,12); resultText.alignment = TextAlignmentOptions.Center;
            Button retry = Button(card,"RetryTown","もう一度挑戦",restart); Place((RectTransform)retry.transform,0,.49f,330,72,0,0);
            Button end = Button(card,"ExitTown","メニューへ",menu); Place((RectTransform)end.transform,.51f,1,330,72,0,0);
            conversationPanel.SetActive(false);
            ResetPlayer(); SetActive(false);
        }

        public void ResetPlayer()
        {
            player.localPosition = startPosition; player.localRotation = startRotation;
            townCamera.transform.localPosition = startPosition + Vector3.up * 1.6f;
            townCamera.transform.localRotation = startRotation;
            wasWalking = false; animationTime = 0; results.SetActive(false);
            previousPhase = TownPhase.Exploring;
            previousResident = -1;
            // Reset after the town is active, so restarting from the menu also works.
            resetResidentAnimations = true;
            messageCandidate = lastRecognizedMessage = "";
            messageCandidateSeconds = 0;
            messagePopupTime = MessagePopupSeconds;
            recognizedMessageGroup.alpha = 0;
            recognizedMessagePanel.gameObject.SetActive(false);
            conversationPanel.SetActive(false);
        }

        public void SetActive(bool active)
        {
            if (root != null) root.SetActive(active);
            if (world != null) world.SetActive(active);

            if (active && (root == null || world == null)) return;

            if (active)
            {
                arCamera = Camera.main;

                if (arCamera != null)
                {
                    // 元の表示範囲を覚えて、右半分へ変更
                    if (!changedCameraRect)
                    {
                        originalCameraRect = arCamera.rect;
                        changedCameraRect = true;
                    }

                    arCamera.rect = new Rect(
                        0.5f, // 左端
                        0f,   // 下端
                        0.5f, // 横幅
                        1f    // 高さ
                    );

                    // 町専用のLayerをARCameraには描かせない
                    if (!removedLayer &&
                        (arCamera.cullingMask & (1 << TownLayer)) != 0)
                    {
                        arCamera.cullingMask &= ~(1 << TownLayer);
                        removedLayer = true;
                    }
                }
            }
            else
            {
                if (arCamera != null)
                {
                    // 通常モードへ戻るとき全画面表示に復元
                    if (changedCameraRect)
                    {
                        arCamera.rect = originalCameraRect;
                        changedCameraRect = false;
                    }

                    if (removedLayer)
                    {
                        arCamera.cullingMask |= 1 << TownLayer;
                        removedLayer = false;
                    }
                }
            }
        }
        public void Update(TownConversationSession session, string recognition, string inputMessage)
        {
            UpdateResidentAnimations(session);
            UpdateRecognizedMessage(session.Phase == TownPhase.Finished ? "" : inputMessage,
                session.Phase == TownPhase.Finished);
            animationTime += Time.unscaledDeltaTime;
            Canvas.ForceUpdateCanvases();
            float aspect = map.rectTransform.rect.width / Mathf.Max(1,map.rectTransform.rect.height);
            townCamera.aspect = aspect;
            bool walking = session.Phase == TownPhase.Walking;
            if (walking && !wasWalking) travelStart = player.localPosition;
            if (walking)
            {
                Vector3 destination = destinations[session.Resident];
                Vector3 direction = destination - travelStart;
                if (direction.sqrMagnitude > .01f) player.localRotation = Quaternion.LookRotation(-direction);
                player.localPosition = Vector3.Lerp(travelStart,destination,session.TravelProgress) + Vector3.up * Mathf.Abs(Mathf.Sin(animationTime*12))*.08f;
            }
            else if (wasWalking && session.Resident >= 0)
            { player.localPosition = destinations[session.Resident]; }
            wasWalking = walking;

            Vector3 cameraPosition = player.localPosition + Vector3.up * 1.6f;
            townCamera.transform.localPosition = cameraPosition;

            Vector3 lookTarget = session.Resident >= 0
                ? Places[session.Resident] + Vector3.up * 1.6f
                : cameraPosition + startRotation * Vector3.forward * 5f;
            Vector3 lookDirection = lookTarget - cameraPosition;

            if (lookDirection.sqrMagnitude > .001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection,Vector3.up);
                townCamera.transform.localRotation = Quaternion.Slerp(
                    townCamera.transform.localRotation,
                    targetRotation,
                    Time.unscaledDeltaTime * 5f);
            }

            clock.text = "残り " + Mathf.CeilToInt(session.Remaining) + " 秒";
            status.text = recognition + (string.IsNullOrEmpty(inputMessage) ? "" : "  → " + inputMessage);
            progress.rectTransform.anchorMax = new Vector2(session.AnswerProgress,1);
            conversationPanel.SetActive(session.Phase == TownPhase.ClearMarkers ||
                session.Phase == TownPhase.Answering || session.Phase == TownPhase.Feedback);
            leave.gameObject.SetActive(session.Phase == TownPhase.ClearMarkers || session.Phase == TownPhase.Answering);
            title.text = session.Resident < 0 ? "広場を歩いて話しかけよう" : TownConversationSession.Names[session.Resident];
            string answerHint = session.AnswerHintVisible
                ? TownConversationSession.Hints[session.Question]
                : "ヒントはあと" + Mathf.CeilToInt(TownConversationSession.HintDelaySeconds - session.ConversationSeconds) + "秒で表示します。";
            switch (session.Phase)
            {
                case TownPhase.ClearMarkers:
                    speech.text = TownConversationSession.Prompts[session.Question];
                    hint.text = (session.WrongAnswer
                        ? "その返事は伝わらなかったみたい。別の返答のマーカーを見せてね。\n"
                        : "一度マーカーを全部隠してね。\n") + answerHint; break;
                case TownPhase.Answering:
                    speech.text = TownConversationSession.Prompts[session.Question];
                    hint.text = answerHint + "\n返答を約0.6秒見せてください。"; break;
                case TownPhase.Feedback:
                    speech.text = session.Reply; hint.text = "会話成立！ +100点\n次は別の住人と話してみよう。"; break;
            }
            results.SetActive(session.Phase == TownPhase.Finished);
            if (session.Phase == TownPhase.Finished)
                resultText.text = session.Conversations + " 回の会話ができました\n<size=64>" + session.Score + " 点</size>\n<size=24>" + (session.Conversations == 0 ? "次はヒントを見ながら1回の会話に挑戦！" : "気持ちが伝わったね！ また町へ出かけよう。") + "</size>";
        }

        private void UpdateRecognizedMessage(string message, bool finished)
        {
            if (finished)
            {
                recognizedMessagePanel.gameObject.SetActive(false);
                return;
            }

            message = message ?? "";
            float delta = Time.unscaledDeltaTime;
            if (message != messageCandidate)
            {
                messageCandidate = message;
                messageCandidateSeconds = 0;
            }
            else
            {
                messageCandidateSeconds += delta;
                if (messageCandidateSeconds >= MessageRecognitionSeconds && message != lastRecognizedMessage)
                {
                    lastRecognizedMessage = message;
                    if (message.Length > 0)
                    {
                        recognizedMessageText.text = message;
                        messagePopupTime = 0;
                        recognizedMessagePanel.gameObject.SetActive(true);
                    }
                }
            }

            messagePopupTime += delta;
            if (messagePopupTime >= MessagePopupSeconds)
            {
                recognizedMessagePanel.gameObject.SetActive(false);
                return;
            }
            float fadeIn = Mathf.Clamp01(messagePopupTime / .25f);
            float fadeOut = Mathf.Clamp01((MessagePopupSeconds - messagePopupTime) / .5f);
            recognizedMessageGroup.alpha = Mathf.SmoothStep(0,1,Mathf.Min(fadeIn,fadeOut));
            float rise = Mathf.SmoothStep(0,1,messagePopupTime / MessagePopupSeconds);
            recognizedMessagePanel.anchoredPosition = new Vector2(0,36f * rise);
        }

        private void UpdateResidentAnimations(TownConversationSession session)
        {
            if (resetResidentAnimations)
            {
                foreach (Animator animator in residentAnimators) ResetResidentAnimation(animator);
                resetResidentAnimations = false;
            }

            if (previousResident >= 0 && (previousResident != session.Resident ||
                (session.Phase == TownPhase.Finished && previousPhase != TownPhase.Finished)))
                ResetResidentAnimation(residentAnimators[previousResident]);

            if (session.Resident >= 0 && previousResident == session.Resident)
            {
                Animator animator = residentAnimators[session.Resident];
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    // ClearMarkers also follows a wrong answer; greet only on arrival.
                    if (previousPhase == TownPhase.Walking && session.Phase == TownPhase.ClearMarkers)
                    {
                        animator.ResetTrigger(SuccessTrigger);
                        animator.SetTrigger(GreetTrigger);
                    }
                    else if (previousPhase == TownPhase.Answering && session.Phase == TownPhase.Feedback)
                    {
                        animator.ResetTrigger(GreetTrigger);
                        animator.SetTrigger(SuccessTrigger);
                    }
                }
            }
            previousPhase = session.Phase;
            previousResident = session.Resident;
        }

        private static void ResetResidentAnimation(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.ResetTrigger(GreetTrigger);
            animator.ResetTrigger(SuccessTrigger);
            animator.Play(IdleState, 0, 0f);
        }

        private void LoadTown(GameObject prefab)
        {
            GameObject town = Object.Instantiate(prefab,world.transform,false);
            town.name = "TownWorld";
            town.transform.localPosition = Vector3.zero;
            town.transform.localRotation = Quaternion.identity;
            foreach (Transform child in town.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = TownLayer;
            // Imported preview cameras must not replace the Vuforia camera.
            foreach (Camera camera in town.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (AudioListener listener in town.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;

            Transform start = town.transform.Find("PlayerStart");
            startPosition = world.transform.InverseTransformPoint(start.position);
            startRotation = Quaternion.Inverse(world.transform.rotation) * start.rotation;
            for (int i=0;i<Places.Length;i++)
            {
                Transform resident = town.transform.Find(TownConversationSession.PrefabNames[i]);
                residentAnimators[i] = resident.GetComponentInChildren<Animator>(true);
                if (residentAnimators[i] != null)
                {
                    // Movement belongs to the game; reactions must stay at the authored position.
                    residentAnimators[i].applyRootMotion = false;
                    residentAnimators[i].updateMode = AnimatorUpdateMode.UnscaledTime;
                }
                Places[i] = world.transform.InverseTransformPoint(resident.position);
                Vector3 forward = Vector3.ProjectOnPlane(resident.forward,Vector3.up).normalized;
                destinations[i] = world.transform.InverseTransformPoint(resident.position + forward * 1.8f);
            }
        }
        private static RectTransform Rect(Transform parent,string name)
        { var obj = new GameObject(name,typeof(RectTransform)); obj.transform.SetParent(parent,false); return (RectTransform)obj.transform; }
        private static void Fill(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static void Place(RectTransform r,float min,float max,float top,float height,float left,float right)
        { r.anchorMin=new Vector2(min,1); r.anchorMax=new Vector2(max,1); r.pivot=new Vector2(.5f,1); r.offsetMin=new Vector2(left,-top-height); r.offsetMax=new Vector2(-right,-top); }
        private static Image Paint(GameObject obj,Color color)
        { Image image=obj.AddComponent<Image>(); image.color=color; return image; }
        private TMP_Text Text(Transform parent,string name,string text,int size)
        {
            RectTransform r=Rect(parent,name); var label=r.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=font; label.text=text; label.fontSize=size; label.enableAutoSizing=true; label.fontSizeMin=14; label.fontSizeMax=size;
            label.color=Color.white; label.alignment=TextAlignmentOptions.MidlineLeft; label.raycastTarget=false; return label;
        }
        private Button Button(Transform parent,string name,string text,Action click)
        {
            RectTransform r=Rect(parent,name); Image image=Paint(r.gameObject,new Color(.16f,.32f,.34f));
            Button button=r.gameObject.AddComponent<Button>(); button.targetGraphic=image; button.onClick.AddListener(()=>click());
            ColorBlock colors=button.colors; colors.disabledColor=new Color(.35f,.35f,.35f,.5f); button.colors=colors;
            TMP_Text label=Text(r,"Label",text,24); Fill(label.rectTransform); label.rectTransform.offsetMin=new Vector2(8,4); label.rectTransform.offsetMax=new Vector2(-8,-4); label.alignment=TextAlignmentOptions.Center;
            return button;
        }
        public void Dispose()
        {
            SetActive(false);
            if (townCamera != null) townCamera.targetTexture=null;
            if (map != null) map.texture=null;
            if (texture != null) texture.Release();
            Release(texture);
            Release(world); Release(root);
        }
        private static void Release(Object value)
        { if(Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
