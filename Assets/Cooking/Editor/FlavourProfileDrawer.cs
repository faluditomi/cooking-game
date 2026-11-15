using UnityEditor;
using UnityEngine;

namespace Cooking.EditorTools
{
    /// <summary>Compact 4×2 grid for <see cref="FlavourProfile"/> fields instead of eight stacked rows.</summary>
    [CustomPropertyDrawer(typeof(FlavourProfile))]
    public class FlavourProfileDrawer : PropertyDrawer
    {
        private const int Columns = 4;
        private const float CellGap = 6f;

        private static readonly string[] FieldNames =
        {
            "savoury", "salty", "sweet", "bitter", "sour", "pain", "nutrition", "texture",
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int rows = Mathf.CeilToInt(FieldNames.Length / (float)Columns);
            return EditorGUIUtility.singleLineHeight * (rows + 1) + EditorGUIUtility.standardVerticalSpacing * rows;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect labelRect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(labelRect, label);

            Rect body = EditorGUI.IndentedRect(new Rect(position.x, position.y + line + spacing, position.width, position.height - line - spacing));

            int oldIndent = EditorGUI.indentLevel;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = 56f;

            float cellWidth = (body.width - CellGap * (Columns - 1)) / Columns;

            for(int i = 0; i < FieldNames.Length; i++)
            {
                SerializedProperty child = property.FindPropertyRelative(FieldNames[i]);
                if(child == null) continue;

                int row = i / Columns;
                int column = i % Columns;
                Rect cell = new Rect(body.x + column * (cellWidth + CellGap), body.y + row * (line + spacing), cellWidth, line);

                GUIContent cellLabel = new GUIContent(FlavourAxes.Label(FlavourAxes.All[i]), child.tooltip);
                EditorGUI.PropertyField(cell, child, cellLabel);
            }

            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUI.indentLevel = oldIndent;

            EditorGUI.EndProperty();
        }
    }
}
