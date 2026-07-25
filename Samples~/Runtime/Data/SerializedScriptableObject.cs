using System;
using System.Reflection;
using UnityEngine;

namespace TaoTie.Inspector
{
    /// <summary>
    /// Base class for ScriptableObjects that should always use the TaoTie Inspector drawing system,
    /// regardless of whether the type has TaoTie attributes on its fields.
    /// 
    /// This is the TaoTie equivalent of Odin's SerializedScriptableObject.
    /// Inheriting from this class forces enhanced drawing (Dictionary support, unified groups, etc.)
    /// without needing to add any attributes.
    /// </summary>
    public abstract class SerializedScriptableObject : ScriptableObject, IForceTaoTieDrawing
    {
        /// <summary>
        /// Override to handle logic after the inspector applies modifications.
        /// </summary>
        protected virtual void OnAfterDeserialize() { }

        /// <summary>Internal — called by TaoTieEditor after ApplyModifiedProperties.</summary>
        internal void NotifyAfterDeserialize() => OnAfterDeserialize();

        protected virtual void OnEnable()
        {
            InitializeNullFields();
        }

        /// <summary>
        /// Initialize null class fields — Unity ScriptableObject creation bypasses C# constructors.
        /// </summary>
        private void InitializeNullFields()
        {
            var type = GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType.IsValueType) continue;
                    if (field.FieldType.IsArray) continue;
                    if (field.FieldType.IsGenericType) continue;
                    if (field.FieldType.IsAbstract) continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;

                    try
                    {
                        if (field.GetValue(this) == null)
                            field.SetValue(this, Activator.CreateInstance(field.FieldType));
                    }
                    catch { }
                }
                currentType = currentType.BaseType;
            }
        }
    }

    /// <summary>
    /// Base class for MonoBehaviours that should always use the TaoTie Inspector drawing system.
    /// 
    /// This is the TaoTie equivalent of Odin's SerializedMonoBehaviour.
    /// </summary>
    public abstract class SerializedMonoBehaviour : MonoBehaviour, IForceTaoTieDrawing
    {
        /// <summary>
        /// Override to handle logic after the inspector applies modifications.
        /// </summary>
        protected virtual void OnAfterDeserialize() { }

        /// <summary>Internal — called by TaoTieEditor after ApplyModifiedProperties.</summary>
        internal void NotifyAfterDeserialize() => OnAfterDeserialize();

        protected virtual void OnEnable()
        {
            InitializeNullFields();
        }

        private void InitializeNullFields()
        {
            var type = GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType.IsValueType) continue;
                    if (field.FieldType.IsArray) continue;
                    if (field.FieldType.IsGenericType) continue;
                    if (field.FieldType.IsAbstract) continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;

                    try
                    {
                        if (field.GetValue(this) == null)
                            field.SetValue(this, Activator.CreateInstance(field.FieldType));
                    }
                    catch { }
                }
                currentType = currentType.BaseType;
            }
        }
    }

    /// <summary>
    /// Base class for StateMachineBehaviours that should always use the TaoTie Inspector drawing system.
    /// 
    /// This is the TaoTie equivalent of Odin's SerializedStateMachineBehaviour.
    /// </summary>
    public abstract class SerializedStateMachineBehaviour : UnityEngine.StateMachineBehaviour, IForceTaoTieDrawing
    {
        /// <summary>
        /// Override to handle logic after the inspector applies modifications.
        /// </summary>
        protected virtual void OnAfterDeserialize() { }

        /// <summary>Internal — called by TaoTieEditor after ApplyModifiedProperties.</summary>
        internal void NotifyAfterDeserialize() => OnAfterDeserialize();

        protected virtual void OnEnable()
        {
            InitializeNullFields();
        }

        private void InitializeNullFields()
        {
            var type = GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType.IsValueType) continue;
                    if (field.FieldType.IsArray) continue;
                    if (field.FieldType.IsGenericType) continue;
                    if (field.FieldType.IsAbstract) continue;
                    if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;

                    try
                    {
                        if (field.GetValue(this) == null)
                            field.SetValue(this, Activator.CreateInstance(field.FieldType));
                    }
                    catch { }
                }
                currentType = currentType.BaseType;
            }
        }
    }

    /// <summary>
    /// Marker interface for types that should always use enhanced TaoTie Inspector drawing.
    /// SerializedScriptableObject, SerializedMonoBehaviour, and SerializedStateMachineBehaviour
    /// all implement this. Any custom type can also implement it to force enhanced drawing.
    /// </summary>
    public interface IForceTaoTieDrawing { }
}
