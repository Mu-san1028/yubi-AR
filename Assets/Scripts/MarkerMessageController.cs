using System;
using System.Text;
using UnityEngine;
using Vuforia;

namespace Yubi
{
    public enum MarkerAppMode { Menu, Communication, Practice, Town }

    public class MarkerMessageController : MonoBehaviour
    {
        [Header("Targets in rule order (IDs start at 1)")]
        [SerializeField] private ObserverBehaviour[] targets = new ObserverBehaviour[MarkerMessageRules.MarkerCount];
        [Header("Additional patterns for the same meanings (IDs 1-6)")]
        [SerializeField] private ObserverBehaviour[] additionalTargets = new ObserverBehaviour[MarkerMessageRules.MarkerCount];
        [SerializeField] private Texture2D[] markerPreviews = new Texture2D[MarkerMessageRules.MarkerCount];
        [SerializeField] private string[] markerLabels = { "1 緑：肯定", "2 赤：否定", "3 青：感謝", "4 紫：お願い", "5 黄：手伝う", "ID：待つ" };
        [Header("Exact marker combinations and messages")]
        [SerializeField] private MarkerMessageRule[] rules = MarkerMessageRules.CreateDefaultRules();
        [SerializeField, Min(0f)] private float stableSeconds = 0.25f;
        [SerializeField] private Font displayFont = null;
        [Header("Animated meaning icons near the recognized markers")]
        [SerializeField] private bool showMessageEffects = true;
        [SerializeField, Min(1f)] private float effectSizeInMarkers = 3.5f;
        [Header("Practice: seconds to hold an answer / hide all markers")]
        [SerializeField, Min(0.25f)] private float practiceAnswerSeconds = 0.8f;
        [SerializeField, Min(0f)] private float practiceClearSeconds = 0.25f;
        [Header("Camera-free check (Inspector, during Play)")]
        [SerializeField] private bool useSimulatedMarkers = false;
        [Tooltip("Bits: 1=green, 2=red, 4=blue, 8=purple, 16=yellow, 32=idmarker")]
        [SerializeField, Range(0, MarkerMessageRules.MaximumMask)] private int simulatedMask = 0;

        private MarkerCombinationState state = new MarkerCombinationState();
        private MarkerPracticeSession practice;
        private MarkerAppView view;
        private MarkerMessageEffects effects;
        private TownConversationSession townSession;
        private TownGameView townView;
        [Header("Town world prefab (PlayerStart and six residents)")]
        [SerializeField] private GameObject townPrefab = null;
        [Tooltip("Optional scene copy used for editing; hidden during Play.")]
        [SerializeField] private GameObject townScenePreview = null;
        [Header("Town audio")]
        [SerializeField] private AudioClip townBgm = null;
        [SerializeField] private AudioClip townSuccessSound = null;
        [SerializeField] private AudioClip townFailureSound = null;
        private AudioSource townBgmSource, townFeedbackSource;
        private MarkerAppMode mode;
        private bool guideOpen, hintOpen;
        public MarkerAppMode Mode { get { return mode; } }

        private void Awake()
        {
            if (townPrefab != null && townScenePreview != null) townScenePreview.SetActive(false);
            HideLegacyCubes(targets); HideLegacyCubes(additionalTargets);
            view = new MarkerAppView(transform, displayFont, GetMarkerLabel, rules,
                ShowMenu, StartCommunication, StartPractice, ToggleGuide, ToggleHint, NextQuestion, SkipQuestion, ReviewQuestions, markerPreviews, StartTown);
            effects = new MarkerMessageEffects(transform, targets, additionalTargets);
            townBgmSource = gameObject.AddComponent<AudioSource>();
            townBgmSource.playOnAwake = false;
            townBgmSource.loop = true;
            townBgmSource.spatialBlend = 0f;
            townBgmSource.volume = .3f;
            townFeedbackSource = gameObject.AddComponent<AudioSource>();
            townFeedbackSource.playOnAwake = false;
            townFeedbackSource.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (view != null) { view.Root.SetActive(true); ShowMenu(); }
        }

        private void LateUpdate()
        {
            int mask = useSimulatedMarkers ? simulatedMask : GetVisibleMask();
            view.UpdateSafeArea(); view.UpdateRecognition(mask, GetStatusText(mask), useSimulatedMarkers);
            if (mode == MarkerAppMode.Town)
            {
                if (townSession.Phase == TownPhase.Exploring)
                    townSession.Visit((townSession.LastResident + 1) % TownConversationSession.Names.Length);
                TownPhase previousPhase = townSession.Phase;
                townSession.Update(Time.unscaledDeltaTime, mask);
                if (previousPhase == TownPhase.Answering && townSession.Phase == TownPhase.Feedback)
                    PlayTownFeedback(townSuccessSound);
                else if (previousPhase == TownPhase.Answering &&
                    townSession.Phase == TownPhase.ClearMarkers && townSession.WrongAnswer)
                    PlayTownFeedback(townFailureSound);
                if (townSession.Phase == TownPhase.Finished) townBgmSource.Stop();
                townView.Update(townSession, GetStatusText(mask), MarkerMessageRules.FindMessage(mask, rules));
                if (guideOpen)
                    effects.Clear();
                else
                    UpdateEffects(
                        townSession.Phase == TownPhase.Feedback && mask == townSession.LastAnswer ? mask : 0,
                        townSession.Phase == TownPhase.Answering ? mask : 0,
                        townSession.AnswerProgress);
                return;
            }
            if (guideOpen || mode == MarkerAppMode.Menu) { effects.Clear(); return; }
            if (mode == MarkerAppMode.Communication)
            {
                state.Update(mask, Time.unscaledTime, stableSeconds);
                string message = MarkerMessageRules.FindMessage(state.ConfirmedMask, rules);
                bool tooMany = mask != 0 && !MarkerMessageRules.IsSupportedCombination(mask);
                bool confirming = mask != 0 && state.ConfirmedMask != mask;
                view.UpdateCommunication(message, mask, tooMany, confirming);
                UpdateEffects(!string.IsNullOrWhiteSpace(message) ? state.ConfirmedMask : 0,
                    confirming ? mask : 0, state.ReadingProgress(Time.unscaledTime, stableSeconds));
            }
            else
            {
                practice.Update(mask, Time.unscaledTime, practiceAnswerSeconds, practiceClearSeconds);
                view.UpdatePractice(practice, hintOpen, DescribeMask, MarkerMessageRules.FindMessage(practice.LastAnswerMask, rules));
                // Correct feedback persists after hiding the answer; the AR effect must not.
                bool correctAndVisible = practice.Feedback == PracticeFeedback.Correct && mask == practice.LastAnswerMask;
                UpdateEffects(correctAndVisible ? mask : 0, practice.Feedback == PracticeFeedback.Reading ? mask : 0,
                    practice.ReadingProgress(Time.unscaledTime, practiceAnswerSeconds));
            }
        }

        private void UpdateEffects(int mask, int readingMask = 0, float progress = 0)
        {
            Camera camera = Camera.main;
            effects.SizeInMarkers = effectSizeInMarkers;
            bool narrowEffect = mode == MarkerAppMode.Town ||
                (mode == MarkerAppMode.Practice && camera != null && camera.aspect > 1);
            float fraction = narrowEffect ? .30f : .48f;
            effects.UpdateEffect(showMessageEffects ? mask : 0, Time.unscaledTime, useSimulatedMarkers, camera, fraction);
            if (showMessageEffects && mask == 0 && MarkerMessageRules.IsSupportedCombination(readingMask))
                effects.UpdateReading(readingMask, progress, useSimulatedMarkers, camera);
        }

        private void PlayTownFeedback(AudioClip clip)
        {
            if (clip != null) townFeedbackSource.PlayOneShot(clip);
        }

        private void StopTownAudio()
        {
            if (townBgmSource != null) townBgmSource.Stop();
            if (townFeedbackSource != null) townFeedbackSource.Stop();
        }

        private static void HideLegacyCubes(ObserverBehaviour[] observers)
        {
            if (observers == null) return;
            foreach (ObserverBehaviour observer in observers)
            {
                if (observer == null) continue;
                Transform cube = observer.transform.Find("Cube");
                if (cube != null) cube.gameObject.SetActive(false);
            }
        }

        public void ShowMenu()
        {
            StopTownAudio();
            if (townView != null) townView.SetActive(false);
            if (effects != null) effects.Clear();
            mode = MarkerAppMode.Menu; guideOpen = false;
            state = new MarkerCombinationState(); view.ShowMode(mode); view.ShowGuide(false);
        }

        public void StartCommunication()
        {
            StopTownAudio();
            if (townView != null) townView.SetActive(false);
            if (effects != null) effects.Clear();
            mode = MarkerAppMode.Communication; state = new MarkerCombinationState();
            guideOpen = false; view.ShowGuide(false); view.ShowMode(mode);
        }

        public void StartPractice(bool onlySingles)
        {
            StopTownAudio();
            if (townView != null) townView.SetActive(false);
            if (effects != null) effects.Clear();
            mode = MarkerAppMode.Practice;
            view.SetPracticeCourse(onlySingles);
            practice = new MarkerPracticeSession(rules, onlySingles, Environment.TickCount);
            hintOpen = guideOpen = false; view.ShowGuide(false); view.ShowMode(mode);
        }

        public void StartTown()
        {
            if (townPrefab == null)
            {
                Debug.LogError("Town world prefab is not assigned.", this);
                return;
            }
            if (townPrefab.transform.Find("PlayerStart") == null)
            {
                Debug.LogError("Town prefab is missing a direct child: PlayerStart", this);
                return;
            }
            foreach (string residentName in TownConversationSession.PrefabNames)
                if (townPrefab.transform.Find(residentName) == null)
                {
                    Debug.LogError("Town prefab is missing a direct child: " + residentName, this);
                    return;
                }
            effects.Clear(); guideOpen = false; view.ShowGuide(false);
            mode = MarkerAppMode.Town; view.ShowMode(mode);
            townSession = new TownConversationSession();
            if (townView == null)
                townView = new TownGameView(
                view.EffectParent,
                view.DisplayFont,
                ShowMenu,
                StartTown,
                ToggleGuide,
                () => townSession.LeaveConversation(),
                townPrefab);
            townView.ResetPlayer(); townView.SetActive(true);
            townView.Update(townSession, GetStatusText(0), "");
            if (townBgm != null)
            {
                if (townBgmSource.clip != townBgm) townBgmSource.clip = townBgm;
                if (!townBgmSource.isPlaying) townBgmSource.Play();
            }
        }

        public void ToggleGuide()
        {
            if (effects != null) effects.Clear();
            guideOpen = !guideOpen;
            if (guideOpen && mode == MarkerAppMode.Practice) practice.MarkHintUsed();
            if (!guideOpen)
            {
                // Reading the guide must never count as stable camera input.
                state = new MarkerCombinationState();
                if (mode == MarkerAppMode.Practice) practice.RestartObservation();
            }
            view.ShowGuide(guideOpen);
        }

        public void ToggleHint() { hintOpen = !hintOpen; if (hintOpen) practice.MarkHintUsed(); }
        public void NextQuestion() { if (practice.Next()) { hintOpen = false; effects.Clear(); } }
        public void SkipQuestion() { practice.Skip(); hintOpen = false; effects.Clear(); }
        public void ReviewQuestions() { practice = practice.CreateReview(Environment.TickCount); hintOpen = false; effects.Clear(); }

        private int GetVisibleMask()
        {
            // Old and new images share the same six meanings, including in practice mode.
            return GetTrackedMask(targets) | GetTrackedMask(additionalTargets);
        }

        private static int GetTrackedMask(ObserverBehaviour[] observers)
        {
            int mask = 0;
            if (observers == null) return mask;
            for (int i = 0; i < Math.Min(MarkerMessageRules.MarkerCount, observers.Length); i++)
            {
                ObserverBehaviour target = observers[i];
                // Estimated poses may remain after a marker has been hidden.
                if (target != null && target.isActiveAndEnabled && target.TargetStatus.Status == Status.TRACKED) mask |= 1 << i;
            }
            return mask;
        }

        private string GetMarkerLabel(int index)
        {
            string label = markerLabels != null && index < markerLabels.Length ? markerLabels[index] : null;
            return string.IsNullOrEmpty(label) ? (index == 5 ? "ID：待つ" : "マーカー" + (index + 1)) : label;
        }

        private string DescribeMask(int mask)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < MarkerMessageRules.MarkerCount; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (builder.Length > 0) builder.Append(" ＋ ");
                builder.Append(GetMarkerLabel(i));
            }
            return builder.ToString();
        }

        private string GetStatusText(int mask)
        {
            return (useSimulatedMarkers ? "テスト中：" : "認識中：") + (mask == 0 ? "なし" : DescribeMask(mask));
        }

        private void OnDisable() { StopTownAudio(); if (townView != null) townView.SetActive(false); if (effects != null) effects.Clear(); if (view != null) view.Root.SetActive(false); }
        private void OnDestroy() { if (townView != null) townView.Dispose(); if (effects != null) effects.Dispose(); if (view != null) view.Dispose(); }
    }
}
