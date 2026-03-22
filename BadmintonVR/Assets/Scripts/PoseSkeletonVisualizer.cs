// ============================================================
//  PoseSkeletonVisualizer.cs
//
//  Reads directly from OVRSkeleton every LateUpdate.
//  Uses an anchorTransform as a positional reference —
//  all joint positions are offset by anchor.position so
//  the skeleton stays locked to the player's origin.
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace BadmintonPoseTracking
{
    public class PoseSkeletonVisualizer : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("OVRSkeleton on your MetaBodyTracking GameObject.")]
        public OVRSkeleton bodySkeleton;

        [Tooltip("BodyPoseRecorder — used for controller positions.")]
        public BodyPoseRecorder bodyPoseRecorder;

        [Tooltip("Transform to anchor the skeleton to (drag in your XROrigin). " +
                 "All joint positions are offset by this transform's position.")]
        public Transform anchorTransform;

        [Header("Joint Spheres")]
        [Range(0.01f, 0.1f)]  public float jointRadius      = 0.04f;
        [Range(0.01f, 0.15f)] public float controllerRadius = 0.06f;

        [Header("Bones")]
        [Range(0.005f, 0.05f)] public float boneRadius  = 0.015f;
        public bool showBones = true;

        [Header("Colors")]
        public Color trackedColor    = Color.green;
        public Color untrackedColor  = Color.red;
        public Color controllerColor = Color.cyan;
        public Color boneColor       = new Color(0.8f, 0.8f, 0.8f, 0.5f);

        [Header("Visibility")]
        public bool hideUntrackedJoints = false;
        public bool showLabels          = true;

        [Tooltip("True = render all OVR bones (including fingers). " +
                 "False = only bones the recorder tracks.")]
        public bool showFullSkeleton    = false;

        // ── Bone connections ───────────────────────────────────────────────

        private static readonly (OVRSkeleton.BoneId A, OVRSkeleton.BoneId B)[] Connections =
        {
            (OVRSkeleton.BoneId.FullBody_Hips,           OVRSkeleton.BoneId.FullBody_SpineLower),
            (OVRSkeleton.BoneId.FullBody_SpineLower,     OVRSkeleton.BoneId.FullBody_SpineMiddle),
            (OVRSkeleton.BoneId.FullBody_SpineMiddle,    OVRSkeleton.BoneId.FullBody_SpineUpper),
            (OVRSkeleton.BoneId.FullBody_SpineUpper,     OVRSkeleton.BoneId.FullBody_Chest),
            (OVRSkeleton.BoneId.FullBody_Chest,          OVRSkeleton.BoneId.FullBody_Neck),
            (OVRSkeleton.BoneId.FullBody_Neck,           OVRSkeleton.BoneId.FullBody_Head),

            (OVRSkeleton.BoneId.FullBody_Chest,          OVRSkeleton.BoneId.FullBody_LeftShoulder),
            (OVRSkeleton.BoneId.FullBody_LeftShoulder,   OVRSkeleton.BoneId.FullBody_LeftArmUpper),
            (OVRSkeleton.BoneId.FullBody_LeftArmUpper,   OVRSkeleton.BoneId.FullBody_LeftArmLower),
            (OVRSkeleton.BoneId.FullBody_LeftArmLower,   OVRSkeleton.BoneId.FullBody_LeftHandWrist),

            (OVRSkeleton.BoneId.FullBody_Chest,          OVRSkeleton.BoneId.FullBody_RightShoulder),
            (OVRSkeleton.BoneId.FullBody_RightShoulder,  OVRSkeleton.BoneId.FullBody_RightArmUpper),
            (OVRSkeleton.BoneId.FullBody_RightArmUpper,  OVRSkeleton.BoneId.FullBody_RightArmLower),
            (OVRSkeleton.BoneId.FullBody_RightArmLower,  OVRSkeleton.BoneId.FullBody_RightHandWrist),

            (OVRSkeleton.BoneId.FullBody_Hips,           OVRSkeleton.BoneId.FullBody_LeftUpperLeg),
            (OVRSkeleton.BoneId.FullBody_LeftUpperLeg,   OVRSkeleton.BoneId.FullBody_LeftLowerLeg),
            (OVRSkeleton.BoneId.FullBody_LeftLowerLeg,   OVRSkeleton.BoneId.FullBody_LeftFootAnkle),
            (OVRSkeleton.BoneId.FullBody_LeftFootAnkle,  OVRSkeleton.BoneId.FullBody_LeftFootBall),

            (OVRSkeleton.BoneId.FullBody_Hips,           OVRSkeleton.BoneId.FullBody_RightUpperLeg),
            (OVRSkeleton.BoneId.FullBody_RightUpperLeg,  OVRSkeleton.BoneId.FullBody_RightLowerLeg),
            (OVRSkeleton.BoneId.FullBody_RightLowerLeg,  OVRSkeleton.BoneId.FullBody_RightFootAnkle),
            (OVRSkeleton.BoneId.FullBody_RightFootAnkle, OVRSkeleton.BoneId.FullBody_RightFootBall),
        };

        // ── Private ────────────────────────────────────────────────────────

        private readonly Dictionary<OVRSkeleton.BoneId, GameObject> _spheres
            = new Dictionary<OVRSkeleton.BoneId, GameObject>();
        private readonly Dictionary<OVRSkeleton.BoneId, OVRBone> _boneCache
            = new Dictionary<OVRSkeleton.BoneId, OVRBone>();
        private readonly List<(OVRSkeleton.BoneId A, OVRSkeleton.BoneId B, GameObject Cap)> _capsules
            = new List<(OVRSkeleton.BoneId, OVRSkeleton.BoneId, GameObject)>();

        // Only visualise bones that the recorder actually tracks — skip fingers
        private static readonly HashSet<OVRSkeleton.BoneId> AllowedBones = new HashSet<OVRSkeleton.BoneId>
        {
            OVRSkeleton.BoneId.Body_Hips,
            OVRSkeleton.BoneId.Body_SpineLower,
            OVRSkeleton.BoneId.Body_SpineMiddle,
            OVRSkeleton.BoneId.Body_SpineUpper,
            OVRSkeleton.BoneId.Body_Chest,
            OVRSkeleton.BoneId.Body_Neck,
            OVRSkeleton.BoneId.Body_Head,

            OVRSkeleton.BoneId.Body_LeftShoulder,
            OVRSkeleton.BoneId.Body_LeftScapula,
            OVRSkeleton.BoneId.Body_LeftArmUpper,
            OVRSkeleton.BoneId.Body_LeftArmLower,
            OVRSkeleton.BoneId.Body_LeftHandWrist,

            OVRSkeleton.BoneId.Body_RightShoulder,
            OVRSkeleton.BoneId.Body_RightScapula,
            OVRSkeleton.BoneId.Body_RightArmUpper,
            OVRSkeleton.BoneId.Body_RightArmLower,
            OVRSkeleton.BoneId.Body_RightHandWrist,

            OVRSkeleton.BoneId.FullBody_LeftUpperLeg,
            OVRSkeleton.BoneId.FullBody_LeftLowerLeg,
            OVRSkeleton.BoneId.FullBody_LeftFootAnkle,
            OVRSkeleton.BoneId.FullBody_LeftFootBall,

            OVRSkeleton.BoneId.FullBody_RightUpperLeg,
            OVRSkeleton.BoneId.FullBody_RightLowerLeg,
            OVRSkeleton.BoneId.FullBody_RightFootAnkle,
            OVRSkeleton.BoneId.FullBody_RightFootBall,
        };

        private GameObject _leftControllerSphere;
        private GameObject _rightControllerSphere;

        private Material _trackedMat;
        private Material _untrackedMat;
        private Material _controllerMat;
        private Material _boneMat;

        private bool      _built;
        private PoseFrame _lastFrame;

        // Container GO — purely for hierarchy tidiness, NOT used for positioning
        private GameObject _container;

        // ── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (bodySkeleton    == null) bodySkeleton    = FindFirstObjectByType<OVRSkeleton>();
            if (bodyPoseRecorder== null) bodyPoseRecorder= GetComponent<BodyPoseRecorder>();

            if (anchorTransform == null)
            {
                var origin = FindFirstObjectByType<XROrigin>();
                if (origin != null) anchorTransform = origin.transform;
            }

            CreateMaterials();

            _container = new GameObject("SkeletonVisualizer");
            // NOT parented to anything — positions are set in world space each frame

            CreateControllerSpheres();
        }

        private void OnEnable()
        {
            if (bodyPoseRecorder != null)
                bodyPoseRecorder.onFrameCaptured += OnFrameCaptured;
        }

        private void OnDisable()
        {
            if (bodyPoseRecorder != null)
                bodyPoseRecorder.onFrameCaptured -= OnFrameCaptured;
        }

        private void OnDestroy()
        {
            if (_container != null) Destroy(_container);
            Destroy(_trackedMat);
            Destroy(_untrackedMat);
            Destroy(_controllerMat);
            Destroy(_boneMat);
        }

        private void OnFrameCaptured(PoseFrame frame) => _lastFrame = frame;

        // ── LateUpdate — main loop ─────────────────────────────────────────

        private void LateUpdate()
        {
            if (bodySkeleton == null) return;

            if (!_built && bodySkeleton.IsInitialized &&
                bodySkeleton.Bones != null && bodySkeleton.Bones.Count > 0)
            {
                BuildBoneCache();
                CreateJointSpheres();
                if (showBones) CreateBoneCapsules();
                _built = true;
            }

            if (!_built) return;

            bool tracking = bodySkeleton.IsDataValid && bodySkeleton.IsDataHighConfidence;

            UpdateJoints(tracking);
            if (showBones) UpdateCapsules(tracking);
            UpdateControllers();
        }

        // ── Joint sphere update ────────────────────────────────────────────

        private void UpdateJoints(bool tracking)
        {
            foreach (var kvp in _spheres)
            {
                var go = kvp.Value;
                if (go == null) continue;

                if (!tracking || !_boneCache.TryGetValue(kvp.Key, out var bone)
                              || bone?.Transform == null)
                {
                    if (hideUntrackedJoints) { go.SetActive(false); continue; }
                    go.SetActive(true);
                    go.GetComponent<MeshRenderer>().material = _untrackedMat;
                    continue;
                }

                go.SetActive(true);
                // OVRSkeleton bone positions are in OVR tracking space
                // (relative to the guardian/floor origin).
                // anchorTransform.TransformPoint converts tracking space → world space
                // exactly the same way XROrigin does for controllers.
                go.transform.position = anchorTransform != null
                    ? anchorTransform.TransformPoint(bone.Transform.position)
                    : bone.Transform.position;
                go.transform.rotation = anchorTransform != null
                    ? anchorTransform.rotation * bone.Transform.rotation
                    : bone.Transform.rotation;
                go.GetComponent<MeshRenderer>().material = _trackedMat;
            }
        }

        // ── Capsule update ─────────────────────────────────────────────────

        private void UpdateCapsules(bool tracking)
        {
            foreach (var (idA, idB, go) in _capsules)
            {
                if (go == null) continue;

                if (!tracking
                    || !_boneCache.TryGetValue(idA, out var bA) || bA?.Transform == null
                    || !_boneCache.TryGetValue(idB, out var bB) || bB?.Transform == null)
                {
                    go.SetActive(false);
                    continue;
                }

                go.SetActive(true);

                Vector3 posA = anchorTransform != null
                    ? anchorTransform.TransformPoint(bA.Transform.position)
                    : bA.Transform.position;
                Vector3 posB = anchorTransform != null
                    ? anchorTransform.TransformPoint(bB.Transform.position)
                    : bB.Transform.position;

                StretchCapsule(go.transform, posA, posB);
            }
        }

        // ── Controller update ──────────────────────────────────────────────

        private void UpdateControllers()
        {
            if (_lastFrame == null) return;

            var lp = _lastFrame.GetJoint(TrackedJoint.LeftController);
            if (_leftControllerSphere != null)
            {
                _leftControllerSphere.SetActive(lp.isTracked);
                if (lp.isTracked)
                    _leftControllerSphere.transform.position = lp.position;
            }

            var rp = _lastFrame.GetJoint(TrackedJoint.RightController);
            if (_rightControllerSphere != null)
            {
                _rightControllerSphere.SetActive(rp.isTracked);
                if (rp.isTracked)
                    _rightControllerSphere.transform.position = rp.position;
            }
        }

        // ── Build / create helpers ─────────────────────────────────────────

        private void BuildBoneCache()
        {
            _boneCache.Clear();
            foreach (var bone in bodySkeleton.Bones)
            {
                if (bone?.Transform == null) continue;
                if (!showFullSkeleton && !AllowedBones.Contains(bone.Id)) continue;
                _boneCache[bone.Id] = bone;
            }

            Debug.Log($"[PoseSkeletonVisualizer] Cached {_boneCache.Count} bones " +
                      $"(filtered from {bodySkeleton.Bones.Count} total).");
        }

        private void CreateJointSpheres()
        {
            foreach (var kvp in _boneCache)
            {
                var go = MakeSphere($"Joint_{kvp.Key}", jointRadius, _untrackedMat);
                go.SetActive(!hideUntrackedJoints);
                _spheres[kvp.Key] = go;
            }
        }

        private void CreateBoneCapsules()
        {
            foreach (var (a, b) in Connections)
            {
                var go = MakeCapsule($"Bone_{a}_{b}");
                go.SetActive(false);
                _capsules.Add((a, b, go));
            }
        }

        private void CreateControllerSpheres()
        {
            _leftControllerSphere  = MakeSphere("Joint_LeftController",  controllerRadius, _controllerMat);
            _rightControllerSphere = MakeSphere("Joint_RightController", controllerRadius, _controllerMat);
        }

        // ── Primitive factories ────────────────────────────────────────────

        private GameObject MakeSphere(string goName, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = goName;
            go.transform.SetParent(_container.transform);
            go.transform.localScale = Vector3.one * radius * 2f;
            Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.material          = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            return go;
        }

        private GameObject MakeCapsule(string goName)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = goName;
            go.transform.SetParent(_container.transform);
            Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.material          = _boneMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            return go;
        }

        private void StretchCapsule(Transform t, Vector3 a, Vector3 b)
        {
            float   dist = Vector3.Distance(a, b);
            Vector3 dir  = (b - a).normalized;
            t.position   = (a + b) * 0.5f;
            t.localScale = new Vector3(boneRadius * 2f, dist * 0.5f, boneRadius * 2f);
            if (dir != Vector3.zero)
                t.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        }

        // ── Materials ──────────────────────────────────────────────────────

        private void CreateMaterials()
        {
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null &&
                       UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                           .GetType().Name.Contains("Universal");
            string sh = urp ? "Universal Render Pipeline/Lit" : "Standard";

            _trackedMat    = MakeMat(sh, trackedColor,    0.5f);
            _untrackedMat  = MakeMat(sh, untrackedColor,  0.35f);
            _controllerMat = MakeMat(sh, controllerColor, 0.7f);
            _boneMat       = MakeMat(sh, boneColor,       0.25f);
        }

        private static Material MakeMat(string shaderName, Color col, float alpha)
        {
            var mat = new Material(Shader.Find(shaderName) ?? Shader.Find("Standard"));
            col.a = alpha;
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

        // ── Editor gizmo labels ────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showLabels || !_built) return;

            var style = new GUIStyle { fontSize = 9 };
            style.normal.textColor = Color.white;

            foreach (var kvp in _boneCache)
            {
                if (kvp.Value?.Transform == null) continue;
                Vector3 pos = anchorTransform != null
                    ? anchorTransform.TransformPoint(kvp.Value.Transform.position)
                    : kvp.Value.Transform.position;
                UnityEditor.Handles.Label(
                    pos + Vector3.up * 0.05f,
                    kvp.Key.ToString().Replace("FullBody_", ""), style);
            }
        }
#endif
    }
}