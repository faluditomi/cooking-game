using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Pictures of dishes and faces of regulars. A dish is drawn as in the design doc: the ingredient
    /// sprites heaped on a plate, with brown char marks when it's fried and a layer of broth when it's
    /// a soup. Used on the desk's plates, the Dish Served page, the recipe and history pages and lists,
    /// and the shop's bundles.
    /// </summary>
    public partial class CookingPrototype
    {
        private const int MaxPieces = 10;

        /// <summary>A square dish picture for a layout (list rows, bundle tiles).</summary>
        private RectTransform DishIcon(Transform parent, IReadOnlyList<IngredientPortion> portions, float size)
        {
            RectTransform root = UIFactory.Rect(parent, "Dish Picture");
            UIFactory.Size(root, size, 0f, size, 0f);
            BuildDishPicture(root, portions);
            return root;
        }

        /// <summary>Draws the dish into <paramref name="root"/> (which keeps its own size and anchors).</summary>
        private void BuildDishPicture(RectTransform root, IReadOnlyList<IngredientPortion> portions)
        {
            UIFactory.ClearChildren(root);
            if(portions == null) return;

            bool soup = false;
            bool fried = false;
            int burnt = 0;
            List<Sprite> pieces = new List<Sprite>();
            List<Sprite> seasonings = new List<Sprite>();

            foreach(IngredientPortion portion in portions)
            {
                if(portion == null || !portion.IsValid) continue;
                soup |= (portion.cooking & CookMethods.Boiled) != 0;
                fried |= (portion.cooking & CookMethods.Fried) != 0;
                if(!portion.IsRaw && portion.doneness == Doneness.Burnt) burnt += portion.quantity;

                // Seasonings are a sprinkle, not a piece of the heap.
                List<Sprite> into = portion.ingredient.IsSeasoning ? seasonings : pieces;
                for(int i = 0; i < portion.quantity && into.Count < MaxPieces; i++) into.Add(portion.ingredient.Icon);
            }

            // A plate of only seasonings (an MSG2 cube, say) shows them.
            if(pieces.Count == 0) pieces.AddRange(seasonings.GetRange(0, Mathf.Min(3, seasonings.Count)));

            RectTransform frame = UIFactory.Rect(root, "Frame");
            UIFactory.Stretch(frame);
            frame.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            Image plateImage = UIFactory.Panel(frame, "Plate", plateSprite != null ? Color.white : new Color(0.86f, 0.84f, 0.79f, 1f));
            plateImage.sprite = plateSprite != null ? plateSprite : UIFactory.Circle;
            plateImage.preserveAspect = true;
            plateImage.raycastTarget = false;
            UIFactory.Stretch(plateImage.rectTransform);

            if(soup)
            {
                // A bowl's worth of broth under the pieces...
                Image bowl = UIFactory.Panel(frame, "Broth", new Color(0.72f, 0.5f, 0.24f, 0.85f));
                bowl.sprite = UIFactory.Circle;
                bowl.raycastTarget = false;
                UIFactory.Place(bowl.rectTransform, 0.13f, 0.13f, 0.87f, 0.87f);
            }

            RectTransform inside = UIFactory.Rect(frame, "Pieces");
            UIFactory.Place(inside, 0.18f, 0.18f, 0.82f, 0.82f);

            // Fried pieces come out golden; burnt ones dark.
            Color tint = !fried ? Color.white : burnt > 0 ? new Color(0.55f, 0.42f, 0.36f, 1f) : new Color(1f, 0.86f, 0.66f, 1f);
            AddHeap(inside, pieces, tint, 0.42f);

            if(fried)
            {
                // Brown char marks across the top, more of them (and darker) when something burnt.
                int marks = Mathf.Min(10, 4 + burnt * 2);
                Color charColor = burnt > 0 ? new Color(0.12f, 0.06f, 0.02f, 0.85f) : new Color(0.4f, 0.2f, 0.06f, 0.7f);

                for(int i = 0; i < marks; i++)
                {
                    Vector2 at = HeapPoint(i, marks, 0.38f);
                    Image mark = UIFactory.Panel(inside, "Char", charColor);
                    mark.sprite = UIFactory.Circle;
                    mark.raycastTarget = false;
                    UIFactory.Place(mark.rectTransform, at.x - 0.11f, at.y - 0.025f, at.x + 0.11f, at.y + 0.025f);
                    mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i * 67) % 180 - 90f);
                }
            }

            if(soup)
            {
                // ...and over them, so they sit in it.
                Image surface = UIFactory.Panel(frame, "Broth Surface", new Color(0.78f, 0.56f, 0.28f, 0.45f));
                surface.sprite = UIFactory.Circle;
                surface.raycastTarget = false;
                UIFactory.Place(surface.rectTransform, 0.14f, 0.14f, 0.86f, 0.86f);
            }

            if(seasonings.Count > 0 && pieces.Count > seasonings.Count)
            {
                // A pinch of whatever seasoned it, on the rim.
                Image pinch = UIFactory.Icon(frame, seasonings[0], 0f);
                pinch.GetComponent<LayoutElement>().ignoreLayout = true;
                UIFactory.Place(pinch.rectTransform, 0.66f, 0.06f, 0.94f, 0.34f);
            }
        }

        /// <summary>Where piece <paramref name="index"/> of <paramref name="count"/> sits: a sunflower spiral, so they spread evenly.</summary>
        private static Vector2 HeapPoint(int index, int count, float spread)
        {
            float radius = count <= 1 ? 0f : spread * Mathf.Sqrt((index + 0.5f) / count);
            float angle = index * 2.39996f;
            return new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius * 0.85f);
        }

        /// <summary>Overlapping, slightly tipped pictures heaped inside <paramref name="inside"/>.</summary>
        private static void AddHeap(RectTransform inside, List<Sprite> sprites, Color tint, float spread)
        {
            for(int i = 0; i < sprites.Count; i++)
            {
                Vector2 centre = HeapPoint(i, sprites.Count, spread);
                float size = sprites.Count <= 3 ? 0.55f : sprites.Count <= 6 ? 0.44f : 0.36f;

                Image piece = UIFactory.Panel(inside, "Piece", sprites[i] != null ? tint : Color.clear);
                piece.sprite = sprites[i];
                piece.preserveAspect = true;
                piece.raycastTarget = false;
                UIFactory.Place(piece.rectTransform, centre.x - size * 0.5f, centre.y - size * 0.5f, centre.x + size * 0.5f, centre.y + size * 0.5f);
                piece.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ((i * 47) % 50) - 25f);
            }
        }

        /// <summary>A regular's face: the top of their picture, or a coloured placeholder.</summary>
        private static RectTransform FaceIcon(Transform parent, Customer regular, float size)
        {
            RectTransform root = UIFactory.Rect(parent, "Face");
            UIFactory.Size(root, size, 0f, size, 0f);
            Image back = root.gameObject.AddComponent<Image>();
            back.sprite = UIFactory.Circle;
            back.raycastTarget = false;

            float hue = regular != null ? (regular.name.GetHashCode() & 0xFFFF) / 65535f : 0f;
            back.color = Color.HSVToRGB(hue, 0.35f, 0.4f);

            Sprite portrait = regular != null ? regular.portrait : null;

            if(portrait == null)
            {
                Image head = UIFactory.Panel(root, "Head", Color.HSVToRGB(hue, 0.3f, 0.8f));
                head.sprite = UIFactory.Circle;
                head.raycastTarget = false;
                UIFactory.Place(head.rectTransform, 0.3f, 0.45f, 0.7f, 0.85f);
                Image body = UIFactory.Panel(root, "Body", Color.HSVToRGB(hue, 0.4f, 0.65f));
                body.sprite = UIFactory.Circle;
                body.raycastTarget = false;
                UIFactory.Place(body.rectTransform, 0.18f, -0.25f, 0.82f, 0.42f);
                root.gameObject.AddComponent<RectMask2D>();
                return root;
            }

            // Head and shoulders: the picture, wider than the icon, hung from its top.
            root.gameObject.AddComponent<RectMask2D>();
            Image picture = UIFactory.Panel(root, "Portrait", Color.white);
            picture.sprite = portrait;
            picture.raycastTarget = false;
            RectTransform rt = picture.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            float width = size * 1.5f;
            rt.sizeDelta = new Vector2(width, width * portrait.rect.height / portrait.rect.width);
            rt.anchoredPosition = new Vector2(0f, size * 0.05f);
            return root;
        }

        /// <summary>Puts <paramref name="picture"/> at the left of a list button and moves its label over.</summary>
        private static void PinLeft(Button button, RectTransform picture, float size)
        {
            picture.GetComponent<LayoutElement>().ignoreLayout = true;
            picture.anchorMin = picture.anchorMax = new Vector2(0f, 0.5f);
            picture.pivot = new Vector2(0f, 0.5f);
            picture.sizeDelta = new Vector2(size, size);
            picture.anchoredPosition = new Vector2(6f, 0f);
            Text text = button.GetComponentInChildren<Text>();
            text.rectTransform.offsetMin = new Vector2(size + 14f, text.rectTransform.offsetMin.y);
        }

        /// <summary>Puts <paramref name="picture"/> at the right of a list button.</summary>
        private static void PinRight(Button button, RectTransform picture, float size)
        {
            picture.GetComponent<LayoutElement>().ignoreLayout = true;
            picture.anchorMin = picture.anchorMax = new Vector2(1f, 0.5f);
            picture.pivot = new Vector2(1f, 0.5f);
            picture.sizeDelta = new Vector2(size, size);
            picture.anchoredPosition = new Vector2(-6f, 0f);
            Text text = button.GetComponentInChildren<Text>();
            text.rectTransform.offsetMax = new Vector2(-(size + 14f), text.rectTransform.offsetMax.y);
        }
    }
}
