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
    }

    /// <summary>
    /// Marker interface for types that should always use enhanced TaoTie Inspector drawing.
    /// SerializedScriptableObject, SerializedMonoBehaviour, and SerializedStateMachineBehaviour
    /// all implement this. Any custom type can also implement it to force enhanced drawing.
    /// </summary>
    public interface IForceTaoTieDrawing { }
}
