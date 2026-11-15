using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// Static data for one ingredient: what one unit tastes like and how much of a dish it fills.
    /// </summary>
    /// <remarks>
    /// Flavour values are intensities on the 0..10 scale. A dish's taste is
    /// sum(flavour × quantity) / sum(bulk × quantity), so a bulk-1 ingredient eaten alone tastes
    /// exactly like its flavour values, and low-bulk seasonings (salt, MSG2) concentrate into
    /// whatever they are mixed with. See <see cref="FlavourCalculator"/>.
    /// </remarks>
    [CreateAssetMenu(fileName = "Ingredient", menuName = "Cooking/Ingredient")]
    public class IngredientDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [Tooltip("Cost of one unit at the market.")]
        [SerializeField, Min(0f)] private float price = 1f;

        [Header("Flavour")]
        [Tooltip("How much of a dish one unit fills. 1 for a main ingredient, around 0.1 for a pinch of seasoning.")]
        [SerializeField, Min(0.01f)] private float bulk = 1f;
        [Tooltip("What one unit tastes like, before prep and cooking.")]
        [SerializeField] private FlavourProfile flavour;
        [Tooltip("Added per unit when the ingredient is served without cooking. Use it for things that are bad raw (rice, chicken).")]
        [SerializeField] private FlavourProfile rawDelta;

        [TextArea] [SerializeField] private string notes;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Sprite Icon => icon;
        public float Price => price;
        public float Bulk => bulk;
        public FlavourProfile Flavour => flavour;
        public FlavourProfile RawDelta => rawDelta;
        public string Notes => notes;

        /// <summary>Sets the data in code. Intended for tests and editor tooling.</summary>
        public void Configure(string newDisplayName, FlavourProfile newFlavour, float newBulk = 1f,
            FlavourProfile newRawDelta = default, float newPrice = 1f)
        {
            displayName = newDisplayName;
            flavour = newFlavour;
            bulk = Mathf.Max(0.01f, newBulk);
            rawDelta = newRawDelta;
            price = Mathf.Max(0f, newPrice);
        }

        private void OnValidate()
        {
            bulk = Mathf.Max(0.01f, bulk);
            price = Mathf.Max(0f, price);
        }
    }
}
