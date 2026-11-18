using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// The Current Order radar as a cooking tool. Dishes are dots (the centre of their shape on the
    /// chart). It shows the order (a grey dot) and the tasty zone it belongs to (blue) as the target,
    /// and the dish as it builds up (an orange dot): what's on the plate plus what's in the pot and
    /// pan, as they'll taste once cooked. While an ingredient is held over the pot, the pan or a
    /// plate, a dashed ring shows where the dot would move if it were dropped there.
    /// </summary>
    public partial class CookingPrototype
    {
        private static readonly Color OrderGrey = new Color(0.85f, 0.85f, 0.85f, 0.75f);
        private static readonly Color PreviewColor = new Color(1f, 0.85f, 0.6f, 0.95f);
        /// <summary>A dish too simple to count yet.</summary>
        private static readonly Color SimpleGrey = new Color(0.55f, 0.55f, 0.58f, 1f);

        /// <summary>The held ingredient's would-be dish, while it's over the pot, the pan or a plate; null otherwise.</summary>
        private List<IngredientPortion> hoverDish;
        /// <summary>What the dotted preview was made for, so it's only rebuilt when that changes.</summary>
        private object hoverTarget;

        /// <summary>The dish so far: the plate you're working on, plus the pot and pan as they'll come out.</summary>
        private List<IngredientPortion> DishSoFar()
        {
            List<IngredientPortion> dish = new List<IngredientPortion>();
            Plate plateNow = held == Held.Plate && heldPlate != null ? heldPlate : focusPlate;
            if(plateNow != null) dish.AddRange(plateNow.Items);
            AddStation(dish, pot);
            AddStation(dish, wok);
            return dish;
        }

        private static void AddStation(List<IngredientPortion> dish, CookingStation station)
        {
            if(station == null) return;
            // As it'll come out if it's taken off in the orange; once it's in the red, burnt.
            Doneness doneness = station.IsRuined ? Doneness.Burnt : Doneness.Perfect;

            foreach(IngredientPortion portion in station.Contents)
            {
                if(portion == null || !portion.IsValid) continue;
                dish.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking | station.Method, doneness));
            }
        }

        /// <summary>Redraws the order radar: the order, its zone, the dish so far and any dotted preview.</summary>
        private void RefreshTasteGuide()
        {
            if(plateRadar == null) return;

            plateRadar.ClearProfiles();

            // Only while someone is waiting on an order: once it's served (or the day is over) the
            // radar is clear again.
            if(phase != Phase.Service || customer == null || !orderIn)
            {
                hoverDish = null;
                hoverTarget = null;
                return;
            }

            List<IngredientPortion> dish = DishSoFar();
            bool target = orderKnown && order != null;

            // Dots: the order is where to aim, the dish is where you are, the ring is where you'd go.
            if(target) plateRadar.AddDot(orderProfile, OrderGrey);
            // Grey until it's a proper dish (enough ingredients, something cooked): only then can it count.
            int needed = DishJudge.Settings.Default.MinIngredients;
            if(dish.Count > 0) plateRadar.AddDot(FlavourCalculator.Calculate(dish, rules), DishJudge.IsProperDish(dish, needed) ? UIFactory.Accent : SimpleGrey);
            if(hoverDish != null) plateRadar.AddDot(FlavourCalculator.Calculate(hoverDish, rules), DishJudge.IsProperDish(hoverDish, needed) ? PreviewColor : SimpleGrey, true);
        }

        /// <summary>
        /// Per frame while an ingredient is held: over the pot, the pan or a plate, preview the dish
        /// with it added (as that station would cook it). Cheap when the target hasn't changed.
        /// </summary>
        private void UpdateTastePreview(Vector2 screen)
        {
            object target = null;
            List<IngredientPortion> preview = null;

            if(held == Held.Portion && heldPortion != null && phase == Phase.Service)
            {
                StationView station = StationAt(screen);
                DeskItem item = station == null ? ItemAt(screen) : null;

                if(station != null && station.Station.CanAdd(heldPortion, out _))
                {
                    target = station;
                    preview = DishSoFar();
                    preview.Add(new IngredientPortion(heldPortion.ingredient, heldPortion.quantity, heldPortion.prep, heldPortion.cooking | station.Station.Method, Doneness.Perfect));
                }
                else if(item != null && item.Plate != null)
                {
                    target = item;
                    // That plate, with the pot and pan, and the held ingredient on it as it is.
                    preview = new List<IngredientPortion>(item.Plate.Items);
                    AddStation(preview, pot);
                    AddStation(preview, wok);
                    preview.Add(heldPortion);
                }
            }

            if(target == hoverTarget) return;

            hoverTarget = target;
            hoverDish = preview;
            RefreshTasteGuide();
        }
    }
}
