// ============================================================
//  SwingReplayVisualizer.cs
//
//  Drop on any GameObject. Set swingName. Hit Play.
//  Skeleton builds itself, loops the animation, cleans up on destroy.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BadmintonPoseTracking
{
    public sealed class SwingReplayVisualizer : MonoBehaviour
    {
        [Header("Display")]
        [Tooltip("Human-readable label for this skeleton — shown in UI or passed to events.\n" +
                 "Set automatically by SwingCoordinator; override here for standalone use.")]
        public string displayName = string.Empty;

        [Header("Swing")]
        public string swingName = "smash";

        [Tooltip("If true, loads swingName from disk and plays on Start().\n" +
                 "Set to false when you plan to call PlayCapture() from code.")]
        public bool autoPlayOnStart = true;

        [Header("Playback")]
        [Range(0.1f, 3f)]
        public float playbackSpeed = 1f;

        [Tooltip("Loop playback.  If false, plays once then stops.")]
        public bool loop = true;

        [Header("Visuals")]
        [Range(0.01f, 0.1f)]  public float jointRadius = 0.03f;
        [Range(0.005f, 0.05f)] public float boneRadius = 0.012f;
        public Color jointColor = new Color(0.2f, 0.8f, 1f, 0.85f);
        public Color boneColor  = new Color(0.2f, 0.6f, 1f, 0.4f);

        private static readonly (TrackedJoint A, TrackedJoint B)[] Connections =
        {
            (TrackedJoint.Hips,          TrackedJoint.SpineLower),
            (TrackedJoint.SpineLower,    TrackedJoint.SpineMiddle),
            (TrackedJoint.SpineMiddle,   TrackedJoint.SpineUpper),
            (TrackedJoint.SpineUpper,    TrackedJoint.Chest),
            (TrackedJoint.Chest,         TrackedJoint.Neck),
            (TrackedJoint.Neck,          TrackedJoint.Head),
            (TrackedJoint.Chest,         TrackedJoint.LeftShoulder),
            (TrackedJoint.LeftShoulder,  TrackedJoint.LeftUpperArm),
            (TrackedJoint.LeftUpperArm,  TrackedJoint.LeftForearm),
            (TrackedJoint.LeftForearm,   TrackedJoint.LeftWrist),
            (TrackedJoint.Chest,         TrackedJoint.RightShoulder),
            (TrackedJoint.RightShoulder, TrackedJoint.RightUpperArm),
            (TrackedJoint.RightUpperArm, TrackedJoint.RightForearm),
            (TrackedJoint.RightForearm,  TrackedJoint.RightWrist),
        };

        private PoseFrame[] _frames;
        private float       _captureRateFps;

        private readonly Dictionary<TrackedJoint, Transform> _joints
            = new Dictionary<TrackedJoint, Transform>();
        private readonly List<(TrackedJoint A, TrackedJoint B, Transform t)> _bones
            = new List<(TrackedJoint, TrackedJoint, Transform)>();

        private Material _jointMat;
        private Material _boneMat;
        private Coroutine _playCoroutine;
        private bool      _skeletonBuilt;

        private void Start()
        {
            CreateMaterials();
            BuildSkeleton();

            if (autoPlayOnStart && !string.IsNullOrEmpty(swingName))
                LoadAndPlay();
        }

        private void OnDestroy()
        {
            Destroy(_jointMat);
            Destroy(_boneMat);
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Human-readable label for this skeleton (e.g. "smash_overhead (you)").
        /// Set by SwingCoordinator automatically; can also be set from game-mode code.
        /// </summary>
        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        /// <summary>True while a playback coroutine is running.</summary>
        public bool IsPlaying => _playCoroutine != null;

        /// <summary>
        /// Play frames directly from a PoseCapture (e.g. the trimmed player capture).
        /// Stops any current playback first.
        /// </summary>
        public void PlayCapture(PoseCapture capture)
        {
            if (capture == null || capture.FrameCount == 0) return;
            PlayFrames(capture.Frames, capture.CaptureRateFps);
        }

        /// <summary>
        /// Play frames directly from an array + fps.
        /// </summary>
        public void PlayFrames(PoseFrame[] frames, float fps)
        {
            if (frames == null || frames.Length == 0) return;

            EnsureSkeleton();
            Stop();

            _frames         = frames;
            _captureRateFps = fps > 0 ? fps : 30f;

            Debug.Log($"[SwingReplayVisualizer] Playing {_frames.Length} frames at {_captureRateFps} fps");
            _playCoroutine = StartCoroutine(PlayLoop());
        }

        /// <summary>
        /// Load and play a swing by name from StreamingAssets/Swings/.
        /// Also sets DisplayName to the provided name.
        /// </summary>
        public void PlayFromDisk(string name)
        {
            swingName   = name;
            displayName = name;
            LoadAndPlay();
        }

        /// <summary>Stop playback and hide the skeleton.</summary>
        public void Stop()
        {
            if (_playCoroutine != null)
            {
                StopCoroutine(_playCoroutine);
                _playCoroutine = null;
            }
            HideAll();
        }

        /// <summary>
        /// Change skeleton colors at runtime.  Useful for differentiating
        /// the player skeleton from the reference skeleton.
        /// </summary>
        public void SetColors(Color joint, Color bone)
        {
            jointColor = joint;
            boneColor  = bone;
            if (_jointMat != null) _jointMat.color = joint;
            if (_boneMat  != null) _boneMat.color  = bone;
        }

        private void LoadAndPlay()
        {
            string path = SwingPath(swingName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SwingReplayVisualizer] Not found: {path}");
                return;
            }

            var dto = JsonUtility.FromJson<SwingDto>(File.ReadAllText(path));
            if (dto?.frames == null || dto.frames.Length == 0)
            {
                Debug.LogWarning($"[SwingReplayVisualizer] Empty or corrupt: {path}");
                return;
            }

            float fps = dto.captureRateFps > 0 ? dto.captureRateFps : 30f;

            Debug.Log($"[SwingReplayVisualizer] '{swingName}'  {dto.frames.Length} frames — looping");
            PlayFrames(dto.frames, fps);
        }

        private IEnumerator PlayLoop()
        {
            var wait = new WaitForSeconds(1f / (_captureRateFps * playbackSpeed));
            int i = 0;
            while (true)
            {
                ApplyFrame(_frames[i]);
                yield return wait;
                i++;
                if (i >= _frames.Length)
                {
                    if (!loop)
                    {
                        _playCoroutine = null;
                        yield break;
                    }
                    i = 0;
                }
            }
        }

        private void ApplyFrame(PoseFrame frame)
        {
            var hips = frame.GetJoint(TrackedJoint.Hips);
            Vector3 root = hips.isTracked ? hips.position : Vector3.zero;

            foreach (var kvp in _joints)
            {
                var joint = frame.GetJoint(kvp.Key);
                var t     = kvp.Value;

                if (!joint.isTracked) { t.gameObject.SetActive(false); continue; }

                t.gameObject.SetActive(true);
                t.position = transform.TransformPoint(joint.position - root);
                t.rotation = transform.rotation * joint.rotation;
            }

            foreach (var (a, b, bt) in _bones)
            {
                if (!_joints.TryGetValue(a, out var tA) || !tA.gameObject.activeSelf ||
                    !_joints.TryGetValue(b, out var tB) || !tB.gameObject.activeSelf)
                { bt.gameObject.SetActive(false); continue; }

                bt.gameObject.SetActive(true);
                StretchBone(bt, tA.position, tB.position);
            }
        }

        private void EnsureSkeleton()
        {
            if (_skeletonBuilt) return;
            if (_jointMat == null) CreateMaterials();
            BuildSkeleton();
        }

        private void HideAll()
        {
            foreach (var kvp in _joints)
                kvp.Value.gameObject.SetActive(false);
            foreach (var (_, _, bt) in _bones)
                bt.gameObject.SetActive(false);
        }

        private void BuildSkeleton()
        {
            foreach (TrackedJoint id in System.Enum.GetValues(typeof(TrackedJoint)))
            {
                if (id == TrackedJoint.COUNT) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = id.ToString();
                go.transform.SetParent(transform);
                go.transform.localScale = Vector3.one * jointRadius * 2f;
                Destroy(go.GetComponent<Collider>());
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial    = _jointMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                go.SetActive(false);
                _joints[id] = go.transform;
            }

            foreach (var (a, b) in Connections)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"{a}_{b}";
                go.transform.SetParent(transform);
                Destroy(go.GetComponent<Collider>());
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial    = _boneMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                go.SetActive(false);
                _bones.Add((a, b, go.transform));
            }

            _skeletonBuilt = true;
        }

        private void StretchBone(Transform t, Vector3 a, Vector3 b)
        {
            float   dist = Vector3.Distance(a, b);
            Vector3 dir  = (b - a).normalized;
            t.position   = (a + b) * 0.5f;
            t.localScale = new Vector3(boneRadius * 2f, dist * 0.5f, boneRadius * 2f);
            if (dir != Vector3.zero)
                t.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        }

        private void CreateMaterials()
        {
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null &&
                       UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                           .GetType().Name.Contains("Universal");
            string sh = urp ? "Universal Render Pipeline/Lit" : "Standard";
            _jointMat = MakeMat(sh, jointColor);
            _boneMat  = MakeMat(sh, boneColor);
        }

        private static Material MakeMat(string shader, Color col)
        {
            var mat = new Material(Shader.Find(shader) ?? Shader.Find("Standard"));
            mat.color = col;
            mat.SetFloat("_Mode",    3);
            mat.SetFloat("_Surface", 1);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",   0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_ALPHABLEND_ON");
            return mat;
        }

        private static string SwingPath(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return Path.Combine(Application.streamingAssetsPath, "Swings", name + ".json");
        }

        [System.Serializable]
        private sealed class SwingDto
        {
            public string      swingName;
            public float       captureRateFps;
            public float       durationSeconds;
            public PoseFrame[] frames;
        }
    }
}