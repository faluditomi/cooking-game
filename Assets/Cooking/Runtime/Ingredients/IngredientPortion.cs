using System;
using UnityEngine;

namespace Cooking
{
    /// <summary>Prep done on the workbench. An ingredient can be peeled and then sliced.</summary>
    [Flags]
    public enum PrepSteps
    {
        None = 0,
        Peeled = 1 << 0,
        Sliced = 1 << 1,
        Grated = 1 << 2,
    }

    /// <summary>Cooking stations an ingredient has been through. None means raw.</summary>
    [Flags]
    public enum CookMethods
    {
        None = 0,
        Boiled = 1 << 0,
        Fried = 1 << 1,
    }

    /// <summary>
    /// How well the cooking went. Ignored for raw ingredients. Overcooked is the boiling
    /// station's failure, Burnt the frying station's.
    /// </summary>
    public enum Doneness
    {
        Perfect = 0,
        Undercooked = 1,
        Overcooked = 2,
        Burnt = 3,
    }

    /// <summary>
    /// An amount of one ingredient as it ends up on the plate: how many units, and what was
    /// done to them. A dish is a list of portions.
    /// </summary>
    [Serializable]
    public class IngredientPortion
    {
        public IngredientDefinition ingredient;
        [Min(0)] public int quantity = 1;
        public PrepSteps prep = PrepSteps.None;
        public CookMethods cooking = CookMethods.None;
        public Doneness doneness = Doneness.Perfect;

        public IngredientPortion() { }

        public IngredientPortion(IngredientDefinition ingredient, int quantity = 1,
            PrepSteps prep = PrepSteps.None, CookMethods cooking = CookMethods.None,
            Doneness doneness = Doneness.Perfect)
        {
            this.ingredient = ingredient;
            this.quantity = quantity;
            this.prep = prep;
            this.cooking = cooking;
            this.doneness = doneness;
        }

        public bool IsRaw => cooking == CookMethods.None;

        /// <summary>True when this portion contributes anything to a dish.</summary>
        public bool IsValid => ingredient != null && quantity > 0;
    }
}
