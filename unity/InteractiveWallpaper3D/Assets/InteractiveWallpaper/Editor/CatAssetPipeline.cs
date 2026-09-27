#nullable enable

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace InteractiveWallpaper.Editor
{
    public static class CatAssetPipeline
    {
        private const string CatRoot = "Assets/Resources/Characters/Cat";
        private const string MaterialRoot = CatRoot + "/Materials";
        private const string TextureRoot = CatRoot + "/Textures";
        private static readonly string[] Variants = { "CatTuxedo", "CatOrangeTabby", "CatSilverTabby" };

        [MenuItem("交互式壁纸/Build Cat Models")]
        public static void BuildCatModels()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            EnsureAssetFolders();
            EnsureTextureImporters();
            foreach (var variant in Variants)
            {
                ConfigureModelImporter(variant);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var materials = CreateUnityMaterials(variant);
                BuildVariant(variant, materials);
            }

            var sourcePrefab = $"{CatRoot}/CatTuxedo.prefab";
            var fallbackPrefab = $"{CatRoot}/SneakyCat.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefab) != null)
            {
                UpdateFallbackPrefab(sourcePrefab, fallbackPrefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("Cat asset pipeline completed: textured randomized cat prefabs and SneakyCat fallback are ready.");
        }

        [MenuItem("交互式壁纸/Validate Cat Models")]
        public static void ValidateCatModels()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var variant in Variants)
            {
                var modelPath = $"{CatRoot}/{variant}.fbx";
                var importedClips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                    .Select(clip => clip.name + " [" + clip.length.ToString("F2") + "s]");
                Debug.Log($"Cat imported clips {variant}: {string.Join(", ", importedClips)}");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{CatRoot}/{variant}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"Cat prefab missing: {variant}");
                    continue;
                }

                var instance = UnityEngine.Object.Instantiate(prefab);
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError($"Cat prefab has no renderers: {variant}");
                }
                else
                {
                    var bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                    var rootTransform = instance.transform;
                    foreach (var bone in instance.GetComponentsInChildren<Transform>(true))
                    {
                        if (bone.name.IndexOf("head_05", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            bone.name.IndexOf("pelvis_01", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            bone.name.IndexOf("neck_04", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Debug.Log($"Cat bone {variant}/{bone.name}: local={rootTransform.InverseTransformPoint(bone.position)}, world={bone.position}");
                        }
                    }
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Debug.Log($"Cat skinned renderer {variant}/{renderer.name}: localBounds={renderer.localBounds}, material={string.Join(",", renderer.sharedMaterials.Select(material => material != null ? material.name : "null"))}");
                    }
                    Debug.Log($"Cat validation {variant}: bounds={bounds.size}, center={bounds.center}, renderers={renderers.Length}, animator={instance.GetComponentInChildren<Animator>() != null}, lod={instance.GetComponent<LODGroup>() != null}");
                }
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
        private static void UpdateFallbackPrefab(string sourcePrefab, string fallbackPrefab)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefab);
            if (source == null)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPrefab) == null)
            {
                AssetDatabase.CopyAsset(sourcePrefab, fallbackPrefab);
                return;
            }

            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = "SneakyCat";
            PrefabUtility.SaveAsPrefabAsset(instance, fallbackPrefab);
            UnityEngine.Object.DestroyImmediate(instance);
        }
        private static void EnsureAssetFolders()
        {
            if (!AssetDatabase.IsValidFolder(MaterialRoot))
            {
                AssetDatabase.CreateFolder(CatRoot, "Materials");
            }
        }
        private static void EnsureTextureImporters()
        {
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureRoot })
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                {
                    continue;
                }

                var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                var isNormal = fileName.EndsWith("_Normal", StringComparison.Ordinal);
                var isData = isNormal || fileName.EndsWith("_Roughness", StringComparison.Ordinal) || fileName.EndsWith("_Smoothness", StringComparison.Ordinal);
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !isData;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureModelImporter(string variant)
        {
            var path = $"{CatRoot}/{variant}.fbx";
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                throw new InvalidOperationException($"Cat model not imported: {path}");
            }

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.isReadable = false;
            // Use defaults from the current FBX instead of stale custom clips retained in
            // the ModelImporter meta from the previous rig; otherwise new takes become __preview__ clips only.
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                if (clip.name.IndexOf("CatSneakWalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("CatRunAway", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    clip.loopTime = true;
                    clip.loopPose = true;
                }
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static CatMaterials CreateUnityMaterials(string variant)
        {
            var prefix = variant switch
            {
                "CatTuxedo" => "Cat Tuxedo",
                "CatOrangeTabby" => "Cat Orange Tabby",
                "CatSilverTabby" => "Cat Silver Tabby",
                _ => variant,
            };

            var body = LoadTexture($"{variant}_Fur_BaseColor.png");
            var accent = LoadTexture($"{variant}_Accent_BaseColor.png");
            var normal = LoadTexture($"{variant}_Fur_Normal.png");
            var smoothness = LoadTexture($"{variant}_Fur_Smoothness.png");
            var furCardTexture = LoadTexture($"{variant}_FurCard_BaseColor.png");
            var fur = CreateMaterial($"{variant}_Fur", body, normal, smoothness, 0.36f);
            var chest = CreateMaterial($"{variant}_Chest", accent, normal, smoothness, 0.42f);
            var paws = CreateMaterial($"{variant}_Paws", accent, normal, smoothness, 0.42f);
            var furCard = CreateFurCardMaterial(variant, furCardTexture);
            return new CatMaterials(prefix, fur, chest, paws, furCard);
        }

        private static Texture2D LoadTexture(string fileName)
        {
            var path = $"{TextureRoot}/{fileName}";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException($"Cat texture not found: {path}");
            }
            return texture;
        }

        private static Material CreateMaterial(string name, Texture2D baseMap, Texture2D normalMap, Texture2D smoothnessMap, float smoothness)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException("No Unity Lit shader is available for cat materials.");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baseMap);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseMap);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normalMap);
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0.32f);
            if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", smoothnessMap);
            if (material.HasProperty("_SmoothnessTextureChannel")) material.SetFloat("_SmoothnessTextureChannel", 0f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.name = name;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateFurCardMaterial(string variant, Texture2D texture)
        {
            var path = $"{MaterialRoot}/{variant}_FurCard.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null)
                {
                    throw new InvalidOperationException("No Unity Lit shader is available for fur cards.");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.18f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.32f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = 2450;
            material.name = $"{variant}_FurCard";
            EditorUtility.SetDirty(material);
            return material;
        }
        private static void BuildVariant(string variant, CatMaterials materials)
        {
            var modelPath = $"{CatRoot}/{variant}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException($"Unable to load cat model: {modelPath}");
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .ToArray();
            var controllerPath = $"{CatRoot}/{variant}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            ConfigureController(controller, clips);

            var prefabPath = $"{CatRoot}/{variant}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);

            // Blender FBX export already converts the imported GLB to Unity Y-up.
            // Keep a neutral visual wrapper so desktop XY movement/yaw leaves rig axes intact.
            var instance = new GameObject(variant);
            var visual = UnityEngine.Object.Instantiate(model, instance.transform, false);
            visual.name = "CatRiggedVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            ApplyMaterials(visual, materials);
            ConfigureLodGroup(instance);
            var animator = instance.GetComponentInChildren<Animator>() ?? instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
        }

        private static void ConfigureLodGroup(GameObject root)
        {
            var existing = root.GetComponent<LODGroup>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }
            var group = root.AddComponent<LODGroup>();
            if (group == null)
            {
                throw new InvalidOperationException($"Unable to add LODGroup to {root.name}");
            }
            var allRenderers = root.GetComponentsInChildren<Renderer>(true);
            var withoutFurCards = allRenderers
                .Where(renderer => !IsFurCardRenderer(renderer))
                .ToArray();
            var mainRenderers = withoutFurCards
                .Where(renderer => !renderer.gameObject.name.StartsWith("Cat_Detail_", StringComparison.Ordinal))
                .ToArray();
            if (mainRenderers.Length == 0)
            {
                mainRenderers = withoutFurCards;
            }

            group.SetLODs(new[]
            {
                new LOD(0.55f, allRenderers),
                new LOD(0.25f, withoutFurCards),
                new LOD(0.08f, mainRenderers),
            });
            group.RecalculateBounds();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
        }

        private static bool IsFurCardRenderer(Renderer renderer)
        {
            if (renderer.gameObject.name.IndexOf("FurCard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return renderer.sharedMaterials.Any(material =>
                material != null && material.name.IndexOf("FurCard", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private static void ApplyMaterials(GameObject root, CatMaterials materials)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (var i = 0; i < slots.Length; i++)
                {
                    var sourceName = slots[i] != null ? slots[i].name : renderer.gameObject.name;
                    if (sourceName.IndexOf("FurCard", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        slots[i] = materials.FurCard;
                    }
                    else if (sourceName.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             sourceName.IndexOf("Muzzle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        slots[i] = materials.Chest;
                    }
                    else if (sourceName.IndexOf("Paws", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             sourceName.IndexOf("Paw", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        slots[i] = materials.Paws;
                    }
                    else
                    {
                        // The supplied rig is a single UV-mapped SkinnedMeshRenderer whose material
                        // is named Scene_-_Root; route it explicitly to the chosen coat texture.
                        slots[i] = materials.Fur;
                    }
                }
                renderer.sharedMaterials = slots;
            }
        }

        private sealed class CatMaterials
        {
            public CatMaterials(string sourcePrefix, Material fur, Material chest, Material paws, Material furCard)
            {
                SourcePrefix = sourcePrefix;
                Fur = fur;
                Chest = chest;
                Paws = paws;
                FurCard = furCard;
            }

            public string SourcePrefix { get; }
            public Material Fur { get; }
            public Material Chest { get; }
            public Material Paws { get; }
            public Material FurCard { get; }
        }

        private static void ConfigureController(AnimatorController controller, AnimationClip[] clips)
        {
            foreach (var trigger in new[] { "Peek", "Sneak", "Reach", "Grab", "Startled", "RunAway" })
            {
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            }

            var stateMachine = controller.layers[0].stateMachine;
            var idle = AddState(stateMachine, "Idle", FindClip(clips, "CatIdleCrouch"));
            stateMachine.defaultState = idle;
            var peek = AddState(stateMachine, "Peek", FindClip(clips, "CatPeek"));
            var sneak = AddState(stateMachine, "Sneak", FindClip(clips, "CatSneakWalk"));
            var reach = AddState(stateMachine, "Reach", FindClip(clips, "CatReach"));
            var grab = AddState(stateMachine, "Grab", FindClip(clips, "CatSneakWalk"));
            var startled = AddState(stateMachine, "Startled", FindClip(clips, "CatStartled"));
            var runAway = AddState(stateMachine, "RunAway", FindClip(clips, "CatRunAway"));
            peek.speed = 0.8f;
            sneak.speed = 0.58f;
            reach.speed = 0.45f;
            grab.speed = 0.58f;
            startled.speed = 1.05f;
            runAway.speed = 1.12f;

            AddAnyStateTransition(stateMachine, peek, "Peek");
            AddAnyStateTransition(stateMachine, sneak, "Sneak");
            AddAnyStateTransition(stateMachine, reach, "Reach");
            AddAnyStateTransition(stateMachine, grab, "Grab");
            AddAnyStateTransition(stateMachine, startled, "Startled");
            AddAnyStateTransition(stateMachine, runAway, "RunAway");
            AddExitTransition(peek, idle);
            AddExitTransition(reach, idle);
            AddExitTransition(startled, idle);
        }

        private static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.AddState(name);
            state.motion = clip;
            return state;
        }

        private static AnimationClip FindClip(AnimationClip[] clips, string name)
        {
            var clip = clips.FirstOrDefault(c => string.Equals(c.name, name, StringComparison.Ordinal))
                ?? clips.FirstOrDefault(c => c.name.Contains(name, StringComparison.Ordinal));
            if (clip == null)
            {
                throw new InvalidOperationException($"Animation clip not found: {name}; available: {string.Join(", ", clips.Select(c => c.name))}");
            }
            return clip;
        }

        private static void AddAnyStateTransition(AnimatorStateMachine machine, AnimatorState destination, string trigger)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = 0.08f;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddExitTransition(AnimatorState source, AnimatorState destination)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 0.92f;
            transition.duration = 0.08f;
        }
    }
}




























