using UnityEditor;
using System.Reflection;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NaughtyAttributes.Editor
{
    public static class PropertyUtility
    {
        public static T GetAttribute<T>(SerializedProperty property) where T : class
        {
            T[] attributes = GetAttributes<T>(property);
            return (attributes.Length > 0) ? attributes[0] : null;
        }

        public static T[] GetAttributes<T>(SerializedProperty property) where T : class
        {
            FieldInfo fieldInfo = ReflectionUtility.GetField(GetTargetObjectWithProperty(property), property.name);
            if (fieldInfo == null)
            {
                    return new T[] { };
            }

            return (T[])fieldInfo.GetCustomAttributes(typeof(T), true);
        }

        public static GUIContent GetLabel(SerializedProperty property)
        {
            LabelAttribute labelAttribute = GetAttribute<LabelAttribute>(property);
            string labelText = (labelAttribute == null)
                ? property.displayName
                : labelAttribute.Label;

            GUIContent label = new GUIContent(labelText);
            return label;
        }

        public static void CallOnValueChangedCallbacks(SerializedProperty property)
        {
            OnValueChangedAttribute[] onValueChangedAttributes = GetAttributes<OnValueChangedAttribute>(property);
            if (onValueChangedAttributes.Length == 0)
            {
                return;
            }

            object target = GetTargetObjectWithProperty(property);
            property.serializedObject.ApplyModifiedProperties(); // We must apply modifications so that the new value is updated in the serialized object

            foreach (var onValueChangedAttribute in onValueChangedAttributes)
            {
                MethodInfo callbackMethod = ReflectionUtility.GetMethod(target, onValueChangedAttribute.CallbackName);
                if (callbackMethod != null &&
                    callbackMethod.ReturnType == typeof(void) &&
                    callbackMethod.GetParameters().Length == 0)
                {
                    callbackMethod.Invoke(target, new object[] { });
                }
                else
                {
                    string warning = string.Format(
                        "{0} can invoke only methods with 'void' return type and 0 parameters",
                        onValueChangedAttribute.GetType().Name);

                    Debug.LogWarning(warning, property.serializedObject.targetObject);
                }
            }
        }

        public static bool IsEnabled(SerializedProperty property)
        {
            ReadOnlyAttribute readOnlyAttribute = GetAttribute<ReadOnlyAttribute>(property);
            if (readOnlyAttribute != null)
            {
                return false;
            }

            EnableIfAttributeBase enableIfAttribute = GetAttribute<EnableIfAttributeBase>(property);
            if (enableIfAttribute == null)
            {
                return true;
            }

            object target = GetTargetObjectWithProperty(property);

            // deal with enum conditions
            if (enableIfAttribute.EnumValue != null)
            {
                Enum value = GetEnumValue(target, enableIfAttribute.Conditions[0]);
                if (value != null)
                {
                    bool matched = value.GetType().GetCustomAttribute<FlagsAttribute>() == null
                        ? enableIfAttribute.EnumValue.Equals(value)
                        : value.HasFlag(enableIfAttribute.EnumValue);

                    return matched != enableIfAttribute.Inverted;
                }

                string message = enableIfAttribute.GetType().Name + " needs a valid enum field, property or method name to work";
                Debug.LogWarning(message, property.serializedObject.targetObject);

                return false;
            }

            // deal with normal conditions
            List<bool> conditionValues = GetConditionValues(target, enableIfAttribute.Conditions);
            if (conditionValues.Count > 0)
            {
                bool enabled = GetConditionsFlag(conditionValues, enableIfAttribute.ConditionOperator, enableIfAttribute.Inverted);
                return enabled;
            }
            else
            {
                string message = enableIfAttribute.GetType().Name + " needs a valid boolean condition field, property or method name to work";
                Debug.LogWarning(message, property.serializedObject.targetObject);

                return false;
            }
        }

        public static bool IsVisible(SerializedProperty property)
        {
            ShowIfAttributeBase showIfAttribute = GetAttribute<ShowIfAttributeBase>(property);
            if (showIfAttribute == null)
            {
                return true;
            }

            object target = GetTargetObjectWithProperty(property);

            // deal with enum conditions
            if (showIfAttribute.EnumValue != null)
            {
                Enum value = GetEnumValue(target, showIfAttribute.Conditions[0]);
                if (value != null)
                {
                    bool matched = value.GetType().GetCustomAttribute<FlagsAttribute>() == null
                        ? showIfAttribute.EnumValue.Equals(value)
                        : value.HasFlag(showIfAttribute.EnumValue);

                    return matched != showIfAttribute.Inverted;
                }

                string message = showIfAttribute.GetType().Name + " needs a valid enum field, property or method name to work";
                Debug.LogWarning(message, property.serializedObject.targetObject);

                return false;
            }

            // deal with normal conditions
            List<bool> conditionValues = GetConditionValues(target, showIfAttribute.Conditions);
            if (conditionValues.Count > 0)
            {
                bool enabled = GetConditionsFlag(conditionValues, showIfAttribute.ConditionOperator, showIfAttribute.Inverted);
                return enabled;
            }
            else
            {
                string message = showIfAttribute.GetType().Name + " needs a valid boolean condition field, property or method name to work";
                Debug.LogWarning(message, property.serializedObject.targetObject);

                return false;
            }
        }

        /// <summary>
        ///		Gets an enum value from reflection.
        /// </summary>
        /// <param name="target">The target object.</param>
        /// <param name="enumName">Name of a field, property, or method that returns an enum.</param>
        /// <returns>Null if can't find an enum value.</returns>
        internal static Enum GetEnumValue(object target, string enumName)
        {
            FieldInfo enumField = ReflectionUtility.GetField(target, enumName);
            if (enumField != null && enumField.FieldType.IsSubclassOf(typeof(Enum)))
            {
                return (Enum)enumField.GetValue(target);
            }

            PropertyInfo enumProperty = ReflectionUtility.GetProperty(target, enumName);
            if (enumProperty != null && enumProperty.PropertyType.IsSubclassOf(typeof(Enum)))
            {
                return (Enum)enumProperty.GetValue(target);
            }

            MethodInfo enumMethod = ReflectionUtility.GetMethod(target, enumName);
            if (enumMethod != null && enumMethod.ReturnType.IsSubclassOf(typeof(Enum)))
            {
                return (Enum)enumMethod.Invoke(target, null);
            }

            return null;
        }

        internal static List<bool> GetConditionValues(object target, string[] conditions)
        {
            List<bool> conditionValues = new List<bool>();
            foreach (var condition in conditions)
            {
                FieldInfo conditionField = ReflectionUtility.GetField(target, condition);
                if (conditionField != null &&
                    conditionField.FieldType == typeof(bool))
                {
                    conditionValues.Add((bool)conditionField.GetValue(target));
                }

                PropertyInfo conditionProperty = ReflectionUtility.GetProperty(target, condition);
                if (conditionProperty != null &&
                    conditionProperty.PropertyType == typeof(bool))
                {
                    conditionValues.Add((bool)conditionProperty.GetValue(target));
                }

                MethodInfo conditionMethod = ReflectionUtility.GetMethod(target, condition);
                if (conditionMethod != null &&
                    conditionMethod.ReturnType == typeof(bool) &&
                    conditionMethod.GetParameters().Length == 0)
                {
                    conditionValues.Add((bool)conditionMethod.Invoke(target, null));
                }
            }

            return conditionValues;
        }

        internal static bool GetConditionsFlag(List<bool> conditionValues, EConditionOperator conditionOperator, bool invert)
        {
            bool flag;
            if (conditionOperator == EConditionOperator.And)
            {
                flag = true;
                foreach (var value in conditionValues)
                {
                    flag = flag && value;
                }
            }
            else
            {
                flag = false;
                foreach (var value in conditionValues)
                {
                    flag = flag || value;
                }
            }

            if (invert)
            {
                flag = !flag;
            }

            return flag;
        }

        public static Type GetPropertyType(SerializedProperty property)
        {
            object obj = GetTargetObjectOfProperty(property);
            Type objType = obj.GetType();

            return objType;
        }

        /// <summary>
        /// Gets the object the property represents.
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        public static object GetTargetObjectOfProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return null;
            }

            string path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            string[] elements = path.Split('.');

            foreach (var element in elements)
            {
                if (element.Contains("["))
                {
                    string elementName = element.Substring(0, element.IndexOf("["));
                    int index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue_Imp(obj, elementName, index);
                }
                else
                {
                    obj = GetValue_Imp(obj, element);
                }
            }

            return obj;
        }

        /// <summary>
        /// Gets the object that the property is a member of
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        public static object GetTargetObjectWithProperty(SerializedProperty property)
        {
            string path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            string[] elements = path.Split('.');

            for (int i = 0; i < elements.Length - 1; i++)
            {
                string element = elements[i];
                if (element.Contains("["))
                {
                    string elementName = element.Substring(0, element.IndexOf("["));
                    int index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue_Imp(obj, elementName, index);
                }
                else
                {
                    obj = GetValue_Imp(obj, element);
                }
            }

            return obj;
        }

        private static object GetValue_Imp(object source, string name)
        {
            if (source == null)
            {
                return null;
            }

            Type type = source.GetType();

            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(source);
                }

                PropertyInfo property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    return property.GetValue(source, null);
                }

                type = type.BaseType;
            }

            return null;
        }

        private static object GetValue_Imp(object source, string name, int index)
        {
            IEnumerable enumerable = GetValue_Imp(source, name) as IEnumerable;
            if (enumerable == null)
            {
                return null;
            }

            IEnumerator enumerator = enumerable.GetEnumerator();
            for (int i = 0; i <= index; i++)
            {
                if (!enumerator.MoveNext())
                {
                    return null;
                }
            }

            return enumerator.Current;
        }

        // ===================================================
        // New stuff

        struct TypeAndFieldInfo
        {
            internal Type type;
            internal FieldInfo fi;
        }

        // Rev 3, be more evil with more cache!
        private static readonly Dictionary<int, TypeAndFieldInfo> s_PathHashVsType = new Dictionary<int, TypeAndFieldInfo>();
        private static readonly Dictionary<Type, PropertyDrawer> s_TypeVsDrawerCache = new Dictionary<Type, PropertyDrawer>();

        /// <summary>
        /// Searches for custom property drawer for given property, or returns null if no custom property drawer was found.
        /// </summary>
        public static PropertyDrawer FindDrawer(this SerializedProperty property)
        {
            PropertyDrawer drawer;
            TypeAndFieldInfo tfi;
            
            int pathHash = _GetUniquePropertyPathHash(property);

            if (!s_PathHashVsType.TryGetValue(pathHash, out tfi))
            {
                tfi.type = _GetPropertyType(property, out tfi.fi);
                s_PathHashVsType[pathHash] = tfi;
            }

            if (tfi.type == null)
                return null;

            if (!s_TypeVsDrawerCache.TryGetValue(tfi.type, out drawer))
            {
                drawer = tfi.type.FindDrawer();
                s_TypeVsDrawerCache.Add(tfi.type, drawer);
            }

            if (drawer != null)
            {
                // Drawer created by custom way like this will not have "fieldInfo" field installed
                // It is an optional, but some user code in advanced drawer might use it.
                // To install it, we must use reflection again, the backing field name is "internal FieldInfo m_FieldInfo"
                // See ref file in UnityCsReference (2019) project. Note that name could changed in future update.
                // unitycsreference\Editor\Mono\ScriptAttributeGUI\PropertyDrawer.cs
                var fieldInfoBacking = typeof(PropertyDrawer).GetField("m_FieldInfo", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fieldInfoBacking != null)
                    fieldInfoBacking.SetValue(drawer, tfi.fi);
            }
            
            return drawer;
        }

        /// <summary>
        /// Gets type of a serialized property.
        /// </summary>
        private static Type _GetPropertyType(SerializedProperty property, out FieldInfo fi)
        {
            // To see real property type, must dig into object that hosts it.
            property.GetPropertyFieldInfo(out Type resolvedType, out fi);
            return resolvedType;
        }

        /// <summary>
        /// For caching.
        /// </summary>
        private static int _GetUniquePropertyPathHash(SerializedProperty property)
        {
            int hash = property.serializedObject.targetObject.GetType().GetHashCode();
            hash += property.propertyPath.GetHashCode();
            return hash;
        }

        public static Type GetPropertyFieldType(this SerializedProperty property)
        {
            property.GetPropertyFieldInfo(out Type resolvedType, out _);
            return resolvedType;
        }

        private static void GetPropertyFieldInfo(this SerializedProperty property, out Type resolvedType, out FieldInfo fi)
        {
            resolvedType = null;
            fi = null;

            string[] fullPath = property.propertyPath.Split('.');

            // fi is FieldInfo in perspective of parentType (property.serializedObject.targetObject)
            // NonPublic to support [SerializeField] vars
            Type parentType = property.serializedObject.targetObject.GetType();
            fi = parentType.GetField(fullPath[0], BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fi == null)
                return;

            resolvedType = fi.FieldType;

            for (int i = 1; i < fullPath.Length; i++)
            {
                // To properly handle array and list
                // This has deeper rabbit hole, see
                // unitycsreference\Editor\Mono\ScriptAttributeGUI\ScriptAttributeUtility.cs GetFieldInfoFromPropertyPath
                // here we will simplify it for now (could break)

                // If we are at 'Array' section like in `tiles.Array.data[0].tilemodId`
                if (_IsArrayPropertyPath(fullPath, i))
                {
                    if (fi.FieldType.IsArray)
                        resolvedType = fi.FieldType.GetElementType();
                    else if (_IsListType(fi.FieldType, out Type underlying))
                        resolvedType = underlying;

                    i++; // skip also the 'data[x]' part
                    // In this case, fi is not updated, FieldInfo stay the same pointing to 'tiles' part
                }
                else
                {
                    fi = resolvedType.GetField(fullPath[i], BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    resolvedType = fi.FieldType;
                }
            }
        }

        static bool _IsArrayPropertyPath(string[] fullPath, int i)
        {
            // Also search for array pattern, thanks user https://gist.github.com/kkolyan
            // like `tiles.Array.data[0].tilemodId`
            // This is just a quick check, actual check in Unity uses RegEx
            return (fullPath[i] == "Array" && i + 1 < fullPath.Length && fullPath[i + 1].StartsWith("data"));
        }

        /// <summary>
        /// Stolen from unitycsreference\Editor\Mono\ScriptAttributeGUI\ScriptAttributeUtility.cs
        /// </summary>
        static bool _IsListType(Type t, out Type containedType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                containedType = t.GetGenericArguments()[0];
                return true;
            }

            containedType = null;
            return false;
        }

        /// <summary>
        /// Returns custom property drawer for type if one could be found, or null if
        /// no custom property drawer could be found. Does not use cached values, so it's resource intensive.
        /// </summary>
        public static PropertyDrawer FindDrawer(this Type propertyType)
        {
            var cpdType = typeof(CustomPropertyDrawer);
            FieldInfo typeField = cpdType.GetField("m_Type", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo childField = cpdType.GetField("m_UseForChildren", BindingFlags.NonPublic | BindingFlags.Instance);

            // Optimization note:
            // For benchmark (on DungeonLooter 0.8.4)
            // - Original, search all assemblies and classes: 250 msec
            // - Wappen optimized, search only specific name assembly and classes: 5 msec
            
            foreach (Assembly assem in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Wappen optimization: filter only "*Editor" assembly
                if (!assem.FullName.Contains("Editor"))
                    continue;

                foreach (Type candidate in assem.GetTypes())
                {
                    // Wappen optimization: filter only "*Drawer" class name, like "SomeTypeDrawer"
                    if (!candidate.Name.Contains("Drawer"))
                        continue;

                    // See if this is a class that has [CustomPropertyDrawer( typeof( T ) )]
                    foreach (Attribute a in candidate.GetCustomAttributes(typeof(CustomPropertyDrawer)))
                    {
                        if (a.GetType().IsSubclassOf(typeof(CustomPropertyDrawer)) || a.GetType() == typeof(CustomPropertyDrawer))
                        {
                            CustomPropertyDrawer drawerAttribute = (CustomPropertyDrawer)a;
                            Type drawerType = (Type)typeField.GetValue(drawerAttribute);
                            if (drawerType == propertyType ||
                                ((bool)childField.GetValue(drawerAttribute) && (propertyType.IsSubclassOf(drawerType) || IsGenericSubclass(drawerType, propertyType))))
                            {
                                if (candidate.IsSubclassOf(typeof(PropertyDrawer)))
                                {
                                    // Technical note: PropertyDrawer.fieldInfo will not available via this drawer
                                    // It has to be manually setup by caller.
                                    var drawer = (PropertyDrawer)Activator.CreateInstance(candidate);
                                    return drawer;
                                }
                            }
                        }
                    }
                }
            }
            
            return null;
        }


        /// <summary>
        /// Returns true if the parent type is generic and the child type implements it.
        /// </summary>
        private static bool IsGenericSubclass(Type parent, Type child)
        {
            if (!parent.IsGenericType)
            {
                return false;
            }

            Type currentType = child;
            bool isAccessor = false;
            while (!isAccessor && currentType != null)
            {
                if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == parent.GetGenericTypeDefinition() &&
                    currentType.GetGenericArguments().Equals(parent.GetGenericArguments()))
                {
                    isAccessor = true;
                    break;
                }
                currentType = currentType.BaseType;
            }
            return isAccessor;
        }
    }
}
