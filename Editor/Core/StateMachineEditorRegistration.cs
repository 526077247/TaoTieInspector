using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    /// <summary>
    /// Forces the TaoTie editor to be used for every StateMachineBehaviour subtype, including inside
    /// the Animator window. Unity's Animator window ignores the compile-time
    /// [CustomEditor(typeof(object), true)] fallback for StateMachineBehaviour, but it DOES consult the
    /// runtime-injected MonoEditorType records written by <see cref="CustomEditorUtility"/>.
    ///
    /// Registering only from [DidReloadScripts] is not enough on a cold project open: Unity rebuilds its
    /// internal custom-editor table lazily at the end of domain initialisation, which can run AFTER the
    /// [DidReloadScripts] pass and wipe our injected records. Re-running the (idempotent) injection from
    /// EditorApplication.delayCall - once Unity has finished initialising - makes it stick on both a cold
    /// open and a regular recompile, matching how Odin re-applies its editors.
    /// </summary>
    internal static class StateMachineEditorRegistration
    {
        [InitializeOnLoadMethod]
        private static void ScheduleDelayedInjection()
        {
            EditorApplication.delayCall += InjectDelayed;
        }

        [DidReloadScripts]
        private static void InjectOnReload()
        {
            Inject();
        }

        private static void InjectDelayed()
        {
            EditorApplication.delayCall -= InjectDelayed;
            Inject();
        }

        private static void Inject()
        {
            // No once-guard here: on a cold open the [DidReloadScripts] pass can be wiped by Unity's
            // subsequent editor-table rebuild, so the delayCall pass must re-run this. ResetCustomEditors
            // makes re-injection idempotent, so running it from both passes is safe.
            var smbType = typeof(UnityEngine.StateMachineBehaviour);
            if (smbType == null) return;

            try
            {
                var types = TypeCache.GetTypesDerivedFrom(smbType);

                // Rebuild Unity's editor table from compile-time [CustomEditor] first, then inject ours
                // at the front - same as Odin - so the injected StateMachineBehaviour editors survive
                // Unity's lazy Rebuild and stay deterministic across script reloads.
                CustomEditorUtility.ResetCustomEditors();

                for (int i = 0; i < types.Count; i++)
                {
                    Type t = types[i];
                    if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                    CustomEditorUtility.RegisterCustomEditor(t, typeof(TaoTieEditor), false, false);
                }
                if (CustomEditorUtility.IsValid)
                {
                    Debug.Log($"[TaoTie.Inspector] Registered TaoTieEditor for {types.Count} StateMachineBehaviour subtype(s).");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TaoTie.Inspector] Failed to register StateMachineBehaviour editors: " + e);
            }
        }
    }
}
