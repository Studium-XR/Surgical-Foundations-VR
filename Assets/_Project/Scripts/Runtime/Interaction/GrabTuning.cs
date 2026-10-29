using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// Makes hand-held props easy to turn over. The prefabs snap to a fixed attach point and accept one hand, so an
    /// instrument jumped into a set pose and could only be turned by twisting the wrist. On every scene load, each
    /// grabbable instrument and prop gets:
    ///  • dynamic attach: it is held where it was picked up, in the pose it was in, and turns with the hand from there;
    ///  • two hands: grab with the second hand to turn it with both (the default grab transformer handles the rotation).
    /// Laparoscopic instruments in a port keep their setup: their pose comes from the port (fulcrum), not the hand.
    /// </summary>
    public static class GrabTuning
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var grab in root.GetComponentsInChildren<XRGrabInteractable>(true))
            {
                var n = grab.name;
                if (!n.StartsWith("INST_") && !n.StartsWith("PROP_") && !n.StartsWith("PPE_")) continue;
                // InstrumentInstaller turns shafted instruments into LapInstruments on this same event, possibly after us.
                if (grab.GetComponent<LapInstrument>() != null || HasChild(grab.transform, "Pivot_Port")) continue;

                grab.useDynamicAttach = true;
                grab.matchAttachPosition = true;
                grab.matchAttachRotation = true;
                grab.snapToColliderVolume = false;
                grab.selectMode = InteractableSelectMode.Multiple;
            }
        }

        static bool HasChild(Transform root, string childName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == childName) return true;
            return false;
        }
    }
}
