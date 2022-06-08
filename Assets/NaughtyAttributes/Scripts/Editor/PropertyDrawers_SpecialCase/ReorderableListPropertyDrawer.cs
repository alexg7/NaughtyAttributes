using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Collections;

namespace NaughtyAttributes.Editor
{
    public class ReorderableListPropertyDrawer : SpecialCasePropertyDrawerBase
    {
        public static readonly ReorderableListPropertyDrawer Instance = new ReorderableListPropertyDrawer();

        private readonly Dictionary<string, ReorderableListWrapper> _reorderableListsByPropertyName = new Dictionary<string, ReorderableListWrapper>();

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

        private string GetPropertyKeyName(SerializedProperty property)
        {
            return property.serializedObject.targetObject.GetInstanceID() + "." + property.propertyPath; //property.name;
        }

        protected override float GetPropertyHeight_Internal(SerializedProperty property)
        {
            if (property.isArray)
            {
                string key = GetPropertyKeyName(property);

                var reorderableList = GetOrCreateList(property);
                if (reorderableList == null)
                {
                    return 0;
                }

                return reorderableList.GetHeight() + 2.0f; // 2 pix space after the list
            }

            return EditorGUI.GetPropertyHeight(property, true) + 2.0f; // 2 pix space after the list
        }

        ReorderableList GetOrCreateList(SerializedProperty property)
        {
            string key = GetPropertyKeyName(property);

            if (!_reorderableListsByPropertyName.TryGetValue(key, out var reorderableList))
            {
                reorderableList = new ReorderableListWrapper(property.serializedObject, property, true, true, true, true);
                reorderableList.list.drawHeaderCallback = (Rect r) =>
                    {
                        //EditorGUI.LabelField(r, string.Format("{0}: {1}", PropertyUtility.GetLabel(property), property.arraySize), GetLabelStyle());
                        HandleDragAndDrop(r, reorderableList.list);
                        r.xMin += 10.0f;
                        bool isExpanded = EditorGUI.Foldout(r, property.isExpanded, string.Format("{0}: {1}", PropertyUtility.GetLabel(property), property.arraySize), true, GetFoldoutStyle());
                        if (property.isExpanded != isExpanded)
                        {
                            property.isExpanded = isExpanded;
                            EditorUtility.SetDirty(property.serializedObject.targetObject);
                        }
                    };

                reorderableList.list.drawElementCallback = (Rect r, int index, bool isActive, bool isFocused) =>
                    {
                        if (property.isExpanded)
                            DrawElement(property, r, index, isActive, isFocused);
                    };

                reorderableList.list.elementHeightCallback = (int index) =>
                    {
                        if (!property.isExpanded)
                            return (index == 0) ? EditorGUIUtility.singleLineHeight : 0.0f;

                        if (property.arraySize > index)
                        {
                            SerializedProperty element = property.GetArrayElementAtIndex(index);
                            return ListItemPropertyDrawer.Instance.GetPropertyHeight(element) + 2.0f; // height + 2 pix space between lines
                        }

                        return 0.0f;
                    };

                //reorderableList.list.drawFooterCallback = (Rect r) =>
                //    {
                //        if (Event.current.type == EventType.Repaint)
                //        {
                //            //ReorderableList.defaultBehaviours.footerBackground.Draw(r, false, false, false, false);
                //            //ReorderableList.defaultBehaviours.boxBackground.Draw(r, false, false, false, false);
                //        }

                //        //if (displayAdd || displayRemove)
                //            ReorderableList.defaultBehaviours.DrawFooter(r, reorderableList.list); // draw the footer if the add or remove buttons are required
                //    };

                _reorderableListsByPropertyName.Add(key, reorderableList);
            }

            return reorderableList.list;
        }

        protected override void OnGUI_Internal(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (property.isArray)
            {
                var reorderableList = GetOrCreateList(property);

                if (rect == default)
                    reorderableList.DoLayoutList();
                else
                    reorderableList.DoList(rect);
            }
            else
            {
                string message = typeof(ReorderableListAttribute).Name + " can be used only on arrays or lists";
                NaughtyEditorGUI.HelpBox_Layout(message, MessageType.Warning, context: property.serializedObject.targetObject);
                EditorGUILayout.PropertyField(property, true);
            }
        }

        private void DrawElement(SerializedProperty property, Rect r, int index, bool isActive, bool isFocused)
        {
            if (property.arraySize > index)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(index);
                r.y += 1.0f;
                r.x += 10.0f;
                r.width -= 10.0f;
                r.height -= 2.0f; // 2 pix spacing
                ListItemPropertyDrawer.Instance.OnGUI(r, element);
            }
        }
        
        public void ClearCache()
        {
            ListItemPropertyDrawer.Instance.ClearCache();
            _reorderableListsByPropertyName.Clear();
        }

        private Object GetAssignableObject(Object obj, ReorderableList list)
        {
            System.Type listType = PropertyUtility.GetPropertyType(list.serializedProperty);
            System.Type elementType = ReflectionUtility.GetListElementType(listType);

            if (elementType == null)
            {
                return null;
            }

            System.Type objType = obj.GetType();

            if (elementType.IsAssignableFrom(objType))
            {
                return obj;
            }

            if (objType == typeof(GameObject))
            {
                if (typeof(Transform).IsAssignableFrom(elementType))
                {
                    Transform transform = ((GameObject)obj).transform;
                    if (elementType == typeof(RectTransform))
                    {
                        RectTransform rectTransform = transform as RectTransform;
                        return rectTransform;
                    }
                    else
                    {
                        return transform;
                    }
                }
                else if (typeof(MonoBehaviour).IsAssignableFrom(elementType))
                {
                    return ((GameObject)obj).GetComponent(elementType);
                }
            }

            return null;
        }

        private void HandleDragAndDrop(Rect rect, ReorderableList list)
        {
            var currentEvent = Event.current;
            var usedEvent = false;

            switch (currentEvent.type)
            {
                case EventType.DragExited:
                    if (GUI.enabled)
                    {
                        HandleUtility.Repaint();
                    }

                    break;

                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (rect.Contains(currentEvent.mousePosition) && GUI.enabled)
                    {
                        // Check each single object, so we can add multiple objects in a single drag.
                        bool didAcceptDrag = false;
                        Object[] references = DragAndDrop.objectReferences;
                        foreach (Object obj in references)
                        {
                            Object assignableObject = GetAssignableObject(obj, list);
                            if (assignableObject != null)
                            {
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                                if (currentEvent.type == EventType.DragPerform)
                                {
                                    list.serializedProperty.arraySize++;
                                    int arrayEnd = list.serializedProperty.arraySize - 1;
                                    list.serializedProperty.GetArrayElementAtIndex(arrayEnd).objectReferenceValue = assignableObject;
                                    didAcceptDrag = true;
                                }
                            }
                        }

                        if (didAcceptDrag)
                        {
                            GUI.changed = true;
                            DragAndDrop.AcceptDrag();
                            usedEvent = true;
                        }
                    }

                    break;
            }

            if (usedEvent)
            {
                currentEvent.Use();
            }
        }
    }

    internal class ReorderableListWrapper
    {
        public ReorderableList list;

        public ReorderableListWrapper(IList elements, System.Type elementType)
        {
            list = new ReorderableList(elements, elementType);
        }

        public ReorderableListWrapper(IList elements, System.Type elementType, bool draggable, bool displayHeader, bool displayAddButton, bool displayRemoveButton)
        {
            list = new ReorderableList(elements, elementType, draggable, displayHeader, displayAddButton, displayRemoveButton);
        }

        public ReorderableListWrapper(SerializedObject serializedObject, SerializedProperty elements)
        {
            list = new ReorderableList(serializedObject, elements);
        }

        public ReorderableListWrapper(SerializedObject serializedObject, SerializedProperty elements, bool draggable, bool displayHeader, bool displayAddButton, bool displayRemoveButton)
        {
            list = new ReorderableList(serializedObject, elements, draggable, displayHeader, displayAddButton, displayRemoveButton);
        }
    }
}
