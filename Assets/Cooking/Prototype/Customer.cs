using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Cooking.Prototype
{
    /// <summary>An Eyesider who comes to the stall: what they order, what they refuse, and what they secretly crave.</summary>
    [Serializable]
    public class Customer
    {
        public string name = "Eyesider";
        [Tooltip("Which NPC this is in the DialogueSystem catalog. None uses the fallback lines below.")]
        public DialogueSystem.DialogueNpcId dialogueId;
        [Tooltip("Liked meals (exact or very close) before they become Familiar.")]
        [FormerlySerializedAs("happyMealsToWarmUp")]
        [Min(1)] public int likedMealsToFamiliar = 3;
        [Tooltip("Dishes they order at first. Empty means anything on the menu. Liked dishes get added as you go.")]
        public List<RecipeDefinition> orders = new List<RecipeDefinition>();
        [Tooltip("Hitting this zone is their dream meal. Optional.")]
        public FlavourZone secretDesire;
        [Tooltip("The dream meal also needs all of these on the plate. They win over refusals: a dream meal is eaten even with something they'd otherwise refuse.")]
        public List<IngredientDefinition> dreamNeeds = new List<IngredientDefinition>();
        [Tooltip("They refuse any dish containing these (unless it's their dream meal).")]
        public List<IngredientDefinition> refuses = new List<IngredientDefinition>();

        [Header("Timing (seconds)")]
        [Tooltip("They hurry you this long after arriving, and again each time this long passes.")]
        [Min(5f)] public float hurryAfter = 50f;
        [Tooltip("Once per order, they muse after a random delay in this range.")]
        public Vector2 musingAfter = new Vector2(8f, 20f);

        [Header("Fallback lines, for when the dialogue catalog has none. {0} is the dish name.")]
        public List<string> orderLines = new List<string>();
        public string likedLine = "...";
        public string dislikedLine = "...";
        public string refusedLine = "I'm not eating that.";
        public string dreamLine = "...oh.";

        public string GetOrderLine(RecipeDefinition order, System.Random random)
        {
            string dish = order != null ? order.DisplayName : "something";
            if(orderLines == null || orderLines.Count == 0) return dish + ".";
            return string.Format(orderLines[random.Next(orderLines.Count)], dish);
        }
    }
}
