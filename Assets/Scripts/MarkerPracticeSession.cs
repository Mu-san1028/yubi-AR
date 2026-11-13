using System;
using System.Collections.Generic;

namespace Yubi
{
    public enum PracticeFeedback { WaitingForClear, Ready, Reading, Incorrect, Correct, Complete }

    public sealed class MarkerPracticeQuestion
    {
        public int Mask { get; private set; }
        public string Message { get; private set; }
        public MarkerPracticeQuestion(int mask, string message) { Mask = mask; Message = message; }
    }

    // Exact sets are judged after settling. Every question and retry requires clearing the camera.
    public sealed class MarkerPracticeSession
    {
        private readonly List<MarkerPracticeQuestion> questions;
        private readonly List<MarkerPracticeQuestion> review = new List<MarkerPracticeQuestion>();
        private int index, candidateMask, attempts;
        private float changedAt;
        private bool timingStarted, usedHint;
        public PracticeFeedback Feedback { get; private set; }
        public int Total { get { return questions.Count; } }
        public int Number { get { return Math.Min(index + 1, Total); } }
        public int CorrectCount { get; private set; }
        public int FirstTryCount { get; private set; }
        public int SkippedCount { get; private set; }
        public int ReviewCount { get { return review.Count; } }
        public int LastAnswerMask { get; private set; }
        public bool Complete { get { return index >= Total; } }
        public MarkerPracticeQuestion Current { get { return Complete ? null : questions[index]; } }

        public float ReadingProgress(float now, float answerSeconds)
        {
            if (Feedback != PracticeFeedback.Reading || !timingStarted || candidateMask == 0) return 0;
            return answerSeconds <= 0 ? 1 : Math.Max(0, Math.Min(1, (now - changedAt) / answerSeconds));
        }

        public MarkerPracticeSession(MarkerMessageRule[] rules, bool singlesOnly, int seed)
            : this(CreateQuestions(rules, singlesOnly), seed) { }

        private MarkerPracticeSession(List<MarkerPracticeQuestion> source, int seed)
        {
            questions = new List<MarkerPracticeQuestion>(source);
            var random = new Random(seed);
            for (int i = questions.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                MarkerPracticeQuestion question = questions[i];
                questions[i] = questions[j]; questions[j] = question;
            }
            ResetQuestion();
        }

        private static List<MarkerPracticeQuestion> CreateQuestions(MarkerMessageRule[] rules, bool singlesOnly)
        {
            var result = new List<MarkerPracticeQuestion>();
            var seen = new HashSet<int>();
            if (rules == null) return result;
            foreach (MarkerMessageRule rule in rules)
            {
                if (rule == null || !rule.TryGetMask(out int mask) || !seen.Add(mask)) continue;
                // The first rule for each set matches communication mode.
                if (string.IsNullOrWhiteSpace(rule.message) || (singlesOnly && (mask & (mask - 1)) != 0)) continue;
                result.Add(new MarkerPracticeQuestion(mask, rule.message));
            }
            return result;
        }

        public void Update(int visibleMask, float now, float answerSeconds, float clearSeconds)
        {
            if (Complete || Feedback == PracticeFeedback.Correct) return;
            if (!timingStarted || candidateMask != visibleMask)
            {
                candidateMask = visibleMask; changedAt = now; timingStarted = true;
            }
            float elapsed = now - changedAt;
            if (Feedback == PracticeFeedback.WaitingForClear || Feedback == PracticeFeedback.Incorrect)
            {
                if (visibleMask == 0 && elapsed >= Math.Max(0f, clearSeconds))
                {
                    Feedback = PracticeFeedback.Ready; LastAnswerMask = 0;
                }
                return;
            }
            if (visibleMask == 0) { Feedback = PracticeFeedback.Ready; return; }
            Feedback = PracticeFeedback.Reading;
            if (elapsed < Math.Max(0f, answerSeconds)) return;
            attempts++; LastAnswerMask = visibleMask;
            if (visibleMask == Current.Mask)
            {
                Feedback = PracticeFeedback.Correct; CorrectCount++;
                if (attempts == 1 && !usedHint) FirstTryCount++;
                else AddToReview();
            }
            else Feedback = PracticeFeedback.Incorrect;
        }

        public void MarkHintUsed()
        {
            if (!Complete && Feedback != PracticeFeedback.Correct) usedHint = true;
        }

        public bool Next()
        {
            if (Complete || Feedback != PracticeFeedback.Correct) return false;
            index++; ResetQuestion(); return true;
        }

        public void Skip()
        {
            if (Complete || Feedback == PracticeFeedback.Correct) return;
            AddToReview(); SkippedCount++; index++; ResetQuestion();
        }

        public MarkerPracticeSession CreateReview(int seed) { return new MarkerPracticeSession(review, seed); }

        public void RestartObservation()
        {
            timingStarted = false;
            if (!Complete && Feedback != PracticeFeedback.Correct && Feedback != PracticeFeedback.Incorrect)
                Feedback = PracticeFeedback.WaitingForClear;
        }

        private void AddToReview() { if (!review.Contains(Current)) review.Add(Current); }

        private void ResetQuestion()
        {
            attempts = 0; usedHint = false; timingStarted = false; LastAnswerMask = 0;
            Feedback = Complete ? PracticeFeedback.Complete : PracticeFeedback.WaitingForClear;
        }
    }
}
