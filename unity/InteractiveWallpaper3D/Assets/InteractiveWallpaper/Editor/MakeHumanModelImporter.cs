#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InteractiveWallpaper.Editor
{
    public sealed class MakeHumanModelImporter : AssetPostprocessor
    {
        private const string CharacterRoot = "Assets/Resources/Characters/MakeHumanDefault/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importBlendShapes = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("_normal", StringComparison.OrdinalIgnoreCase))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var animator = model.GetComponentInChildren<Animator>();
            if (animator?.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                Debug.LogError($"MakeHuman humanoid avatar is invalid: {assetPath}", model);
                return;
            }

            Debug.Log($"MakeHuman humanoid avatar imported: {assetPath}", model);
        }
    }
}
