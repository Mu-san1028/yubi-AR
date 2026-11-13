using System;

namespace Yubi
{
    public enum TownPhase { Exploring, Walking, ClearMarkers, Answering, Feedback, Finished }

    // Camera-independent rules: time continues during movement and conversation.
    public sealed class TownConversationSession
    {
        public const float Duration = 90f;
        public const float HintDelaySeconds = 5f;
        public static readonly string[] Names = { "ハル", "アオ", "ヒロシ", "タロウ", "ジロー", "マイク" };
        public static readonly string[] PrefabNames = {
            "male01_1", "male02_3", "male03_3", "male01_3", "male03_1", "male01_2"
        };
        public static readonly string[] Prompts = {
            "荷物を運ぶのを手伝ってくれますか？",
            "買い物が終わるまで待ってくれますか？",
            "その荷物、私が持ちましょうか？",
            "先に行く？ それとも少し待つ？",
            "道案内するね。広場はこっちだよ！",
            "ちょっと休憩したいな。一緒に待つ？"
        };
        public static readonly string[] Hints = {
            "緑＋黄：手伝えます / 赤＋黄：手伝えません",
            "緑＋ID：待てます / 赤＋ID：待てません",
            "紫＋黄：手伝ってください / 赤：いいえ",
            "ID：待ちます / 黄＋ID：少し待ってから手伝います",
            "青：ありがとう / 紫：お願いします",
            "緑：はい / 赤：いいえ"
        };
        private static readonly int[][] Answers = {
            new[] {17,18}, new[] {33,34}, new[] {24,2},
            new[] {32,48}, new[] {4,8}, new[] {1,2}
        };
        private static readonly string[][] Replies = {
            new[] {"ありがとう！ とても助かったよ。", "わかった！ また今度お願いするね。"},
            new[] {"ありがとう、すぐ戻るね！", "了解！ また広場で会おう。"},
            new[] {"任せて！ 一緒に運ぼう。", "わかった。必要なら声をかけてね。"},
            new[] {"じゃあ、ここで一緒に待とう。", "ありがとう！ 急がなくて大丈夫だよ。"},
            new[] {"どういたしまして！ よい一日を。", "任せて。ゆっくり案内するね！"},
            new[] {"うれしいな。少しお話ししよう！", "そっか。またあとでね！"}
        };

        public TownPhase Phase { get; private set; }
        public float Remaining { get; private set; }
        public int Conversations { get; private set; }
        public int Score { get { return Conversations * 100; } }
        public int Resident { get; private set; } = -1;
        public int LastResident { get; private set; } = -1;
        public int Question { get; private set; }
        public float TravelProgress { get; private set; }
        public float AnswerProgress { get; private set; }
        public string Reply { get; private set; } = "";
        public int LastAnswer { get; private set; }
        public bool WrongAnswer { get; private set; }
        public float ConversationSeconds { get; private set; }
        public bool AnswerHintVisible { get { return ConversationSeconds >= HintDelaySeconds; } }
        private float stageTime, heldTime, clearTime;
        private int heldMask, rejectedMask;
        private readonly int[] visits = new int[Names.Length];

        public TownConversationSession() { Restart(); }
        public void Restart()
        {
            Remaining = Duration; Conversations = 0; Resident = LastResident = -1;
            Question = LastAnswer = 0; Reply = ""; WrongAnswer = false;
            ConversationSeconds = 0;
            TravelProgress = AnswerProgress = stageTime = heldTime = clearTime = 0;
            heldMask = rejectedMask = 0; Array.Clear(visits, 0, visits.Length); Phase = TownPhase.Exploring;
        }

        public bool Visit(int resident)
        {
            if (Phase != TownPhase.Exploring || resident < 0 || resident >= Names.Length || resident == LastResident) return false;
            Resident = resident; Question = (resident + visits[resident] * 3) % Prompts.Length;
            ConversationSeconds = 0;
            Phase = TownPhase.Walking; stageTime = TravelProgress = 0;
            WrongAnswer = false; rejectedMask = 0; Reply = ""; LastAnswer = 0; ResetObservation(); return true;
        }

        public void Update(float delta, int mask)
        {
            if (Phase == TownPhase.Finished) return;
            delta = Math.Max(0, delta); Remaining = Math.Max(0, Remaining - delta);
            // At the deadline no further answer can score.
            if (Remaining <= 0) { Phase = TownPhase.Finished; return; }
            // The hint delay starts when the question appears, and survives retries.
            if (Phase == TownPhase.ClearMarkers || Phase == TownPhase.Answering)
                ConversationSeconds += delta;
            if (Phase == TownPhase.Walking)
            {
                stageTime += delta; TravelProgress = Math.Min(1, stageTime / 1.5f);
                if (TravelProgress >= 1) { Phase = TownPhase.ClearMarkers; ResetObservation(); }
            }
            else if (Phase == TownPhase.ClearMarkers)
            {
                // After a mistake, a different combination can start a new answer
                // without requiring the player to hide every marker first.
                if (WrongAnswer && mask != 0 && mask != rejectedMask)
                {
                    Phase = TownPhase.Answering;
                    ResetObservation(); heldMask = mask;
                    return;
                }
                clearTime = mask == 0 ? clearTime + delta : 0;
                if (clearTime >= .25f) { Phase = TownPhase.Answering; ResetObservation(); }
            }
            else if (Phase == TownPhase.Answering)
            {
                if (mask == 0 || mask != heldMask) { heldMask = mask; heldTime = AnswerProgress = 0; return; }
                heldTime += delta; AnswerProgress = Math.Min(1, heldTime / .6f);
                if (heldTime < .6f) return;
                int answer = Array.IndexOf(Answers[Question], mask);
                if (answer < 0)
                {
                    WrongAnswer = true; rejectedMask = mask;
                    Phase = TownPhase.ClearMarkers; ResetObservation(); return;
                }
                Conversations++; visits[Resident]++; LastResident = Resident; LastAnswer = mask;
                Reply = Replies[Question][answer]; WrongAnswer = false;
                Phase = TownPhase.Feedback; stageTime = 0;
            }
            else if (Phase == TownPhase.Feedback)
            {
                stageTime += delta;
                if (stageTime >= 2f) { Phase = TownPhase.Exploring; Resident = -1; ResetObservation(); }
            }
        }

        public void LeaveConversation()
        {
            if (Phase != TownPhase.Answering && Phase != TownPhase.ClearMarkers) return;
            LastResident = Resident; Resident = -1; Phase = TownPhase.Exploring; ResetObservation();
        }
        private void ResetObservation() { clearTime = heldTime = AnswerProgress = 0; heldMask = 0; }
    }
}
