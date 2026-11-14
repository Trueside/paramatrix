using UnityEditor;
using UnityEngine;

namespace Paramatrix.Unity.Editor
{
    /// <summary>Sets private serialized fields of components from editor code (scene builder, editor tests).</summary>
    public static class SerializedFields
    {
        public static void Set(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'.");
            }

            switch (value)
            {
                case string text: property.stringValue = text; break;
                case bool flag: property.boolValue = flag; break;
                case float number: property.floatValue = number; break;
                case int number: property.intValue = number; break; // also enum fields, by underlying value
                case Object reference: property.objectReferenceValue = reference; break;
                case null: property.objectReferenceValue = null; break;
                default: throw new System.ArgumentException($"Unsupported value for '{field}'.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
