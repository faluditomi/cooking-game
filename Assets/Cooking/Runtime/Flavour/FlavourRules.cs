using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// Global tuning for the flavour system: how prep, cooking and doneness change an ingredient,
    /// and how forgiving zone matching is. Deltas describe what happens to one full-size (bulk 1)
    /// unit; taste deltas are scaled down for low-bulk ingredients such as seasonings.
    /// </summary>
    [CreateAssetMenu(fileName = "FlavourRules", menuName = "Cooking/Flavour Rules")]
    public class FlavourRules : ScriptableObject
    {
        /// <summary>Zone falloff used when no rules asset is supplied.</summary>
        public const float DefaultZoneFalloff = 2f;

        [Header("Prep (per unit, stacks)")]
        public FlavourProfile peeled;
        public FlavourProfile sliced;
        public FlavourProfile grated;

        [Header("Cooking (per unit, stacks)")]
        public FlavourProfile boiled;
        public FlavourProfile fried;

        [Header("Doneness (per unit, cooked ingredients only)")]
        public FlavourProfile undercooked;
        public FlavourProfile overcooked;
        public FlavourProfile burnt;

        [Header("Zones")]
        [Tooltip("How far outside a zone (radar distance) a dish can be before its score reaches 0. 0 means only dishes inside the zone count.")]
        [Min(0f)] public float zoneFalloff = DefaultZoneFalloff;

        /// <summary>Total delta for the portion's prep, cooking and doneness.</summary>
        public FlavourProfile GetProcessingDelta(PrepSteps prep, CookMethods cooking, Doneness doneness)
        {
            FlavourProfile delta = FlavourProfile.Zero;

            if((prep & PrepSteps.Peeled) != 0) delta += peeled;
            if((prep & PrepSteps.Sliced) != 0) delta += sliced;
            if((prep & PrepSteps.Grated) != 0) delta += grated;

            if((cooking & CookMethods.Boiled) != 0) delta += boiled;
            if((cooking & CookMethods.Fried) != 0) delta += fried;

            if(cooking != CookMethods.None)
            {
                switch(doneness)
                {
                    case Doneness.Undercooked: delta += undercooked; break;
                    case Doneness.Overcooked: delta += overcooked; break;
                    case Doneness.Burnt: delta += burnt; break;
                }
            }

            return delta;
        }
    }
}
