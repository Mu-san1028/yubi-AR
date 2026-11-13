using System;

namespace Yubi
{
    [Serializable]
    public class MarkerMessageRule
    {
        public int[] markerIds = new int[0];
        public string message = "";

        public bool TryGetMask(out int mask)
        {
            mask = 0;
            if (markerIds == null || markerIds.Length == 0 || markerIds.Length > MarkerMessageRules.MaximumSimultaneousMarkers)
                return false;

            foreach (int id in markerIds)
            {
                if (id < 1 || id > MarkerMessageRules.MarkerCount || (mask & (1 << (id - 1))) != 0)
                {
                    mask = 0;
                    return false;
                }
                mask |= 1 << (id - 1);
            }
            return true;
        }
    }

    public static class MarkerMessageRules
    {
        public const int MarkerCount = 6;
        public const int MaximumSimultaneousMarkers = 2;
        public const int MaximumMask = (1 << MarkerCount) - 1;

        // IDs 1-5 are green/red/blue/purple/yellow in Mu-san; ID 6 is idmarker.
        public static MarkerMessageRule[] CreateDefaultRules()
        {
            return new[]
            {
                new MarkerMessageRule { markerIds = new[] { 1 }, message = "はい" },
                new MarkerMessageRule { markerIds = new[] { 2 }, message = "いいえ" },
                new MarkerMessageRule { markerIds = new[] { 3 }, message = "ありがとう" },
                new MarkerMessageRule { markerIds = new[] { 4 }, message = "お願いします" },
                new MarkerMessageRule { markerIds = new[] { 5 }, message = "手伝います" },
                new MarkerMessageRule { markerIds = new[] { 6 }, message = "待ちます" },
                new MarkerMessageRule { markerIds = new[] { 1, 5 }, message = "手伝えます" },
                new MarkerMessageRule { markerIds = new[] { 1, 6 }, message = "待てます" },
                new MarkerMessageRule { markerIds = new[] { 2, 5 }, message = "手伝えません" },
                new MarkerMessageRule { markerIds = new[] { 2, 6 }, message = "待てません" },
                new MarkerMessageRule { markerIds = new[] { 3, 5 }, message = "手伝ってくれてありがとう" },
                new MarkerMessageRule { markerIds = new[] { 3, 6 }, message = "待ってくれてありがとう" },
                new MarkerMessageRule { markerIds = new[] { 4, 5 }, message = "手伝ってください" },
                new MarkerMessageRule { markerIds = new[] { 4, 6 }, message = "待ってください" },
                new MarkerMessageRule { markerIds = new[] { 1, 2 }, message = "迷っています" },
                new MarkerMessageRule { markerIds = new[] { 5, 6 }, message = "少し待ってから手伝います" }
            };
        }

        public static bool IsSupportedCombination(int visibleMask)
        {
            if (visibleMask <= 0 || visibleMask > MaximumMask)
                return false;
            int count = 0;
            for (int remaining = visibleMask; remaining != 0; remaining &= remaining - 1)
                count++;
            return count <= MaximumSimultaneousMarkers;
        }

        // Match the whole set, and never select a pair from three or more markers.
        public static string FindMessage(int visibleMask, MarkerMessageRule[] rules)
        {
            if (!IsSupportedCombination(visibleMask) || rules == null)
                return "";

            foreach (MarkerMessageRule rule in rules)
            {
                if (rule != null && rule.TryGetMask(out int mask) && mask == visibleMask)
                    return rule.message ?? "";
            }
            return "";
        }
    }

    // Clear an old message immediately; show a new one only after stable recognition.
    public sealed class MarkerCombinationState
    {
        public int VisibleMask { get; private set; }
        public int ConfirmedMask { get; private set; }
        private float changedAt;

        public float ReadingProgress(float now, float stableSeconds)
        {
            if (VisibleMask == 0) return 0;
            return stableSeconds <= 0 ? 1 : Math.Max(0, Math.Min(1, (now - changedAt) / stableSeconds));
        }

        public void Update(int visibleMask, float now, float stableSeconds)
        {
            if (visibleMask != VisibleMask)
            {
                VisibleMask = visibleMask;
                ConfirmedMask = 0;
                changedAt = now;
            }

            if (now - changedAt >= Math.Max(0f, stableSeconds))
                ConfirmedMask = VisibleMask;
        }
    }
}
