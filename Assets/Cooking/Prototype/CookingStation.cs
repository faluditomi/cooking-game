using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// A frying pan or a pot (Notion, Demo Changes). It starts cooking as soon as the first thing goes
    /// in, and more can go in while it cooks. Everything in it comes off together, as done as the bar
    /// says.
    /// </summary>
    /// <remarks>
    /// Progress runs 0..1 over <see cref="Duration"/> seconds, through three stages:
    /// <list type="bullet">
    /// <item><b>Blue</b> (below <see cref="PerfectStart"/>): plated now, it's undercooked.</item>
    /// <item><b>Orange</b> (to <see cref="PerfectEnd"/>): just right. Shaking the pan (or stirring the
    /// pot) here sends the bar back to the start of the orange, so it can be kept there.</item>
    /// <item><b>Red</b> (from <see cref="PerfectEnd"/>): burnt, beyond saving. The bar can't come back
    /// down; it has to be plated or emptied.</item>
    /// </list>
    /// </remarks>
    public class CookingStation
    {
        public enum Stage
        {
            Raw,
            Cooking,
            Done,
            Ruined,
        }

        public readonly string Name;
        public readonly CookMethods Method;
        public readonly float Duration;
        /// <summary>Where the orange (just right) starts.</summary>
        public readonly float PerfectStart;
        /// <summary>Where the red (ruined) starts.</summary>
        public readonly float PerfectEnd;

        /// <summary>
        /// When true (the pot), the station takes one kind of ingredient at a time unless a stock
        /// ingredient (<see cref="IngredientDefinition.IsStock"/>) is in it, which turns it into a soup.
        /// </summary>
        public bool OneIngredientWithoutStock;

        private readonly List<IngredientPortion> contents = new List<IngredientPortion>();

        public IReadOnlyList<IngredientPortion> Contents => contents;
        public bool IsEmpty => contents.Count == 0;
        public bool IsCooking { get; private set; }
        public float Progress { get; private set; }

        public CookingStation(string name, CookMethods method, float duration, float perfectStart, float perfectEnd)
        {
            Name = name;
            Method = method;
            Duration = Mathf.Max(0.1f, duration);
            PerfectStart = perfectStart;
            PerfectEnd = perfectEnd;
        }

        /// <summary>The frying pan: blue to 50%, orange to 82%, then red.</summary>
        public static CookingStation CreateWok(float duration = 8f)
        {
            return new CookingStation("Pan", CookMethods.Fried, duration, 0.5f, 0.82f);
        }

        /// <summary>The pot: the same bar. One ingredient at a time unless there's stock in it.</summary>
        public static CookingStation CreatePot(float duration = 10f)
        {
            return new CookingStation("Pot", CookMethods.Boiled, duration, 0.5f, 0.82f)
            {
                OneIngredientWithoutStock = true,
            };
        }

        /// <summary>True when stock is in the station, which makes it a soup.</summary>
        public bool IsSoup
        {
            get
            {
                foreach(IngredientPortion portion in contents)
                {
                    if(portion.ingredient.IsStock) return true;
                }

                return false;
            }
        }

        public Stage CurrentStage => !IsCooking ? Stage.Raw : Progress < PerfectStart ? Stage.Cooking : Progress < PerfectEnd ? Stage.Done : Stage.Ruined;
        public bool IsRuined => IsCooking && Progress >= PerfectEnd;
        public Doneness CurrentDoneness => Evaluate(Progress);

        /// <summary>The doneness at <paramref name="progress"/>: undercooked, perfect, or burnt.</summary>
        public Doneness Evaluate(float progress)
        {
            if(progress < PerfectStart) return Doneness.Undercooked;
            return progress < PerfectEnd ? Doneness.Perfect : Doneness.Burnt;
        }

        /// <summary>Whether <paramref name="portion"/> can go in now, and why not.</summary>
        public bool CanAdd(IngredientPortion portion, out string reason)
        {
            reason = null;

            if(portion == null || !portion.IsValid)
            {
                reason = "Nothing to add.";
                return false;
            }

            if(IsRuined)
            {
                reason = "What's in the " + Name.ToLowerInvariant() + " is burnt. Plate it or empty it first.";
                return false;
            }

            if(OneIngredientWithoutStock && !IsSoup && !portion.ingredient.IsStock)
            {
                foreach(IngredientPortion existing in contents)
                {
                    if(existing.ingredient != portion.ingredient)
                    {
                        reason = "Without a stock cube the " + Name.ToLowerInvariant() + " takes one ingredient at a time.";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Adds a portion (same kind stacks). The first thing in starts the cooking.</summary>
        public bool Add(IngredientPortion portion)
        {
            if(!CanAdd(portion, out _)) return false;

            foreach(IngredientPortion existing in contents)
            {
                if(existing.ingredient == portion.ingredient && existing.prep == portion.prep
                   && existing.cooking == portion.cooking && existing.doneness == portion.doneness)
                {
                    existing.quantity += portion.quantity;
                    return true;
                }
            }

            contents.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, portion.doneness));

            if(!IsCooking)
            {
                IsCooking = true;
                Progress = 0f;
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if(!IsCooking) return;
            Progress = Mathf.Min(1f, Progress + deltaTime / Duration);
        }

        /// <summary>
        /// A shake of the pan or a stir of the pot. In the orange it sends the bar back to the start of
        /// the orange, keeping the food just right; anywhere else it does nothing.
        /// </summary>
        public bool Settle()
        {
            if(CurrentStage != Stage.Done) return false;
            Progress = PerfectStart;
            return true;
        }

        /// <summary>Takes everything off, done as the bar says now.</summary>
        public List<IngredientPortion> TakeOff()
        {
            List<IngredientPortion> result = new List<IngredientPortion>();
            if(IsEmpty) return result;

            Doneness doneness = CurrentDoneness;
            foreach(IngredientPortion portion in contents) AddCooked(result, portion, portion.quantity, doneness);
            Clear();
            return result;
        }

        /// <summary>Tips everything out uncooked (back to the pantry), and stops the station.</summary>
        public List<IngredientPortion> Empty()
        {
            List<IngredientPortion> result = new List<IngredientPortion>(contents);
            Clear();
            return result;
        }

        private void AddCooked(List<IngredientPortion> result, IngredientPortion source, int quantity, Doneness doneness)
        {
            // Something cooked twice (boiled, then fried) keeps the worse result.
            Doneness worst = source.IsRaw || doneness > source.doneness ? doneness : source.doneness;
            CookMethods cooking = source.cooking | Method;

            foreach(IngredientPortion existing in result)
            {
                if(existing.ingredient == source.ingredient && existing.prep == source.prep && existing.cooking == cooking && existing.doneness == worst)
                {
                    existing.quantity += quantity;
                    return;
                }
            }

            result.Add(new IngredientPortion(source.ingredient, quantity, source.prep, cooking, worst));
        }

        /// <summary>Bins the contents.</summary>
        public void Clear()
        {
            contents.Clear();
            IsCooking = false;
            Progress = 0f;
        }
    }
}
