using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Naming dishes. After any order whose dish isn't on the menu (an improvised dish, a regular's
    /// new favourite, or their dream meal), the result screen offers a name field: Save dish names it
    /// and adds it to the menu (origin Emphis); Continue leaves it "Nameless".
    /// </summary>
    public partial class CookingPrototype
    {
        private const int DishNameLimit = 20;

        private GameObject namingRow;
        private Text namingHint;
        private InputField namingField;
        private Button saveDishButton;
        /// <summary>The dish the result screen offers to name, or null.</summary>
        private RecipeDefinition namingDish;

        private void ShowNaming(RecipeDefinition dish)
        {
            namingDish = dish != null && !IsOnMenu(dish) ? dish : null;
            namingRow.SetActive(namingDish != null);
            if(namingDish == null) return;

            bool dream = customer != null && Relationship(customer).DreamMeal == namingDish;
            namingHint.text = dream
                ? "<color=#f28c33>Nobody has made this before. It's what " + customer.name + " was missing.</color> Name it to put it on the menu."
                : "<color=#a6a199>This dish isn't on the menu. Name it to save it, or Continue and it stays \"" + namingDish.DisplayName + "\".</color>";
            namingField.text = namingDish.DisplayName == CustomerRelationship.Nameless ? "" : namingDish.DisplayName;
            namingField.interactable = true;
            saveDishButton.interactable = true;
        }

        private void SaveDish()
        {
            if(namingDish == null) return;

            string chosen = namingField.text != null ? namingField.text.Trim() : "";

            if(chosen.Length == 0)
            {
                namingHint.text = "<color=#e5594d>Give it a name first.</color>";
                return;
            }

            if(chosen.Length > DishNameLimit) chosen = chosen.Substring(0, DishNameLimit);

            RecipeDefinition existing = CustomerDialogue.FindByName(VisibleMenu(), chosen);
            if(existing != null && existing != namingDish)
            {
                namingHint.text = "<color=#e5594d>There's already a dish called \"" + existing.DisplayName + "\" on the menu.</color>";
                return;
            }

            CustomerRelationship.Rename(namingDish, chosen);
            if(!menu.Contains(namingDish)) menu.Add(namingDish);
            deletedDishes.Remove(namingDish);

            namingHint.text = "<color=#73cc73>\"" + chosen + "\" is on the menu.</color>";
            namingField.interactable = false;
            saveDishButton.interactable = false;
            Say("\"" + chosen + "\" is on the menu and in the recipe book.", UIFactory.Accent);
            RefreshAll();
        }

        private void BuildNamingRow(RectTransform parent)
        {
            RectTransform group = UIFactory.Rect(parent, "Naming");
            VerticalLayoutGroup layout = UIFactory.Vertical(group.gameObject, 6f, 0);
            layout.childForceExpandHeight = false;
            namingRow = group.gameObject;

            namingHint = UIFactory.Label(group, "", 17);

            RectTransform row = UIFactory.Row(group, 48f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            Image fieldBackground = UIFactory.Panel(row, "Name Field", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(fieldBackground, 48f, 0f, -1f, 1f);
            namingField = fieldBackground.gameObject.AddComponent<InputField>();
            namingField.targetGraphic = fieldBackground;
            namingField.characterLimit = DishNameLimit;

            Text text = UIFactory.Label(fieldBackground.transform, "", 22, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            UIFactory.Stretch(text.rectTransform).offsetMin = new Vector2(12f, 0f);
            Text placeholder = UIFactory.Label(fieldBackground.transform, "Name the dish (" + DishNameLimit + " letters)", 22, TextAnchor.MiddleLeft,
                FontStyle.Italic, UIFactory.MutedText);
            UIFactory.Stretch(placeholder.rectTransform).offsetMin = new Vector2(12f, 0f);
            namingField.textComponent = text;
            namingField.placeholder = placeholder;
            namingField.onSubmit.AddListener(_ => SaveDish());

            saveDishButton = UIFactory.Button(row, "Save dish", SaveDish, new Color(0.25f, 0.4f, 0.25f, 1f), 20);
            UIFactory.Size(saveDishButton, 48f, 0f, 170f, 0f);

            namingRow.SetActive(false);
        }
    }
}
