using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SurgicalFoundations.EditorTools
{
    /// <summary>
    /// Makes animation clips inside .glb/.gltf files (the rigged SimReady models) preview in the Inspector.
    /// Unity only finds a preview model on its own for clips imported by its FBX importer; for glTF clips it shows
    /// "No model is available for preview". When such a clip is selected, this points the clip preview at the model
    /// the clip came from, so pressing Play in the preview shows the drawer opening, the knob turning, and so on.
    /// Uses Unity's internal preview-model setting (the same one dragging a model into the preview area sets).
    /// </summary>
    [InitializeOnLoad]
    static class GltfClipPreview
    {
        static readonly MethodInfo SetPreview;

        static GltfClipPreview()
        {
            var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.AvatarPreviewSelection");
            SetPreview = t?.GetMethod("SetPreview", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(ModelImporterAnimationType), typeof(GameObject) }, null);
            if (SetPreview == null)
            {
                Debug.LogWarning("[GltfClipPreview] This Unity version has no AvatarPreviewSelection.SetPreview; drag the model into the preview by hand.");
                return;
            }
            Selection.selectionChanged += OnSelection;
        }

        static void OnSelection()
        {
            if (!(Selection.activeObject is AnimationClip clip)) return;
            var path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path)) return;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".glb" && ext != ".gltf") return;
            var model = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            if (model == null) return;
            try
            {
                // glTF clips are Generic (they animate transforms by path from the model's root).
                SetPreview.Invoke(null, new object[] { ModelImporterAnimationType.Generic, model });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GltfClipPreview] " + e.Message);
            }
        }
    }
}
