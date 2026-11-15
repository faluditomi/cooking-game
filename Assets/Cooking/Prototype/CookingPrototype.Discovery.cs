using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Discovering a regular's dream meal: after the eureka result, the player names the new recipe
    /// before it goes into the recipe book.
    /// </summary>
    public partial class CookingPrototype
    {
        private GameObject namingOverlay;
        private Text namingTitle;
        private InputField namingField;
        private CustomerRelationship namingFor;

        private void ShowNamingPrompt(CustomerRelationship relationship)
        {
            if(relationship?.DreamMeal == null) return;

            namingFor = relationship;
            namingTitle.text = "<b>New recipe discovered</b>\n<size=20><color=#a6a199>Nobody has made this before. It's what "
                               + relationship.Customer.name + " was missing. What do you call it?</color></size>";
            namingField.text = relationship.DreamMeal.DisplayName;
            namingOverlay.SetActive(true);
            namingOverlay.transform.SetAsLastSibling();
            namingField.Select();
            namingField.ActivateInputField();
        }

        private void ConfirmName()
        {
            if(namingFor == null) return;

            string chosen = string.IsNullOrWhiteSpace(namingField.text) ? namingFor.DreamMeal.DisplayName : namingField.text.Trim();
            namingFor.NameDreamMeal(chosen);
            Say("\"" + chosen + "\" is in the recipe book.", UIFactory.Accent);
            namingOverlay.SetActive(false);
            namingFor = null;
            RefreshAll();
        }

        private void BuildNamingPrompt(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Naming Overlay", new Color(0f, 0f, 0f, 0.6f));
            UIFactory.Stretch(overlay.rectTransform);
            namingOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Naming", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 330f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 16f, 30);
            layout.childForceExpandHeight = false;

            namingTitle = UIFactory.Label(panel, "", 30, TextAnchor.UpperLeft, FontStyle.Normal, UIFactory.Accent);

            Image fieldBackground = UIFactory.Panel(panel, "Name Field", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(fieldBackground, 56f, 0f);
            namingField = fieldBackground.gameObject.AddComponent<InputField>();
            namingField.targetGraphic = fieldBackground;
            namingField.characterLimit = 48;

            Text text = UIFactory.Label(fieldBackground.transform, "", 26, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            UIFactory.Stretch(text.rectTransform).offsetMin = new Vector2(14f, 0f);
            Text placeholder = UIFactory.Label(fieldBackground.transform, "Name the dish", 26, TextAnchor.MiddleLeft, FontStyle.Italic, UIFactory.MutedText);
            UIFactory.Stretch(placeholder.rectTransform).offsetMin = new Vector2(14f, 0f);
            namingField.textComponent = text;
            namingField.placeholder = placeholder;
            namingField.onSubmit.AddListener(_ => ConfirmName());

            RectTransform row = UIFactory.Row(panel, 52f);
            UIFactory.Button(row, "Add to the recipe book", ConfirmName, new Color(0.75f, 0.42f, 0.15f, 1f), 22);

            namingOverlay.SetActive(false);
        }
    }
}
