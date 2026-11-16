using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>What a wok shake did.</summary>
    public enum ShakeResult
    {
        /// <summary>Not a wok, or not cooking.</summary>
        None,
        /// <summary>Too early (grey): it doesn't count, and the bar goes back to the start.</summary>
        TooEarly,
        /// <summary>In the green: one unit stirred properly.</summary>
        Stirred,
        /// <summary>In the red: one unit stirred, but a bit burned (overcooked).</summary>
        StirredOverdone,
        /// <summary>The bar was knocked back, but nothing was left to stir (or it was already in the black).</summary>
        KnockedBack,
    }

    /// <summary>
    /// A wok or a pot. Load it with one batch of prepped portions, start it, and take everything off
    /// at once.
    /// </summary>
    /// <remarks>
    /// Progress runs 0..1 over <see cref="Duration"/> seconds.
    /// <para><b>Pot:</b> where the bar is when you take it off decides the doneness of the whole
    /// batch: below <see cref="PerfectStart"/> undercooked, up to <see cref="PerfectEnd"/> perfect,
    /// then overcooked.</para>
    /// <para><b>Wok</b> (<see cref="Stirs"/>): every unit needs one stir, a shake on the shake pad.
    /// Grey (before <see cref="PerfectStart"/>): too early, the bar goes back to the start. Green (up
    /// to <see cref="PerfectEnd"/>): one unit stirred perfectly. Red (up to <see cref="BlackStart"/>):
    /// one unit stirred but overcooked. Each counted shake knocks the bar back to the middle of the
    /// grey. Reaching the black burns one unit; staying at the end of the bar burns another every
    /// <see cref="BurnEverySeconds"/>. Units never stirred come off undercooked.</para>
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

        /// <summary>The wok: units are stirred one at a time (see remarks).</summary>
        public bool Stirs;
        /// <summary>Wok: from here on, the bar burns units.</summary>
        public float BlackStart = 1f;
        /// <summary>Wok: at the end of the bar, another unit burns this often.</summary>
        public float BurnEverySeconds = 2f;

        /// <summary>
        /// When true (the pot), the station takes one kind of ingredient at a time unless a stock
        /// ingredient (<see cref="IngredientDefinition.IsStock"/>) is in it, which turns it into a soup.
        /// </summary>
        public bool OneIngredientWithoutStock;

        private readonly List<IngredientPortion> contents = new List<IngredientPortion>();

        /// <summary>One unit in the wok and what's happened to it (null: not stirred yet).</summary>
        private class Unit
        {
            public IngredientPortion Source;
            public Doneness? Result;
        }

        private readonly List<Unit> units = new List<Unit>();
        private readonly System.Random random;
        private bool burnedThisPass;
        private float atEndFor;

        public IReadOnlyList<IngredientPortion> Contents => contents;
        public bool IsEmpty => contents.Count == 0;
        public bool IsCooking { get; private set; }
        public float Progress { get; private set; }

        /// <summary>Wok: units in the batch, and how many were stirred (perfect or overcooked) or burned.</summary>
        public int UnitCount => units.Count;
        public int StirredCount => CountUnits(u => u.Result == Doneness.Perfect || u.Result == Doneness.Overcooked);
        public int OverdoneCount => CountUnits(u => u.Result == Doneness.Overcooked);
        public int BurntCount => CountUnits(u => u.Result == Doneness.Burnt);
        public int UnstirredCount => CountUnits(u => u.Result == null);

        public CookingStation(string name, CookMethods method, float duration, float perfectStart, float perfectEnd,
            float dangerStart, bool burns, int seed = 0)
        {
            Name = name;
            Method = method;
            Duration = Mathf.Max(0.1f, duration);
            PerfectStart = perfectStart;
            PerfectEnd = perfectEnd;
            DangerStart = dangerStart;
            Burns = burns;
            random = seed != 0 ? new System.Random(seed) : new System.Random();
        }

        /// <summary>A wok: grey to 55%, a small green to 70%, red to 88%, then black.</summary>
        public static CookingStation CreateWok(float duration = 6f, int seed = 0)
        {
            return new CookingStation("Wok", CookMethods.Fried, duration, 0.55f, 0.7f, 0.7f, burns: true, seed)
            {
                Stirs = true,
                BlackStart = 0.88f,
            };
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
        /// Whether a whole batch can go in. A dish is everything going in at once, so a station takes
        /// one batch until it's emptied. Stock anywhere in the batch makes it a soup, so a pot can take
        /// several ingredients at once with a stock cube.
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

            if(!IsEmpty)
            {
                reason = "Everything for a dish goes into the " + Name.ToLowerInvariant() + " at once. Start it, or bin it and start over.";
                return false;
            }

            if(!OneIngredientWithoutStock) return true;

            IngredientDefinition only = null;

            foreach(IngredientPortion portion in batch)
            {
                if(portion == null || !portion.IsValid) continue;
                if(portion.ingredient.IsStock) return true;
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
        public bool IsInBlack => IsCooking && Stirs && Progress >= BlackStart;
        public Doneness CurrentDoneness => Evaluate(Progress);

        /// <summary>
        /// The doneness at <paramref name="progress"/>. For the pot, that's what the batch gets; for
        /// the wok, what a stir there would do (the black is burnt).
        /// </summary>
        public Doneness Evaluate(float progress)
        {
            if(progress < PerfectStart) return Doneness.Undercooked;
            if(progress <= PerfectEnd) return Doneness.Perfect;
            if(Stirs) return progress < BlackStart ? Doneness.Overcooked : Doneness.Burnt;
            if(Burns && progress >= 1f) return Doneness.Burnt;
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
            burnedThisPass = false;
            atEndFor = 0f;
            units.Clear();

            if(Stirs)
            {
                foreach(IngredientPortion portion in contents)
                {
                    for(int i = 0; i < portion.quantity; i++) units.Add(new Unit { Source = portion });
                }
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if(!IsCooking) return;
            Progress = Mathf.Min(1f, Progress + deltaTime / Duration);
            if(!Stirs) return;

            // Into the black: one unit burns. Left at the end: another every few seconds.
            if(Progress >= BlackStart && !burnedThisPass)
            {
                burnedThisPass = true;
                BurnOne();
            }

            if(Progress >= 1f)
            {
                atEndFor += deltaTime;

                if(atEndFor >= BurnEverySeconds)
                {
                    atEndFor -= BurnEverySeconds;
                    BurnOne();
                }
            }
        }

        /// <summary>Wok: a shake on the shake pad. See the class remarks for what each zone does.</summary>
        public ShakeResult Shake()
        {
            if(!Stirs || !IsCooking) return ShakeResult.None;

            if(Progress < PerfectStart)
            {
                Progress = 0f;
                ResetPass();
                return ShakeResult.TooEarly;
            }

            ShakeResult result = ShakeResult.KnockedBack;

            if(Progress < BlackStart)
            {
                bool green = Progress <= PerfectEnd;
                Unit unit = PickUnit(u => u.Result == null);

                if(unit != null)
                {
                    unit.Result = green ? Doneness.Perfect : Doneness.Overcooked;
                    result = green ? ShakeResult.Stirred : ShakeResult.StirredOverdone;
                }
            }

            // Back to the middle of the grey.
            Progress = PerfectStart * 0.5f;
            ResetPass();
            return result;
        }

        private void ResetPass()
        {
            burnedThisPass = false;
            atEndFor = 0f;
        }

        private void BurnOne()
        {
            Unit unit = PickUnit(u => u.Result != Doneness.Burnt);
            if(unit != null) unit.Result = Doneness.Burnt;
        }

        private Unit PickUnit(System.Predicate<Unit> match)
        {
            List<Unit> candidates = units.FindAll(match);
            return candidates.Count > 0 ? candidates[random.Next(candidates.Count)] : null;
        }

        private int CountUnits(System.Predicate<Unit> match)
        {
            return units.FindAll(match).Count;
        }

        /// <summary>
        /// Takes everything off the heat. Pot: the current doneness for all of it. Wok: each unit as
        /// it was stirred, burned, or (never stirred) undercooked.
        /// </summary>
        public List<IngredientPortion> TakeOff()
        {
            List<IngredientPortion> result = new List<IngredientPortion>();
            if(!IsCooking) return result;

            if(Stirs)
            {
                foreach(Unit unit in units)
                {
                    Doneness doneness = unit.Result ?? Doneness.Undercooked;
                    AddCooked(result, unit.Source, 1, doneness);
                }
            }
            else
            {
                Doneness doneness = CurrentDoneness;
                foreach(IngredientPortion portion in contents) AddCooked(result, portion, portion.quantity, doneness);
            }

            contents.Clear();
            units.Clear();
            IsCooking = false;
            Progress = 0f;
            return result;
        }

        private void AddCooked(List<IngredientPortion> result, IngredientPortion source, int quantity, Doneness doneness)
        {
            // Something cooked twice keeps the worst result.
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
            units.Clear();
            IsCooking = false;
            Progress = 0f;
        }
    }
}
