#nullable enable

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace InteractiveWallpaper
{
    public sealed class VideoCharacterPerformance : MonoBehaviour
    {
        private const string PerformanceRoot = "CharacterVideo/Performances/SneezePickupRestore/";
        private const string LegacyRelativeVideoPath = "CharacterVideo/SneezeRgbMask.mp4";
        private const double SneezeImpactSeconds = 2.50;
        private const double PickupAttachSeconds = 3.20;
        private const double RestoreReleaseSeconds = 1.80;

        private enum PerformanceClip
        {
            None,
            Idle,
            Sneeze,
            Pickup,
            Restore,
            LegacySneeze,
        }

        [Serializable]
        private sealed class TrackPoint
        {
            public float x;
            public float y;
            public float confidence;
        }

        [Serializable]
        private sealed class TrackFrame
        {
            public float time = 0f;
            public TrackPoint? left = null;
            public TrackPoint? right = null;
        }

        [Serializable]
        private sealed class HandTrackFile
        {
            public float fps = 30f;
            public TrackFrame[] frames = Array.Empty<TrackFrame>();
        }

        private VideoPlayer? _player;
        private RenderTexture? _videoTexture;
        private Material? _stageMaterial;
        private Texture2D? _posterTexture;
        private GameObject? _stage;
        private Coroutine? _sequenceRoutine;
        private PerformanceClip _currentClip;
        private bool _prepared;
        private bool _clipEnded;
        private bool _playbackError;
        private bool _sequenceRunning;
        private bool _sneezeImpactTriggered;
        private bool _pickupAttachTriggered;
        private bool _restoreReleaseTriggered;
        private HandTrackFile? _pickupHandTrack;
        private HandTrackFile? _restoreHandTrack;

        public static VideoCharacterPerformance? Instance { get; private set; }
        public string ActionStatus { get; private set; } = "正在准备真人视频…";
        public bool IsPlaying => _player?.isPlaying == true;

        public static string VideoPath => Path.Combine(Application.streamingAssetsPath, LegacyRelativeVideoPath);
        public static bool HasPerformanceSet =>
            File.Exists(GetPerformancePath("01-idle-loop.mp4")) &&
            File.Exists(GetPerformancePath("02-sneeze.mp4")) &&
            File.Exists(GetPerformancePath("03-pickup.mp4")) &&
            File.Exists(GetPerformancePath("04-restore.mp4"));
        public static bool IsVideoAvailable => HasPerformanceSet || File.Exists(VideoPath);

        private static string GetPerformancePath(string fileName)
        {
            return Path.Combine(Application.streamingAssetsPath, PerformanceRoot, fileName);
        }

        private void Awake()
        {
            Instance = this;
        }

        private IEnumerator Start()
        {
            if (!IsVideoAvailable)
            {
                ActionStatus = "真人视频素材不存在";
                Debug.LogWarning($"Video character assets are missing: {VideoPath}");
                yield break;
            }

            // Create the stage and load a still poster immediately. The first
            // video prepare and the expensive Shell snapshot run in parallel.
            CreateStage();
            CreateVideoPlayer();
            if (HasPerformanceSet)
            {
                _pickupHandTrack = LoadHandTrack("03-pickup.handtrack.json");
                _restoreHandTrack = LoadHandTrack("04-restore.handtrack.json");
            }

            if (HasPerformanceSet)
            {
                yield return PrepareAndPlay(
                    GetPerformancePath("01-idle-loop.mp4"),
                    PerformanceClip.Idle,
                    true);
                if (_playbackError)
                {
                    yield break;
                }

                // Do not trigger physics until the desktop snapshot and icon
                // proxy world are ready, but keep the character visible.
                while (ProxyIconWorld.Instance == null ||
                       InteractiveWallpaperBootstrap.Instance?.NativeBridgeReady != true)
                {
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.35f);
                Replay();
            }
            else
            {
                yield return PrepareAndPlay(VideoPath, PerformanceClip.LegacySneeze, false);
                if (!_playbackError)
                {
                    ActionStatus = "真人表演：准备打喷嚏";
                }
            }
        }

        public void Replay()
        {
            if (_player == null || _playbackError)
            {
                return;
            }

            if (_sequenceRunning && _sequenceRoutine != null)
            {
                StopCoroutine(_sequenceRoutine);
                _sequenceRoutine = null;
                _sequenceRunning = false;
                ProxyIconWorld.Instance?.ResetAll();
            }

            if (HasPerformanceSet)
            {
                _sequenceRoutine = StartCoroutine(RunPerformanceSequence());
            }
            else
            {
                _sequenceRoutine = StartCoroutine(RunLegacySneeze());
            }
        }

        private IEnumerator RunPerformanceSequence()
        {
            _sequenceRunning = true;
            _sneezeImpactTriggered = false;
            _pickupAttachTriggered = false;
            _restoreReleaseTriggered = false;

            yield return PrepareAndPlay(
                GetPerformancePath("02-sneeze.mp4"),
                PerformanceClip.Sneeze,
                false);
            yield return WaitForClipFinish();
            if (_playbackError)
            {
                FinishSequence();
                yield break;
            }

            yield return PrepareAndPlay(
                GetPerformancePath("03-pickup.mp4"),
                PerformanceClip.Pickup,
                false);
            yield return WaitForClipFinish();
            if (_playbackError)
            {
                FinishSequence();
                yield break;
            }

            yield return PrepareAndPlay(
                GetPerformancePath("04-restore.mp4"),
                PerformanceClip.Restore,
                false);
            yield return WaitForClipFinish();
            if (_playbackError)
            {
                FinishSequence();
                yield break;
            }

            yield return PrepareAndPlay(
                GetPerformancePath("01-idle-loop.mp4"),
                PerformanceClip.Idle,
                true);
            ActionStatus = "真人待机：动作闭环完成";
            FinishSequence();
        }

        private IEnumerator RunLegacySneeze()
        {
            _sequenceRunning = true;
            _sneezeImpactTriggered = false;
            yield return PrepareAndPlay(VideoPath, PerformanceClip.LegacySneeze, false);
            yield return WaitForClipFinish();
            FinishSequence();
        }

        private IEnumerator PrepareAndPlay(string path, PerformanceClip clip, bool loop)
        {
            if (_player == null)
            {
                yield break;
            }

            _currentClip = clip;
            _prepared = false;
            _clipEnded = false;
            _playbackError = false;
            LoadPosterTexture(PosterNameFor(clip));
            if (_stageMaterial != null)
            {
                _stageMaterial.SetTexture("_MainTex", _posterTexture != null ? _posterTexture : _videoTexture);
            }
            _player.Stop();
            _player.isLooping = loop;
            _player.url = path;
            _player.Prepare();

            while (!_prepared && !_playbackError)
            {
                yield return null;
            }
            if (_playbackError)
            {
                yield break;
            }

            _player.time = 0.0;
            if (_stageMaterial != null)
            {
                _stageMaterial.SetTexture("_MainTex", _videoTexture);
            }
            _player.Play();
            ActionStatus = clip switch
            {
                PerformanceClip.Idle => "真人待机：自然活动",
                PerformanceClip.Sneeze or PerformanceClip.LegacySneeze => "真人表演：准备打喷嚏",
                PerformanceClip.Pickup => "真人表演：弯腰收拢图标",
                PerformanceClip.Restore => "真人表演：释放并恢复图标",
                _ => "真人视频播放中",
            };
            Debug.Log($"Video character clip started: {clip}; path={path}");
        }

        private IEnumerator WaitForClipFinish()
        {
            while (!_clipEnded && !_playbackError)
            {
                yield return null;
            }
        }

        private void Update()
        {
            UpdateStageScale();
            if (_player == null || !_player.isPlaying)
            {
                return;
            }

            switch (_currentClip)
            {
                case PerformanceClip.Sneeze:
                case PerformanceClip.LegacySneeze:
                    if (!_sneezeImpactTriggered && _player.time >= SneezeImpactSeconds)
                    {
                        _sneezeImpactTriggered = true;
                        ActionStatus = "真人表演：喷嚏冲击";
                        TriggerSneezeImpact();
                        Debug.Log($"Video character event: sneeze impact at clipTime={_player.time:F2}");
                    }
                    else if (_sneezeImpactTriggered && _player.time >= 2.2)
                    {
                        ActionStatus = "真人表演：观察图标";
                    }
                    break;

                case PerformanceClip.Pickup:
                    UpdatePickupPerformance();
                    break;

                case PerformanceClip.Restore:
                    UpdateRestorePerformance();
                    break;
            }
        }

        private void UpdatePickupPerformance()
        {
            var world = ProxyIconWorld.Instance;
            if (world == null || _player == null)
            {
                return;
            }

            var normalizedTime = Mathf.Clamp01((float)((_player.time - 1.15) / 2.4));
            var fallbackAnchor = NormalizedToWorld(
                world,
                Mathf.Lerp(0.43f, 0.51f, normalizedTime),
                Mathf.Lerp(0.76f, 0.63f, normalizedTime));
            var anchor = HandTrackAnchor(_pickupHandTrack, _player.time, fallbackAnchor);
            if (!_pickupAttachTriggered && _player.time >= PickupAttachSeconds)
            {
                _pickupAttachTriggered = true;
                world.BeginCharacterPickup(anchor);
                Debug.Log($"Video character event: pickup attach at clipTime={_player.time:F2}");
            }
            if (_pickupAttachTriggered)
            {
                world.UpdateCharacterPickup(anchor);
            }
        }

        private void UpdateRestorePerformance()
        {
            var world = ProxyIconWorld.Instance;
            if (world == null || _player == null)
            {
                return;
            }

            var normalizedTime = Mathf.Clamp01((float)((_player.time - 0.4) / 1.7));
            var fallbackAnchor = NormalizedToWorld(
                world,
                Mathf.Lerp(0.51f, 0.58f, normalizedTime),
                Mathf.Lerp(0.63f, 0.47f, normalizedTime));
            var anchor = HandTrackAnchor(_restoreHandTrack, _player.time, fallbackAnchor);
            if (!_restoreReleaseTriggered && _player.time >= RestoreReleaseSeconds)
            {
                _restoreReleaseTriggered = true;
                world.ReleaseCharacterPickupAndRestore();
                Debug.Log($"Video character event: restore release at clipTime={_player.time:F2}");
            }
            if (!_restoreReleaseTriggered)
            {
                world.UpdateCharacterPickup(anchor);
            }
        }

        private void FinishSequence()
        {
            _sequenceRunning = false;
            _sequenceRoutine = null;
        }

        private HandTrackFile? LoadHandTrack(string fileName)
        {
            var path = GetPerformancePath(fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Hand track asset is missing: {path}");
                return null;
            }
            try
            {
                var track = JsonUtility.FromJson<HandTrackFile>(File.ReadAllText(path));
                if (track == null || track.frames == null || track.frames.Length == 0)
                {
                    throw new InvalidDataException("hand track frames are empty");
                }
                Debug.Log($"Hand track loaded: {fileName}; frames={track.frames.Length}; fps={track.fps:F1}");
                return track;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load hand track {fileName}: {exception.Message}");
                return null;
            }
        }

        private static Vector3 HandTrackAnchor(HandTrackFile? track, double time, Vector3 fallback)
        {
            if (track?.frames == null || track.frames.Length == 0)
            {
                return fallback;
            }

            var frames = track.frames;
            var position = Mathf.Clamp((float)time * Mathf.Max(1f, track.fps), 0f, frames.Length - 1.001f);
            var lower = Mathf.Clamp(Mathf.FloorToInt(position), 0, frames.Length - 1);
            var upper = Mathf.Min(lower + 1, frames.Length - 1);
            var blend = position - lower;
            var left = InterpolateTrackPoint(frames[lower].left, frames[upper].left, blend);
            var right = InterpolateTrackPoint(frames[lower].right, frames[upper].right, blend);
            if (left == null && right == null)
            {
                return fallback;
            }
            var x = left != null && right != null
                ? (left.x + right.x) * 0.5f
                : (left ?? right)!.x;
            var y = left != null && right != null
                ? (left.y + right.y) * 0.5f
                : (left ?? right)!.y;
            var world = ProxyIconWorld.Instance;
            return world == null ? fallback : NormalizedToWorld(world, x, y);
        }

        private static TrackPoint? InterpolateTrackPoint(TrackPoint? lower, TrackPoint? upper, float blend)
        {
            if (lower == null) return upper;
            if (upper == null) return lower;
            return new TrackPoint
            {
                x = Mathf.Lerp(lower.x, upper.x, blend),
                y = Mathf.Lerp(lower.y, upper.y, blend),
                confidence = Mathf.Lerp(lower.confidence, upper.confidence, blend),
            };
        }

        private void CreateStage()
        {
            var shader = Resources.Load<Shader>("Shaders/SideBySideVideoAlpha");
            if (shader == null)
            {
                throw new InvalidOperationException("Side-by-side video alpha shader is missing.");
            }

            _videoTexture = new RenderTexture(2560, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "Video Character RGB Alpha",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _videoTexture.Create();

            _stage = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _stage.name = "Full Screen Video Character";
            Destroy(_stage.GetComponent<Collider>());
            _stage.transform.SetParent(transform, false);

            _stage.transform.position = new Vector3(0f, 0f, -0.42f);
            UpdateStageScale();

            _stageMaterial = new Material(shader)
            {
                name = "Video Character RGB Alpha Material",
                renderQueue = 3100,
            };
            LoadPosterTexture("01-idle-loop-poster.png");
            _stageMaterial.SetTexture("_MainTex", _posterTexture != null ? _posterTexture : _videoTexture);
            var renderer = _stage.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _stageMaterial;
            renderer.sortingOrder = 1000;
        }

        private void UpdateStageScale()
        {
            if (_stage == null)
            {
                return;
            }
            var world = ProxyIconWorld.Instance;
            var width = world != null ? world.WorldWidth : 16f;
            var height = world != null ? world.WorldHeight : 10f;
            _stage.transform.localScale = new Vector3(width, height, 1f);
        }

        private void LoadPosterTexture(string fileName)
        {
            var path = GetPerformancePath(fileName);
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                _posterTexture ??= new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _posterTexture.LoadImage(bytes, markNonReadable: true);
                _posterTexture.name = "Video Character Poster";
                _posterTexture.filterMode = FilterMode.Bilinear;
                _posterTexture.wrapMode = TextureWrapMode.Clamp;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load video poster {fileName}: {exception.Message}");
            }
        }

        private static string PosterNameFor(PerformanceClip clip)
        {
            return clip switch
            {
                PerformanceClip.Sneeze => "02-sneeze-poster.png",
                PerformanceClip.Pickup => "03-pickup-poster.png",
                PerformanceClip.Restore => "04-restore-poster.png",
                _ => "01-idle-loop-poster.png",
            };
        }

        private void CreateVideoPlayer()
        {
            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _videoTexture;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.prepareCompleted += OnPrepared;
            _player.loopPointReached += OnFinished;
            _player.errorReceived += OnVideoError;
        }

        private void OnPrepared(VideoPlayer source)
        {
            _prepared = true;
            ActionStatus = "真人视频已就绪";
            Debug.Log($"Video character prepared: {source.width}x{source.height}, {source.frameRate:F1} fps");
        }

        private void OnFinished(VideoPlayer source)
        {
            if (_currentClip == PerformanceClip.Idle && source.isLooping)
            {
                ActionStatus = "真人待机：自然活动";
                return;
            }

            _clipEnded = true;
            source.Pause();
            if (!_sequenceRunning)
            {
                ActionStatus = "真人表演：等待再次触发";
            }
            Debug.Log($"Video character clip completed: {_currentClip}");
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            _playbackError = true;
            ActionStatus = "真人视频播放失败";
            Debug.LogError($"Video character playback failed: {message}");
        }

        private static Vector3 NormalizedToWorld(ProxyIconWorld world, float x, float y)
        {
            return new Vector3(
                (x - 0.5f) * world.WorldWidth,
                (0.5f - y) * world.WorldHeight,
                -0.56f);
        }

        private static void TriggerSneezeImpact()
        {
            var world = ProxyIconWorld.Instance;
            if (world == null)
            {
                return;
            }

            var origin = NormalizedToWorld(world, 0.54f, 0.30f);
            world.SneezeBurst(origin);
        }

        private void OnDestroy()
        {
            if (_sequenceRoutine != null)
            {
                StopCoroutine(_sequenceRoutine);
                _sequenceRoutine = null;
            }
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepared;
                _player.loopPointReached -= OnFinished;
                _player.errorReceived -= OnVideoError;
            }
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }
            if (_posterTexture != null)
            {
                Destroy(_posterTexture);
            }
            if (_stage != null)
            {
                Destroy(_stage);
            }
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
