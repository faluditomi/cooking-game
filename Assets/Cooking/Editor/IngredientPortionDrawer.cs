using UnityEditor;
using UnityEngine;

namespace Cooking.EditorTools
{
    /// <summary>One-line row for <see cref="IngredientPortion"/>: ingredient, quantity, prep, cooking, doneness.</summary>
    [CustomPropertyDrawer(typeof(IngredientPortion))]
    public class IngredientPortionDrawer : PropertyDrawer
    {
        private const float Gap = 4f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            int oldIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float[] weights = { 0.36f, 0.1f, 0.18f, 0.18f, 0.18f };
            string[] fields = { "ingredient", "quantity", "prep", "cooking", "doneness" };
            string[] tooltips = { "Ingredient", "Quantity", "Prep", "Cooking (none = raw)", "Doneness (cooked only)" };

            float available = position.width - Gap * (fields.Length - 1);
            float x = position.x;

            for(int i = 0; i < fields.Length; i++)
            {
                float width = available * weights[i];
                Rect rect = new Rect(x, position.y, width, position.height);
                SerializedProperty child = property.FindPropertyRelative(fields[i]);

                if(child != null)
                {
                    bool disabled = fields[i] == "doneness" && property.FindPropertyRelative("cooking").intValue == 0;

                    using(new EditorGUI.DisabledScope(disabled))
                    {
                        EditorGUI.PropertyField(rect, child, new GUIContent(string.Empty, tooltips[i]));
                    }
                }

                x += width + Gap;
            }

            EditorGUI.indentLevel = oldIndent;
            EditorGUI.EndProperty();
        }
    }
}
