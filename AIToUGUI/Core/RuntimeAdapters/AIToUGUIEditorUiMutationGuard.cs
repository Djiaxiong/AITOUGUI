using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AIToUGUI
{
    public static class AIToUGUIEditorUiMutationGuard
    {
        public static bool IsUnsafeToMutateUi()
        {
#if UNITY_EDITOR
            return !Application.isPlaying &&
                   (CanvasUpdateRegistry.IsRebuildingLayout() || CanvasUpdateRegistry.IsRebuildingGraphics());
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        public static void QueueEditorCallback(ref bool queued, EditorApplication.CallbackFunction callback)
        {
            if (Application.isPlaying || callback == null || queued)
            {
                return;
            }

            queued = true;
            EditorApplication.delayCall += callback;
        }
#endif

        public static void DestroyComponentSafely(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(component);
                return;
            }

#if UNITY_EDITOR
            if (IsUnsafeToMutateUi())
            {
                var instanceId = component.GetInstanceID();
                EditorApplication.delayCall += () =>
                {
                    var delayedComponent = EditorUtility.InstanceIDToObject(instanceId) as Component;
                    if (delayedComponent != null)
                    {
                        Object.DestroyImmediate(delayedComponent);
                    }
                };
                return;
            }
#endif

            Object.DestroyImmediate(component);
        }

        public static void DestroyGameObjectSafely(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
                return;
            }

#if UNITY_EDITOR
            if (IsUnsafeToMutateUi())
            {
                var instanceId = gameObject.GetInstanceID();
                EditorApplication.delayCall += () =>
                {
                    var delayedObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                    if (delayedObject != null)
                    {
                        Object.DestroyImmediate(delayedObject);
                    }
                };
                return;
            }
#endif

            Object.DestroyImmediate(gameObject);
        }
    }
}
