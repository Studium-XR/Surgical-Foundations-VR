using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SurgicalFoundations.Interaction
{
    /// <summary>
    /// How things sit in the hand. On every scene load:
    ///  • instruments (INST_) go into their working grip: the prefab's "Attach" point on the handle, tip pointing
    ///    forward out of the fist (a trocar points down, palm on its head). Held wherever they were touched, a scalpel
    ///    could end up by the blade or sideways and had to be re-gripped before it was any use;
    ///  • props (PROP_, PPE_) have no working grip, so they are held where they were picked up, in the pose they were in;
    ///  • both accept a second hand, to turn them with two (the default grab transformer handles the rotation).
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

                var instrument = n.StartsWith("INST_") && grab.attachTransform != null;
                grab.useDynamicAttach = !instrument;
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
