using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>An Eyesider who comes to the stall: what they order, what they refuse, and what they secretly crave.</summary>
    [Serializable]
    public class Customer
    {
        public string name = "Eyesider";
        [Tooltip("Dishes they order. Empty means anything on the menu.")]
        public List<RecipeDefinition> orders = new List<RecipeDefinition>();
        [Tooltip("Hitting this zone is their dream meal. Optional.")]
        public FlavourZone secretDesire;
        [Tooltip("They refuse any dish containing these.")]
        public List<IngredientDefinition> refuses = new List<IngredientDefinition>();

        [Header("Lines ({0} is the dish name)")]
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
