namespace Yubi
{
    public enum MarkerEffectSymbol { None, Yes, No, Thanks, Please, Help, Wait, Uncertain }

    public struct MarkerEffectPlan
    {
        public MarkerEffectSymbol Primary;
        public MarkerEffectSymbol Accent;
        public bool Sequence;

        public MarkerEffectPlan(MarkerEffectSymbol primary, MarkerEffectSymbol accent = MarkerEffectSymbol.None, bool sequence = false)
        {
            Primary = primary; Accent = accent; Sequence = sequence;
        }
    }

    // IDs describe meanings, shared by the original and semantic marker images.
    // Resolve the whole sentence; never play two contradictory single-marker effects.
    public static class MarkerEffectRules
    {
        public static MarkerEffectPlan Resolve(int mask)
        {
            switch (mask)
            {
                case 1: return new MarkerEffectPlan(MarkerEffectSymbol.Yes);
                case 2: return new MarkerEffectPlan(MarkerEffectSymbol.No);
                case 4: return new MarkerEffectPlan(MarkerEffectSymbol.Thanks);
                case 8: return new MarkerEffectPlan(MarkerEffectSymbol.Please);
                case 16: return new MarkerEffectPlan(MarkerEffectSymbol.Help);
                case 32: return new MarkerEffectPlan(MarkerEffectSymbol.Wait);
                case 17: return new MarkerEffectPlan(MarkerEffectSymbol.Help, MarkerEffectSymbol.Yes);
                case 33: return new MarkerEffectPlan(MarkerEffectSymbol.Wait, MarkerEffectSymbol.Yes);
                case 18: return new MarkerEffectPlan(MarkerEffectSymbol.Help, MarkerEffectSymbol.No);
                case 34: return new MarkerEffectPlan(MarkerEffectSymbol.Wait, MarkerEffectSymbol.No);
                case 20: return new MarkerEffectPlan(MarkerEffectSymbol.Help, MarkerEffectSymbol.Thanks);
                case 36: return new MarkerEffectPlan(MarkerEffectSymbol.Wait, MarkerEffectSymbol.Thanks);
                case 24: return new MarkerEffectPlan(MarkerEffectSymbol.Help, MarkerEffectSymbol.Please);
                case 40: return new MarkerEffectPlan(MarkerEffectSymbol.Wait, MarkerEffectSymbol.Please);
                case 3: return new MarkerEffectPlan(MarkerEffectSymbol.Uncertain);
                case 48: return new MarkerEffectPlan(MarkerEffectSymbol.Wait, MarkerEffectSymbol.Help, true);
                default: return new MarkerEffectPlan(MarkerEffectSymbol.None);
            }
        }
    }
}
