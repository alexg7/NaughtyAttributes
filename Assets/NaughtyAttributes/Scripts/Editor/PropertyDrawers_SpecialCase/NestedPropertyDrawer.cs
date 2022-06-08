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
    internal class NestedPropertyDrawer : PropertyDrawer
    {
        private Type targetType;
        private NaughtyInspector inspector;

        private List<SerializedProperty> _serializedProperties = new List<SerializedProperty>();
        private IEnumerable<FieldInfo> _nonSerializedFields;
        private IEnumerable<PropertyInfo> _nativeProperties;
        private IEnumerable<MethodInfo> _methods;
        private Dictionary<string, SavedBool> _foldouts = new Dictionary<string, SavedBool>();

        private GUIStyle _labelStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle GetLabelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.boldLabel);
                _labelStyle.richText = true;
            }

            return _labelStyle;
        }

        private GUIStyle GetFoldoutStyle()
        {
            if (_foldoutStyle == null)
            {
                _foldoutStyle = new GUIStyle(EditorStyles.foldoutHeader);
                _foldoutStyle.fontStyle = FontStyle.Bold;
                _foldoutStyle.richText = true;
            }

            return _foldoutStyle;
        }

        public NestedPropertyDrawer(Type type)
        {
            targetType = type;

            _nonSerializedFields = ReflectionUtility.GetAllFields(targetType, f => f.GetCustomAttributes(typeof(ShowNonSerializedFieldAttribute), true).Length > 0);

            _nativeProperties = ReflectionUtility.GetAllProperties(targetType, p => p.GetCustomAttributes(typeof(ShowNativePropertyAttribute), true).Length > 0);

            _methods = ReflectionUtility.GetAllMethods(targetType, m => m.GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0);
        }

        protected void GetSerializedProperties(SerializedProperty property, ref List<SerializedProperty> outSerializedProperties)
        {
            outSerializedProperties.Clear();
            outSerializedProperties.AddRange(property.GetVisibleChildren());
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (Event.current.type == EventType.Layout)
                return;

            GetSerializedProperties(property, ref _serializedProperties);

            float h = 0.0f;

            SpecialCaseDrawerAttribute specialCaseAttribute = PropertyUtility.GetAttribute<SpecialCaseDrawerAttribute>(property);
            if (specialCaseAttribute != null)
            {
                var specDrawer = specialCaseAttribute.GetDrawer();
                h = specDrawer.GetPropertyHeight(property);
                specDrawer.OnGUI(position, property);
            }
            else
            {
                var drawer = property.FindDrawer();
                if (drawer != null)
                {
                    h = drawer.GetPropertyHeight(property, label);
                    drawer.OnGUI(position, property, label);
                }
                else
                    h = DrawSerializedProperties(position, property, label);
            }

            if (!property.isExpanded)
                return;

            position.yMin = h;

            DrawNonSerializedFields(position, property);
            DrawNativeProperties(position, property);
            DrawButtons(position, property);
        }

        private float GetChildPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = 0.0f; // GetPropertyHeight(property, null);
            var specialCaseAttribute = PropertyUtility.GetAttribute<SpecialCaseDrawerAttribute>(property);
            if (specialCaseAttribute != null)
            {
                h = specialCaseAttribute.GetDrawer().GetPropertyHeight(property);
            }
            else
            {
                var drawer = property.FindDrawer();
                if (drawer != null)
                    h = drawer.GetPropertyHeight(property, label);
                else
                    h = EditorGUI.GetPropertyHeight(property, label, true);
            }

            return h;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = 0.0f;

            GetSerializedProperties(property, ref _serializedProperties);

            SpecialCaseDrawerAttribute specialCaseAttribute = PropertyUtility.GetAttribute<SpecialCaseDrawerAttribute>(property);
            if (specialCaseAttribute != null)
            {
                height = specialCaseAttribute.GetDrawer().GetPropertyHeight(property) + 1;
            }
            else
            {
                var drawer = property.FindDrawer();
                if (drawer != null)
                {
                    height = drawer.GetPropertyHeight(property, label) + 1;
                }
                else
                {
                    if (property.hasVisibleChildren)
                        height = EditorGUIUtility.singleLineHeight + 1;

                    if (!property.isExpanded)
                        return height;

                    var props = GetNonGroupedProperties(_serializedProperties).ToList();
                    // Draw non-grouped serialized properties
                    foreach (var prop in props)
                    {
                        float h = GetChildPropertyHeight(prop, null);
                        height += h + 1;
                    }
                }
            }

            height += GetNonSerializedHeight(property);
            height += GetNativeHeight(property);
            height += GetButtonsHeight(property);

            return height;
        }

        protected float DrawSerializedProperties(Rect rect, SerializedProperty property, GUIContent label)
        {
            Rect r = rect;

            if (property.hasVisibleChildren)
            {
                r.height = EditorGUIUtility.singleLineHeight;
                bool isExpanded = EditorGUI.Foldout(r, property.isExpanded, PropertyUtility.GetLabel(property), true, GetFoldoutStyle());
                if (property.isExpanded != isExpanded)
                {
                    property.isExpanded = isExpanded;
                    EditorUtility.SetDirty(property.serializedObject.targetObject);
                }

                if (!property.isExpanded)
                    return EditorGUIUtility.singleLineHeight;

                r.y += r.height + 1;
            }

            //~serializedObject.Update();

            var properties = GetNonGroupedProperties(_serializedProperties).ToList();
            // Draw non-grouped serialized properties
            foreach (var prop in properties)
            {
                float height = GetChildPropertyHeight(prop, null);

                r.height = height;

                var drawer = prop.FindDrawer();
                if (drawer != null)
                    drawer.OnGUI(r, prop, null);
                else
                    NaughtyEditorGUI.PropertyField(r, prop, includeChildren: true);

                r.y += height + 1;
            }
            
            // Draw grouped serialized properties
            foreach (var group in GetGroupedProperties(_serializedProperties))
            {
                IEnumerable<SerializedProperty> visibleProperties = group.Where(p => PropertyUtility.IsVisible(p));
                if (!visibleProperties.Any())
                {
                    continue;
                }

                NaughtyEditorGUI.BeginBoxGroup_Layout(group.Key);
                foreach (var prop in visibleProperties)
                {
                    NaughtyEditorGUI.PropertyField(rect, prop, includeChildren: true);
                }

                NaughtyEditorGUI.EndBoxGroup_Layout();
            }

            // Draw foldout serialized properties
            foreach (var group in GetFoldoutProperties(_serializedProperties))
            {
                IEnumerable<SerializedProperty> visibleProperties = group.Where(p => PropertyUtility.IsVisible(p));
                if (!visibleProperties.Any())
                {
                    continue;
                }

                if (!_foldouts.ContainsKey(group.Key))
                {
                    _foldouts[group.Key] = new SavedBool($"{inspector.target.GetInstanceID()}.{group.Key}", false);
                }

                _foldouts[group.Key].Value = EditorGUI.Foldout(rect, _foldouts[group.Key].Value, group.Key, true);
                if (_foldouts[group.Key].Value)
                {
                    foreach (var prop in visibleProperties)
                    {
                        NaughtyEditorGUI.PropertyField(rect, prop, true);
                    }
                }
            }

            //~serializedObject.ApplyModifiedProperties();

            return r.y;
        }

        protected float GetNonSerializedHeight(SerializedProperty property, bool drawHeader = false)
        {
            float height = 0.0f;
            if (_nonSerializedFields.Any())
            {
                if (drawHeader)
                {
                    height += EditorGUIUtility.singleLineHeight + HorizontalLineAttribute.DefaultHeight;
                }

                float lineHeight = EditorGUIUtility.singleLineHeight;
                foreach (var method in _nonSerializedFields)
                {
                    height += lineHeight;
                }
            }

            return height;
        }

        protected void DrawNonSerializedFields(Rect rect, SerializedProperty property, bool drawHeader = false)
        {
            if (_nonSerializedFields.Any())
            {
                if (drawHeader)
                {
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(rect, "Non-Serialized Fields", GetHeaderGUIStyle());
                    rect.y += rect.height;
                    rect.height = HorizontalLineAttribute.DefaultHeight;
                    NaughtyEditorGUI.HorizontalLine(rect, HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                    rect.y += rect.height;
                }

                rect.height = EditorGUIUtility.singleLineHeight;

                foreach (var field in _nonSerializedFields)
                {
                    NaughtyEditorGUI.NonSerializedField(rect, property.serializedObject.targetObject, field);
                    rect.y += rect.height;
                }
            }
        }

        protected float GetNativeHeight(SerializedProperty property, bool drawHeader = false)
        {
            float height = 0.0f;
            if (_nativeProperties.Any())
            {
                if (drawHeader)
                {
                    height += EditorGUIUtility.singleLineHeight + HorizontalLineAttribute.DefaultHeight;
                }

                float lineHeight = EditorGUIUtility.singleLineHeight;
                foreach (var method in _nativeProperties)
                {
                    height += lineHeight;
                }
            }

            return height;
        }

        protected void DrawNativeProperties(Rect rect, SerializedProperty property, bool drawHeader = false)
        {
            if (_nativeProperties.Any())
            {
                if (drawHeader)
                {
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(rect, "Native Properties", GetHeaderGUIStyle());
                    rect.y += rect.height;
                    rect.height = HorizontalLineAttribute.DefaultHeight;
                    NaughtyEditorGUI.HorizontalLine(rect, HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                    rect.y += rect.height;
                }

                rect.height = EditorGUIUtility.singleLineHeight;

                foreach (var _property in _nativeProperties)
                {
                    NaughtyEditorGUI.NativeProperty(rect, property.serializedObject.targetObject, _property);
                    rect.y += rect.height;
                }
            }
        }

        protected float GetButtonsHeight(SerializedProperty property, bool drawHeader = false)
        {
            float height = 0.0f;
            if (_methods.Any())
            {
                if (drawHeader)
                {
                    height += EditorGUIUtility.singleLineHeight + HorizontalLineAttribute.DefaultHeight;
                }

                float buttonHeight = EditorGUIUtility.singleLineHeight;
                foreach (var method in _methods)
                {
                    height += buttonHeight;
                }
            }

            return height;
        }

        protected void DrawButtons(Rect rect, SerializedProperty property, bool drawHeader = false)
        {
            if (_methods.Any())
            {
                if (drawHeader)
                {
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(rect, "Buttons", GetHeaderGUIStyle());
                    rect.y += rect.height;
                    rect.height = HorizontalLineAttribute.DefaultHeight;
                    NaughtyEditorGUI.HorizontalLine(rect, HorizontalLineAttribute.DefaultHeight, HorizontalLineAttribute.DefaultColor.GetColor());
                    rect.y += rect.height;
                }

                rect.height = EditorGUIUtility.singleLineHeight;

                foreach (var method in _methods)
                {
                    NaughtyEditorGUI.Button(rect, property.serializedObject.targetObject, method);
                    rect.y += rect.height;
                }
            }
        }

        private static IEnumerable<SerializedProperty> GetNonGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties.Where(p => PropertyUtility.GetAttribute<IGroupAttribute>(p) == null);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetGroupedProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<BoxGroupAttribute>(p).Name);
        }

        private static IEnumerable<IGrouping<string, SerializedProperty>> GetFoldoutProperties(IEnumerable<SerializedProperty> properties)
        {
            return properties
                .Where(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p) != null)
                .GroupBy(p => PropertyUtility.GetAttribute<FoldoutAttribute>(p).Name);
        }

        private static GUIStyle GetHeaderGUIStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.UpperCenter;

            return style;
        }
    }
}
