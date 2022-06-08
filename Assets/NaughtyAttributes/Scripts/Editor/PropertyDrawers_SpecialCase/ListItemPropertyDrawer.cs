using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System;

namespace NaughtyAttributes.Editor
{
    public class ListItemPropertyDrawer : SpecialCasePropertyDrawerBase
    {
        public static readonly ListItemPropertyDrawer Instance = new ListItemPropertyDrawer();

        private readonly Dictionary<string, PropertyDrawer> _listItemsByPropertyName = new Dictionary<string, PropertyDrawer>();

        public void ClearCache()
        {
            _listItemsByPropertyName.Clear();
        }

        private string GetPropertyKeyName(SerializedProperty property)
        {
            return property.serializedObject.targetObject.GetInstanceID() + "." + property.propertyPath; //property.name;
        }

        private PropertyDrawer GetItemDrawer(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Generic)
                return null;

            string key = GetPropertyKeyName(property);

            if (!_listItemsByPropertyName.TryGetValue(key, out PropertyDrawer drawer))
            {
                Type type = property.GetPropertyFieldType();
                drawer = new NestedPropertyDrawer(type);
                _listItemsByPropertyName.Add(key, drawer);
            }

            return drawer;
        }

        public static IEnumerator GetDirectChildren(SerializedProperty parent)
        {
            int dots = parent.propertyPath.Count(c => c == '.');
            foreach (SerializedProperty inner in parent)
            {
                bool isDirectChild = inner.propertyPath.Count(c => c == '.') == dots + 1;
                if (isDirectChild)
                    yield return inner;
            }
        }

        protected override float GetPropertyHeight_Internal(SerializedProperty property)
        {
            float height = 0f;

            SpecialCaseDrawerAttribute specialCaseAttribute = PropertyUtility.GetAttribute<SpecialCaseDrawerAttribute>(property);
            if (specialCaseAttribute != null)
            {
                height = specialCaseAttribute.GetDrawer().GetPropertyHeight(property);
            }
            else
            {
                var drawer = GetItemDrawer(property);
                if (drawer != null)
                    height = drawer.GetPropertyHeight(property, null);
                else
                    height = EditorGUI.GetPropertyHeight(property, true);
            }

            height += 2;
            return height;
        }

        protected override void OnGUI_Internal(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (Event.current.type == EventType.Layout)
                return;

            rect.height -= 2;

            //drawer = property.FindDrawer();
            var drawer = GetItemDrawer(property);
            if (drawer != null)
            {
                drawer.OnGUI(rect, property, label);

                return;
            }

            EditorGUI.PropertyField(rect, property, label);
        }
    }
}
