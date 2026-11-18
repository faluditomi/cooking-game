using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Naming dishes. After any order whose dish isn't on the menu (an improvised dish, a regular's
    /// new favourite, or their dream meal), the result screen offers a name field: Save dish names it
    /// and adds it to the menu (origin Emphis); Continue leaves it "Nameless". A dish you didn't save
    /// can still be saved later from its entry in the cooking history.
    /// </summary>
    public partial class CookingPrototype
    {
        private const int DishNameLimit = 20;

        // Result screen
        private GameObject namingRow;
        private Text namingHint;
        private InputField namingField;
        private Button saveDishButton;
        /// <summary>The dish the result screen offers to name, or null.</summary>
        private RecipeDefinition namingDish;

        // Cooking history
        private GameObject historySaveRow;
        private Text historySaveHint;
        private InputField historySaveField;
        private Button historySaveButton;

        private void ShowNaming(RecipeDefinition dish)
        {
            namingDish = dish != null && !IsOnMenu(dish) && !offMenu.Contains(dish) ? dish : null;
            namingRow.SetActive(namingDish != null);
            if(namingDish == null) return;

            bool dream = customer != null && Relationship(customer).DreamMeal == namingDish;
            namingHint.text = dream
                ? "<color=#f28c33>It's what " + customer.name + " was missing.</color> Name it to put it on the menu."
                : "<color=#a6a199>Not on the menu. Name it to save it.</color>";
            namingField.text = namingDish.DisplayName == CustomerRelationship.Nameless ? "" : namingDish.DisplayName;
            namingField.interactable = true;
            saveDishButton.interactable = true;
        }

        private void SaveDish()
        {
            if(namingDish == null) return;

            bool saved = TrySaveDish(namingDish, namingField.text, out string message);
            namingHint.text = message;
            if(!saved) return;

            namingField.interactable = false;
            saveDishButton.interactable = false;
            RefreshAll();
        }

        /// <summary>The history page's save row: shown for a served dish that isn't on the menu.</summary>
        private void RefreshHistorySave(HistoryEntry entry)
        {
            RecipeDefinition dish = entry != null ? entry.Dish : null;
            bool show = dish != null && !IsOnMenu(dish) && !offMenu.Contains(dish);
            historySaveRow.SetActive(show);
            if(!show) return;

            if(historySaveField.isFocused) return;
            historySaveHint.text = "<color=#a6a199>Not on the menu. Name it to save it, so they can order it by name.</color>";
            historySaveField.text = dish.DisplayName == CustomerRelationship.Nameless ? "" : dish.DisplayName;
        }

        private void SaveDishFromHistory()
        {
            if(selectedEntry == null || selectedEntry.Dish == null) return;

            bool saved = TrySaveDish(selectedEntry.Dish, historySaveField.text, out string message);
            if(!saved)
            {
                historySaveHint.text = message;
                return;
            }

            RefreshAll();
        }

        /// <summary>Names <paramref name="dish"/> and puts it on the menu. False (with why) when the name won't do.</summary>
        private bool TrySaveDish(RecipeDefinition dish, string name, out string message)
        {
            string chosen = name != null ? name.Trim() : "";

            if(chosen.Length == 0)
            {
                message = "<color=#e5594d>Give it a name first.</color>";
                return false;
            }

            if(chosen.Length > DishNameLimit) chosen = chosen.Substring(0, DishNameLimit);

            RecipeDefinition existing = CustomerDialogue.FindByName(VisibleMenu(), chosen);
            if(existing != null && existing != dish)
            {
                message = "<color=#e5594d>There's already a dish called \"" + existing.DisplayName + "\" on the menu.</color>";
                return false;
            }

            CustomerRelationship.Rename(dish, chosen);
            if(!menu.Contains(dish)) menu.Add(dish);
            deletedDishes.Remove(dish);

            message = "<color=#73cc73>\"" + chosen + "\" is on the menu.</color>";
            Say("\"" + chosen + "\" is on the menu and in the recipe book.", UIFactory.Accent);
            return true;
        }

        private void BuildNamingRow(RectTransform parent)
        {
            namingRow = BuildNameGroup(parent, "Naming", 14, SaveDish, out namingHint, out namingField, out saveDishButton);
        }

        private void BuildHistorySaveRow(RectTransform parent)
        {
            historySaveRow = BuildNameGroup(parent, "Save From History", 14, SaveDishFromHistory, out historySaveHint, out historySaveField, out historySaveButton);
        }

        /// <summary>A hint, a name field and a Save dish button.</summary>
        private static GameObject BuildNameGroup(RectTransform parent, string name, int fontSize, UnityEngine.Events.UnityAction save,
            out Text hint, out InputField field, out Button button)
        {
            RectTransform group = UIFactory.Rect(parent, name);
            VerticalLayoutGroup layout = UIFactory.Vertical(group.gameObject, 6f, 0);
            layout.childForceExpandHeight = false;

            hint = UIFactory.Label(group, "", fontSize - 2);

            RectTransform row = UIFactory.Row(group, fontSize * 2f + 4f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;

            Image fieldBackground = UIFactory.Panel(row, "Name Field", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(fieldBackground, fontSize * 2f + 4f, 0f, -1f, 1f);
            field = fieldBackground.gameObject.AddComponent<InputField>();
            field.targetGraphic = fieldBackground;
            field.characterLimit = DishNameLimit;

            Text text = UIFactory.Label(fieldBackground.transform, "", fontSize, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            UIFactory.Stretch(text.rectTransform).offsetMin = new Vector2(12f, 0f);
            Text placeholder = UIFactory.Label(fieldBackground.transform, "Name the dish", fontSize, TextAnchor.MiddleLeft,
                FontStyle.Italic, UIFactory.MutedText);
            UIFactory.Stretch(placeholder.rectTransform).offsetMin = new Vector2(12f, 0f);
            field.textComponent = text;
            field.placeholder = placeholder;
            field.onSubmit.AddListener(_ => save());

            button = UIFactory.Button(row, "Save dish", save, new Color(0.25f, 0.4f, 0.25f, 1f), fontSize - 2);
            UIFactory.Size(button, fontSize * 2f + 4f, 0f, fontSize * 6.5f, 0f);

            group.gameObject.SetActive(false);
            return group.gameObject;
        }
    }
}
