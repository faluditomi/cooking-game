using System;
using System.Collections.Generic;
using DialogueSystem;

namespace Cooking.Prototype
{
    /// <summary>
    /// Maps cooking moments onto DialogueSystem triggers: which event a served dish raises, which
    /// NPC state a stage is, which (event, state) pair the catalog can actually answer, and the
    /// tokens in authored lines.
    /// </summary>
    public static class CustomerDialogue
    {
        /// <summary>Order tokens: the dialogue picks one of their liked meals.</summary>
        public static readonly string[] LikedTokens = { "[liked meal]", "[liked dish]" };
        /// <summary>Order token: the dialogue orders their dream meal.</summary>
        public const string DreamToken = "[dream dish]";

        /// <summary>What a dish nobody has named is called in their lines.</summary>
        public const string UnnamedLiked = "the thing you made me the other day";
        public const string UnnamedDream = "the regular";

        public static DialogueNpcState StateFor(NpcStage stage)
        {
            switch(stage)
            {
                case NpcStage.Familiar: return DialogueNpcState.Familiar;
                case NpcStage.Friendly: return DialogueNpcState.Friendly;
                default: return DialogueNpcState.Stranger;
            }
        }

        /// <summary>
        /// The event a served dish raises. Their dream meal is the dream order scene the first time;
        /// after that it's what they ordered.
        /// </summary>
        public static DialogueEventType EventFor(JudgeResult result, bool firstDream = true)
        {
            switch(result.Verdict)
            {
                case Verdict.DreamMeal: return firstDream ? DialogueEventType.DreamOrder : DialogueEventType.CorrectOrder;
                case Verdict.Ordered: return DialogueEventType.CorrectOrder;
                case Verdict.VeryClose:
                case Verdict.CloseEnough: return DialogueEventType.WrongOrderTasty;
                default: return DialogueEventType.WrongOrderNasty;
            }
        }

        /// <summary>
        /// The state to trigger with: the stage itself when the catalog has lines for it, otherwise
        /// None (lines for any stage). Returns false when the catalog has nothing for the event at all.
        /// </summary>
        public static bool TryResolve(DialogueCatalog catalog, DialogueNpcId npc, DialogueEventType eventType, NpcStage stage,
            out DialogueNpcState state)
        {
            state = StateFor(stage);
            if(catalog == null || npc == DialogueNpcId.None) return false;
            if(catalog.HasRule(npc, eventType, state)) return true;

            state = DialogueNpcState.None;
            return catalog.HasRule(npc, eventType, state);
        }

        /// <summary>What an order dialogue asks for.</summary>
        public enum OrderKind
        {
            /// <summary>Nothing in the payload or text: the default pick.</summary>
            Default,
            /// <summary>The payload names a dish.</summary>
            Named,
            Liked,
            Dream,
        }

        /// <summary>
        /// Reads what an order dialogue asks for: a dish named by the graph's payload, or a
        /// liked/dream token in the line text.
        /// </summary>
        public static OrderKind ReadOrder(string payload, string text, out string dishName)
        {
            dishName = null;

            if(!string.IsNullOrWhiteSpace(payload))
            {
                string trimmed = payload.Trim();
                if(trimmed.Equals(DreamToken, StringComparison.OrdinalIgnoreCase)) return OrderKind.Dream;
                foreach(string token in LikedTokens) if(trimmed.Equals(token, StringComparison.OrdinalIgnoreCase)) return OrderKind.Liked;
                dishName = trimmed;
                return OrderKind.Named;
            }

            if(string.IsNullOrEmpty(text)) return OrderKind.Default;
            if(text.IndexOf(DreamToken, StringComparison.OrdinalIgnoreCase) >= 0) return OrderKind.Dream;
            foreach(string token in LikedTokens) if(text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return OrderKind.Liked;
            return OrderKind.Default;
        }

        /// <summary>
        /// Fills the dish tokens in a line. <paramref name="likedName"/> and <paramref name="dreamName"/>
        /// are null when that dish isn't named (not saved, or deleted from the menu). Other tokens,
        /// like [name of her lover], are left as they are.
        /// </summary>
        public static string FillTokens(string text, string likedName, string dreamName)
        {
            if(string.IsNullOrEmpty(text)) return text;

            foreach(string token in LikedTokens) text = Replace(text, token, likedName ?? UnnamedLiked);
            return Replace(text, DreamToken, dreamName ?? UnnamedDream);
        }

        private static string Replace(string text, string token, string value)
        {
            int index;
            while((index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, index) + value + text.Substring(index + token.Length);
            }

            return text;
        }

        /// <summary>A menu recipe by display name (case-insensitive), or null.</summary>
        public static RecipeDefinition FindByName(IEnumerable<RecipeDefinition> recipes, string displayName)
        {
            if(recipes == null || string.IsNullOrWhiteSpace(displayName)) return null;

            foreach(RecipeDefinition recipe in recipes)
            {
                if(recipe != null && string.Equals(recipe.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase)) return recipe;
            }

            return null;
        }
    }
}
