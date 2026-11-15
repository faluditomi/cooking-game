using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// A wok or a pot. Load it with prepped portions, start it, and take everything off at once.
    /// Where the progress bar is when you take it off decides the doneness of the whole batch.
    /// </summary>
    /// <remarks>
    /// Progress runs 0..1 over <see cref="Duration"/> seconds.
    /// Below <see cref="PerfectStart"/> the food is undercooked. Up to <see cref="PerfectEnd"/> it is perfect.
    /// After that a burning station (wok) stays perfect until progress hits 1 and burns, while a
    /// non-burning station (pot) overcooks. In a wok's danger area (<see cref="DangerStart"/> and up)
    /// shaking knocks progress back, which is the "wok magic" from the design doc.
    /// </remarks>
    public class CookingStation
    {
        public readonly string Name;
        public readonly CookMethods Method;
        public readonly float Duration;
        public readonly float PerfectStart;
        public readonly float PerfectEnd;
        public readonly float DangerStart;
        public readonly bool Burns;
        public readonly float ShakeKnockback;

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

        public CookingStation(string name, CookMethods method, float duration, float perfectStart, float perfectEnd,
            float dangerStart, bool burns, float shakeKnockback = 0.15f)
        {
            Name = name;
            Method = method;
            Duration = Mathf.Max(0.1f, duration);
            PerfectStart = perfectStart;
            PerfectEnd = perfectEnd;
            DangerStart = dangerStart;
            Burns = burns;
            ShakeKnockback = shakeKnockback;
        }

        /// <summary>A wok: perfect from halfway, danger from 80%, burnt at the end.</summary>
        public static CookingStation CreateWok(float duration = 8f, float shakeKnockback = 0.15f)
        {
            return new CookingStation("Wok", CookMethods.Fried, duration, 0.5f, 1f, 0.8f, burns: true, shakeKnockback);
        }

        /// <summary>
        /// A pot: a narrow perfect band in the middle, overcooked after it. One ingredient at a time
        /// unless there is stock in it.
        /// </summary>
        public static CookingStation CreatePot(float duration = 10f)
        {
            return new CookingStation("Pot", CookMethods.Boiled, duration, 0.45f, 0.62f, 0.62f, burns: false)
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

        /// <summary>Whether <paramref name="portion"/> can go in now, and why not.</summary>
        public bool CanAdd(IngredientPortion portion, out string reason)
        {
            reason = null;

            if(portion == null || !portion.IsValid)
            {
                reason = "Nothing to add.";
                return false;
            }

            if(IsCooking)
            {
                reason = "The " + Name.ToLowerInvariant() + " is cooking. Wait or take it off first.";
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

        /// <summary>
        /// Whether a whole batch can go in together. Stock anywhere in the batch (or already in the
        /// station) makes it a soup, so a pot can take several ingredients at once with a stock cube.
        /// </summary>
        public bool CanAddAll(IReadOnlyList<IngredientPortion> batch, out string reason)
        {
            reason = null;

            if(batch == null || batch.Count == 0)
            {
                reason = "Nothing selected.";
                return false;
            }

            if(IsCooking)
            {
                reason = "The " + Name.ToLowerInvariant() + " is cooking. Wait or take it off first.";
                return false;
            }

            if(!OneIngredientWithoutStock || IsSoup) return true;

            IngredientDefinition only = null;

            foreach(IngredientPortion portion in batch)
            {
                if(portion == null || !portion.IsValid) continue;
                if(portion.ingredient.IsStock) return true;
            }

            foreach(IngredientPortion portion in contents)
            {
                only = portion.ingredient;
            }

            foreach(IngredientPortion portion in batch)
            {
                if(portion == null || !portion.IsValid) continue;
                if(only == null) only = portion.ingredient;

                if(portion.ingredient != only)
                {
                    reason = "Without a stock cube the " + Name.ToLowerInvariant() + " takes one ingredient at a time. Add a soup cube to the batch.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>Adds a batch checked with <see cref="CanAddAll"/>; stock goes in first so the rest joins the soup.</summary>
        public bool AddAll(IReadOnlyList<IngredientPortion> batch)
        {
            if(!CanAddAll(batch, out _)) return false;

            foreach(IngredientPortion portion in batch)
            {
                if(portion != null && portion.IsValid && portion.ingredient.IsStock) Add(portion);
            }

            foreach(IngredientPortion portion in batch)
            {
                if(portion != null && portion.IsValid && !portion.ingredient.IsStock) Add(portion);
            }

            return true;
        }

        public bool IsInDanger => IsCooking && Progress >= DangerStart;
        public Doneness CurrentDoneness => Evaluate(Progress);

        public Doneness Evaluate(float progress)
        {
            if(progress < PerfectStart) return Doneness.Undercooked;
            if(Burns && progress >= 1f) return Doneness.Burnt;
            if(progress <= PerfectEnd) return Doneness.Perfect;
            return Burns ? Doneness.Burnt : Doneness.Overcooked;
        }

        /// <summary>Adds one portion. Same ingredient with the same prep stacks. See <see cref="CanAdd"/>.</summary>
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
            return true;
        }

        public bool StartCooking()
        {
            if(IsEmpty || IsCooking) return false;

            IsCooking = true;
            Progress = 0f;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if(!IsCooking) return;
            Progress = Mathf.Min(1f, Progress + deltaTime / Duration);
        }

        /// <summary>Wok only: in the danger area, knocks progress back so the food survives longer.</summary>
        public bool Shake()
        {
            if(!Burns || !IsInDanger || Progress >= 1f) return false;

            Progress = Mathf.Max(PerfectStart, Progress - ShakeKnockback);
            return true;
        }

        /// <summary>Takes everything off the heat with the current doneness applied.</summary>
        public List<IngredientPortion> TakeOff()
        {
            List<IngredientPortion> result = new List<IngredientPortion>();
            if(!IsCooking) return result;

            Doneness doneness = CurrentDoneness;

            foreach(IngredientPortion portion in contents)
            {
                // Something cooked twice keeps the worst result.
                Doneness worst = portion.IsRaw || doneness > portion.doneness ? doneness : portion.doneness;
                result.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking | Method, worst));
            }

            contents.Clear();
            IsCooking = false;
            Progress = 0f;
            return result;
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
