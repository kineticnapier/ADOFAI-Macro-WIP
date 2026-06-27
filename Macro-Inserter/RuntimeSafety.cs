using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Macro_Inserter;

internal static class RuntimeSafety
{
    public static bool IsAllowedPlaybackState()
    {
        object? editor = ReflectionCache.GetSingletonInstance("scnEditor");
        if (editor != null &&
            ReflectionCache.TryReadBool(editor, out bool editorPlayMode, "playMode") &&
            editorPlayMode)
        {
            return true;
        }

        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            return false;
        }

        if (controller is Behaviour behaviour)
        {
            return behaviour.isActiveAndEnabled;
        }

        if (controller is Component component)
        {
            return component.gameObject.activeInHierarchy;
        }

        return true;
    }

    public static bool IsPaused()
    {
        if (Time.timeScale <= 0.0f || AudioListener.pause)
        {
            return true;
        }

        foreach (string typeName in new[] { "scrController", "scrConductor", "scnEditor" })
        {
            object? instance = ReflectionCache.GetSingletonInstance(typeName);
            if (instance == null)
            {
                continue;
            }

            object? raw = ReflectionCache.ReadMember(
                instance,
                "paused",
                "isPaused",
                "isGamePaused",
                "pause");

            if (raw is bool paused && paused)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsUiBlockingStart()
    {
        if (IsTextInputFocused())
        {
            return true;
        }

        return IsUnityModManagerUiOpen();
    }

    private static bool IsTextInputFocused()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
        {
            return false;
        }

        GameObject selected = eventSystem.currentSelectedGameObject;
        if (selected.GetComponent<InputField>() != null)
        {
            return true;
        }

        return selected
            .GetComponents<Component>()
            .Any(component => component != null && component.GetType().Name.Contains("TMP_InputField"));
    }

    private static bool IsUnityModManagerUiOpen()
    {
        Type? unityModManager = ReflectionCache.FindType("UnityModManagerNet.UnityModManager");
        if (unityModManager == null)
        {
            return false;
        }

        object? ui = ReflectionCache.ReadMember(unityModManager, instance: null, names: new[] { "UI", "ui" });
        if (ui == null)
        {
            return false;
        }

        object? raw = ReflectionCache.ReadMember(ui, "Opened", "IsOpen", "IsOpened", "visible", "show");
        return raw is bool opened && opened;
    }
}
