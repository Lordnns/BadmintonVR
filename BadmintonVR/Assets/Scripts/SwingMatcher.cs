// ============================================================
//  SwingMatcher.cs  (V2 — segment-based scoring)
//
//  Compares two PoseCaptures using Dynamic Time Warping (DTW)
//  with a Sakoe-Chiba band constraint.
//
//  ─── WHAT CHANGED FROM V1 ───────────────────────────────────
//
//  V1 FrameDist() blended joint.rotation and hips-to-joint
//  direction 50/50.  Two problems killed the score gradient:
//
//    1. Joint rotations on Quest 3 body tracking are derived
//       from child-joint positions and carry ~20–30° of
//       baseline noise.  Making rotation half the signal
//       pre-loaded every score with a large random error that
//       good and bad swings shared equally → scores collapsed
//       into a narrow mid-range band.
//
//    2. Hips-to-wrist direction partially masks arm motion:
//       OVR's body-tracking IK shifts the shoulder joint when
//       the arm rises, so the shoulder (and therefore the
//       wrist) co-moves with the arm.  A high-arm pose and a
//       low-arm pose produce surprisingly similar hips→wrist
//       directions, so the metric loses discrimination on the
//       exact feature it was supposed to measure.
//
//  V2 scores BONE DIRECTION VECTORS in body frame, using
//  position-derived bone segments only:
//
//      upper-arm     = shoulder → elbow
//      forearm       = elbow    → wrist
//      arm extension = shoulder → wrist   ← main arm-posture
//                                          discriminator
//      torso         = hips     → chest
//      shoulder line = L.shldr  → R.shldr
//      head          = chest    → head
//
//  Positions are far less noisy than rotations on Quest 3,
//  Vector3.Angle is scale-invariant (works with any body
//  size), and the shoulder→wrist vector responds *directly*
//  to arm posture because IK co-movement cancels when both
//  endpoints are on the same arm.
//
//  ─── COMPATIBILITY ──────────────────────────────────────────
//
//  The JSON format (ReferencePoseSequence / SwingDto) is
//  UNCHANGED.  V2 reads exactly the same JointPose data that
//  V1 did — only the cost function is different.  Existing
//  reference swings score correctly without re-recording.
//
//  ─── PRESERVED FROM V1 ──────────────────────────────────────
//  • DTW + Sakoe-Chiba band
//  • Pre-allocated flat cost matrix (no per-call alloc)
//  • Geometric mean of path-cost and joint-deviation scores
//  • Auto-trim delegation to SwingTrimmer
//  • Public API: SwingMatcher(), Compare(), SetScoringJoints(),
//    BandFraction, Sensitivity, AutoTrim
//
//  MAX DIMENSIONS
//  MaxFrames = 180 supports captures up to 6 s at 30 fps.
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ── Result ────────────────────────────────────────────────────────────

    public sealed class SwingScore
    {
        /// <summary>0 = no match, 100 = perfect.</summary>
        public float Score;

        /// <summary>Normalised DTW cost before the exp conversion (debug use).</summary>
        public float NormalisedCost;

        /// <summary>Average per-segment angular deviation (degrees) along the DTW path.</summary>
        public float AvgJointDeg;

        /// <summary>Segments that deviated most on average across the matched path.</summary>
        public string[] WeakJoints;

        /// <summary>Original player frame count before trimming (-1 if no trim).</summary>
        public int OriginalFrameCount = -1;

        /// <summary>Frame count actually sent into DTW (after trimming).</summary>
        public int TrimmedFrameCount  = -1;

        /// <summary>Convenience: Score >= threshold → passing grade.</summary>
        public bool Passes(float threshold = 60f) => Score >= threshold;

        public override string ToString()
        {
            string trim = OriginalFrameCount > 0
                ? $"  Trim={OriginalFrameCount}→{TrimmedFrameCount}f"
                : string.Empty;
            return $"Score={Score:F1}  NormCost={NormalisedCost:F3}  AvgSegDeg={AvgJointDeg:F1}°" +
                   $"  Weak=[{string.Join(", ", WeakJoints)}]{trim}";
        }
    }

    // ── Matcher ───────────────────────────────────────────────────────────

    public sealed class SwingMatcher
    {
        // ── Tuning knobs (set before comparing) ───────────────────────────

        /// <summary>
        /// Fraction of max(n,m) used as the Sakoe-Chiba band.
        /// 0.15 = tight (must match timing closely).
        /// 0.30 = loose (allows slow/fast swings).
        /// </summary>
        public float BandFraction = 0.20f;

        /// <summary>
        /// Controls how aggressively the score drops with angular error.
        /// Units: degrees.  A normalised cost equal to (NoiseFloor + Sensitivity)
        /// → score ~37.  Lower = stricter grading.  Good range: 25–50 for V2.
        /// </summary>
        public float Sensitivity = 35f;

        /// <summary>
        /// Angular cost (degrees) treated as "free" before the exp falloff.
        /// Accounts for Quest 3 Body-mode baseline noise — hips rotation
        /// jitter and joint position jitter produce ~18–25° of per-frame
        /// cost even when comparing identical motions.  Without this, the
        /// achievable ceiling is ~70 even for a perfect swing.
        ///
        /// Cost below NoiseFloor → score 100.  Cost above NoiseFloor falls
        /// off exp(-(cost - NoiseFloor) / Sensitivity) × 100.
        /// </summary>
        public float NoiseFloor = 20f;

        /// <summary>
        /// When true, the player capture is automatically trimmed to its
        /// active swing window before DTW comparison.
        /// </summary>
        public bool AutoTrim = true;

        // ── Pre-allocated DTW matrix ──────────────────────────────────────

        private const int MaxFrames = 180;
        private readonly float[] _dtw = new float[MaxFrames * MaxFrames];

        // ── Segment table ─────────────────────────────────────────────────
        //
        //    Each segment is an (anchor, tip, weight, label) tuple.  We
        //    compare the body-relative direction vector (tip.pos - anchor.pos)
        //    between player and reference.  Vector3.Angle gives 0..180°.
        //
        //    Weights reflect "how much does this segment say about swing
        //    posture".  Racket arm dominates.  The total weight is not
        //    meaningful — only relative ratios matter (we divide at the end).

        private struct Segment
        {
            public int    from;   // (int)TrackedJoint
            public int    to;
            public float  weight;
            public string label;
        }

        //    NOTE ON OVR JOINT POSITIONS
        //    ─────────────────────────────────────────────────────
        //    In OVR body tracking:
        //      RightShoulder.pos  ≈ shoulder socket
        //      RightUpperArm.pos  ≈ shoulder socket (same point — upper-arm
        //                            bone's pivot is at its shoulder end)
        //      RightForearm.pos   = elbow
        //      RightWrist.pos     = wrist
        //
        //    So `Shoulder → UpperArm` is a near-zero-length segment and
        //    would give unstable directions.  We use:
        //      UpperArm → Forearm  (upper-arm bone = elbow rel to shoulder)
        //      Forearm  → Wrist    (forearm bone  = wrist rel to elbow)
        //      Shoulder → Wrist    (total arm direction = main posture cue)

        private static readonly Segment[] _segments =
        {
            // ── Racket arm (RIGHT) — primary signal ─────────────────────
            // Upper-arm bone direction: shoulder end → elbow
            new Segment { from = (int)TrackedJoint.RightUpperArm, to = (int)TrackedJoint.RightForearm, weight = 1.5f, label = "R.UpperArm" },
            // Forearm bone direction: elbow → wrist
            new Segment { from = (int)TrackedJoint.RightForearm,  to = (int)TrackedJoint.RightWrist,   weight = 1.5f, label = "R.Forearm"  },
            // TOTAL arm extension: shoulder → wrist.  Strongest "arm up vs
            // arm down" discriminator — IK co-movement cancels because both
            // endpoints sit on the same arm and shift together.
            new Segment { from = (int)TrackedJoint.RightShoulder, to = (int)TrackedJoint.RightWrist,   weight = 2.5f, label = "R.ArmExt"   },

            // ── Left arm — counter-balance signal ───────────────────────
            new Segment { from = (int)TrackedJoint.LeftUpperArm,  to = (int)TrackedJoint.LeftForearm, weight = 0.5f, label = "L.UpperArm" },
            new Segment { from = (int)TrackedJoint.LeftShoulder,  to = (int)TrackedJoint.LeftWrist,   weight = 0.7f, label = "L.ArmExt"   },

            // ── Torso posture ───────────────────────────────────────────
            // Lean / spine direction
            new Segment { from = (int)TrackedJoint.Hips,          to = (int)TrackedJoint.Chest,        weight = 0.8f, label = "Torso"      },
            // Shoulder line — captures upper-body twist vs hips
            new Segment { from = (int)TrackedJoint.LeftShoulder,  to = (int)TrackedJoint.RightShoulder, weight = 0.6f, label = "Shoulders" },

            // ── Head (HMD is always clean) ──────────────────────────────
            new Segment { from = (int)TrackedJoint.Chest,         to = (int)TrackedJoint.Head,         weight = 0.4f, label = "Head"       },
        };

        // Per-segment cumulative-deviation accumulators (for weak-joint report)
        private readonly float[] _segmentDev  = new float[_segments.Length];
        private readonly int[]   _segmentHits = new int  [_segments.Length];

        // ── Constructors (API-compatible with V1) ─────────────────────────

        public SwingMatcher() { }

        /// <summary>Legacy V1 constructor.  Scoring joints array is ignored;
        /// V2 uses a hard-coded segment table.  Kept for API compatibility.</summary>
        public SwingMatcher(ScoringJointEntry[] _scoringJoints) { }

        /// <summary>Legacy V1 setter.  No-op in V2.</summary>
        public void SetScoringJoints(ScoringJointEntry[] _scoringJoints) { }

        // ── Main entry points ─────────────────────────────────────────────

        public SwingScore Compare(PoseCapture player, PoseCapture reference)
        {
            // V2 lets the trimmer pick its own default activity joints —
            // they're tuned for motion detection, not posture scoring.
            PoseCapture trimmed = AutoTrim
                ? SwingTrimmer.Trim(player, reference.DurationSeconds,
                                    reference.CaptureRateFps)
                : player;

            SwingScore score = CompareFrameArrays(trimmed.Frames, trimmed.FrameCount,
                                                   reference.Frames, reference.FrameCount);

            if (AutoTrim && trimmed != player)
            {
                score.OriginalFrameCount = player.FrameCount;
                score.TrimmedFrameCount  = trimmed.FrameCount;
            }
            return score;
        }

        /// <summary>Overload accepting a ReferencePoseSequence directly.</summary>
        public SwingScore Compare(PoseCapture player, ReferencePoseSequence reference)
        {
            float refDuration = reference.DurationSeconds;
            float refFps      = reference.captureRateFps > 0 ? reference.captureRateFps : 30f;

            PoseCapture trimmed = AutoTrim
                ? SwingTrimmer.Trim(player, refDuration, refFps)
                : player;

            var refFrames = reference.frames;
            int refCount  = refFrames.Count;
            var refArr    = new PoseFrame[refCount];
            for (int i = 0; i < refCount; i++) refArr[i] = refFrames[i];

            SwingScore score = CompareFrameArrays(trimmed.Frames, trimmed.FrameCount,
                                                   refArr, refCount);

            if (AutoTrim && trimmed != player)
            {
                score.OriginalFrameCount = player.FrameCount;
                score.TrimmedFrameCount  = trimmed.FrameCount;
            }
            return score;
        }

        // ── Core DTW ──────────────────────────────────────────────────────

        private SwingScore CompareFrameArrays(PoseFrame[] pFrames, int n,
                                               PoseFrame[] rFrames, int m)
        {
            if (n == 0 || m == 0)
                return new SwingScore { Score = 0f, NormalisedCost = float.MaxValue,
                                        WeakJoints = Array.Empty<string>() };

            n = Mathf.Min(n, MaxFrames);
            m = Mathf.Min(m, MaxFrames);

            // Band must be at least |n−m| so the path can reach the endpoint.
            int lengthDiff = Mathf.Abs(n - m);
            int band = Mathf.Max(lengthDiff,
                           Mathf.Max(2, Mathf.RoundToInt(Mathf.Max(n, m) * BandFraction)));

            Debug.Log($"[SwingMatcher V2] DTW  n={n}  m={m}  band={band}  (diff={lengthDiff})");

            // Reset per-segment deviation accumulators
            for (int k = 0; k < _segments.Length; k++)
            {
                _segmentDev[k]  = 0f;
                _segmentHits[k] = 0;
            }

            // ── Fill DTW matrix ───────────────────────────────────────────
            const float INF = float.MaxValue * 0.5f;

            _dtw[0] = FrameDist(pFrames[0], rFrames[0]);

            // First column
            for (int i = 1; i < n; i++)
            {
                int j = 0;
                if (Mathf.Abs(i - j) <= band)
                {
                    float prev = _dtw[(i - 1) * MaxFrames + j];
                    _dtw[i * MaxFrames + j] = prev < INF
                        ? FrameDist(pFrames[i], rFrames[j]) + prev
                        : INF;
                }
                else _dtw[i * MaxFrames + j] = INF;
            }

            // First row
            for (int j = 1; j < m; j++)
            {
                int i = 0;
                if (Mathf.Abs(i - j) <= band)
                {
                    float prev = _dtw[0 * MaxFrames + (j - 1)];
                    _dtw[0 * MaxFrames + j] = prev < INF
                        ? FrameDist(pFrames[i], rFrames[j]) + prev
                        : INF;
                }
                else _dtw[0 * MaxFrames + j] = INF;
            }

            // Fill remaining cells
            for (int i = 1; i < n; i++)
            {
                int jMin = Mathf.Max(1, i - band);
                int jMax = Mathf.Min(m - 1, i + band);

                for (int j = jMin; j <= jMax; j++)
                {
                    float cost = FrameDist(pFrames[i], rFrames[j]);
                    float a = _dtw[(i - 1) * MaxFrames + j];
                    float b = _dtw[i       * MaxFrames + (j - 1)];
                    float c = _dtw[(i - 1) * MaxFrames + (j - 1)];
                    float best = a < b ? a : b;
                    if (c < best) best = c;
                    _dtw[i * MaxFrames + j] = best < INF ? cost + best : INF;
                }
                for (int j = 0;        j < jMin; j++) _dtw[i * MaxFrames + j] = INF;
                for (int j = jMax + 1; j < m;    j++) _dtw[i * MaxFrames + j] = INF;
            }

            // ── Traceback to accumulate per-segment deviations ────────────
            int pi = n - 1, pj = m - 1;
            int pathLength = 0;
            while (pi > 0 || pj > 0)
            {
                FrameDistDetailed(pFrames[pi], rFrames[pj]);
                pathLength++;
                if (pi == 0) { pj--; continue; }
                if (pj == 0) { pi--; continue; }
                float d   = _dtw[(pi - 1) * MaxFrames + (pj - 1)];
                float ins = _dtw[(pi - 1) * MaxFrames + pj];
                float del = _dtw[pi       * MaxFrames + (pj - 1)];
                if (d <= ins && d <= del) { pi--; pj--; }
                else if (ins <= del)      { pi--; }
                else                      { pj--; }
            }
            FrameDistDetailed(pFrames[0], rFrames[0]);
            pathLength++;

            // ── Compute final score ───────────────────────────────────────
            //
            //    Two independent signals combined via geometric mean:
            //      1. dtwScore   — path-alignment quality (timing-invariant).
            //      2. jointScore — weighted-average per-segment deviation
            //                       over the optimal path.
            //    A wrong swing that fools the DTW path will still show high
            //    segment deviations, so the geometric mean catches it.

            float rawCost  = _dtw[(n - 1) * MaxFrames + (m - 1)];
            float normCost = rawCost / Mathf.Max(1, pathLength);
            float dtwEff   = Mathf.Max(0f, normCost - NoiseFloor);
            float dtwScore = Mathf.Exp(-dtwEff / Sensitivity) * 100f;

            // Weighted average of per-segment mean deviations
            float totalWDev = 0f;
            float totalW    = 0f;
            for (int k = 0; k < _segments.Length; k++)
            {
                if (_segmentHits[k] == 0) continue;
                float segMean = _segmentDev[k] / _segmentHits[k];
                totalWDev += segMean * _segments[k].weight;
                totalW    += _segments[k].weight;
            }
            float avgSegDeg  = totalW > 0f ? totalWDev / totalW : 0f;
            float jointEff   = Mathf.Max(0f, avgSegDeg - NoiseFloor);
            float jointScore = Mathf.Exp(-jointEff / Sensitivity) * 100f;

            // Geometric mean — amplifies real differences
            float score = Mathf.Sqrt(dtwScore * jointScore);

            Debug.Log($"[SwingMatcher V2] dtwScore={dtwScore:F1}  jointScore={jointScore:F1}  " +
                      $"avgSegDev={avgSegDeg:F1}°  normCost={normCost:F1}°  " +
                      $"floor={NoiseFloor:F0}°  sens={Sensitivity:F0}°  pathLen={pathLength}  " +
                      $"→ final={score:F1}");

            // ── Identify weak segments ────────────────────────────────────
            const int MaxWeakSegs = 3;
            int weakCount = 0;
            string[] weak = new string[MaxWeakSegs];
            bool[] used = new bool[_segments.Length];
            for (int w = 0; w < MaxWeakSegs; w++)
            {
                float maxDev = 0f;
                int   maxIdx = -1;
                for (int k = 0; k < _segments.Length; k++)
                {
                    if (used[k] || _segmentHits[k] == 0) continue;
                    float avg = _segmentDev[k] / _segmentHits[k];
                    if (avg > maxDev) { maxDev = avg; maxIdx = k; }
                }
                if (maxIdx < 0 || maxDev < 15f) break;  // < 15° = not noteworthy
                used[maxIdx] = true;
                weak[weakCount++] = $"{_segments[maxIdx].label} ({maxDev:F0}°)";
            }
            var finalWeak = new string[weakCount];
            Array.Copy(weak, finalWeak, weakCount);

            return new SwingScore
            {
                Score          = score,
                NormalisedCost = normCost,
                AvgJointDeg    = avgSegDeg,
                WeakJoints     = finalWeak
            };
        }

        // ══════════════════════════════════════════════════════════════════
        //  FRAME DISTANCE  (V2 — bone-direction in body frame)
        // ══════════════════════════════════════════════════════════════════

        // ── Body frame (unchanged from V1) ────────────────────────────────
        //
        //    Pelvis rotation is the most stable anchor on Quest 3 Body-mode.
        //    Falls back to a synthetic shoulder frame, then HMD yaw.

        private static Quaternion BodyFrame(in PoseFrame f)
        {
            var hips = f.GetJoint(TrackedJoint.Hips);
            if (hips.isTracked)
                return hips.rotation;

            var ls = f.GetJoint(TrackedJoint.LeftShoulder);
            var rs = f.GetJoint(TrackedJoint.RightShoulder);
            if (ls.isTracked && rs.isTracked)
            {
                Vector3 right = rs.position - ls.position;
                if (right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    Vector3 forward = Vector3.Cross(right, Vector3.up);
                    if (forward.sqrMagnitude > 0.001f)
                    {
                        forward.Normalize();
                        return Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                    }
                }
            }

            var head = f.GetJoint(TrackedJoint.Head);
            if (head.isTracked)
            {
                Vector3 fwd = head.rotation * Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.001f)
                    return Quaternion.LookRotation(fwd.normalized, Vector3.up);
            }

            return Quaternion.identity;
        }

        // ── Penalty constants for missing / degenerate data ───────────────
        // Chosen to be *less* severe than a genuinely wrong swing (~90°+)
        // but non-zero so the optimizer doesn't reward lost tracking.
        private const float PenaltyUntracked = 40f;
        private const float PenaltyDegenerate = 25f;

        /// <summary>
        /// Per-frame cost = weighted-average segment angle in degrees.
        /// Value range is [0, 180]; typical good frames 10–25°, bad 40–90°.
        /// </summary>
        private float FrameDist(in PoseFrame a, in PoseFrame b)
        {
            Quaternion aInv = Quaternion.Inverse(BodyFrame(a));
            Quaternion bInv = Quaternion.Inverse(BodyFrame(b));

            float totalCost = 0f;
            float totalWeight = 0f;

            for (int k = 0; k < _segments.Length; k++)
            {
                Segment seg = _segments[k];
                var af = a.joints[seg.from]; var at = a.joints[seg.to];
                var bf = b.joints[seg.from]; var bt = b.joints[seg.to];

                float angle;
                if (!af.isTracked || !at.isTracked || !bf.isTracked || !bt.isTracked)
                {
                    angle = PenaltyUntracked;
                }
                else
                {
                    Vector3 aDirWorld = at.position - af.position;
                    Vector3 bDirWorld = bt.position - bf.position;

                    if (aDirWorld.sqrMagnitude < 1e-6f || bDirWorld.sqrMagnitude < 1e-6f)
                    {
                        angle = PenaltyDegenerate;
                    }
                    else
                    {
                        // Rotate into body-local space so facing direction cancels
                        Vector3 aDir = aInv * aDirWorld;
                        Vector3 bDir = bInv * bDirWorld;
                        angle = Vector3.Angle(aDir, bDir);   // [0, 180]
                    }
                }

                totalCost   += angle * seg.weight;
                totalWeight += seg.weight;
            }

            return totalWeight > 0f ? totalCost / totalWeight : 0f;
        }

        /// <summary>
        /// Same computation as FrameDist, but accumulates per-segment
        /// angles into _segmentDev / _segmentHits for the weak-joint
        /// report.  Called only along the DTW traceback path.
        /// </summary>
        private void FrameDistDetailed(in PoseFrame a, in PoseFrame b)
        {
            Quaternion aInv = Quaternion.Inverse(BodyFrame(a));
            Quaternion bInv = Quaternion.Inverse(BodyFrame(b));

            for (int k = 0; k < _segments.Length; k++)
            {
                Segment seg = _segments[k];
                var af = a.joints[seg.from]; var at = a.joints[seg.to];
                var bf = b.joints[seg.from]; var bt = b.joints[seg.to];

                float angle;
                if (!af.isTracked || !at.isTracked || !bf.isTracked || !bt.isTracked)
                {
                    angle = PenaltyUntracked;
                }
                else
                {
                    Vector3 aDirWorld = at.position - af.position;
                    Vector3 bDirWorld = bt.position - bf.position;

                    if (aDirWorld.sqrMagnitude < 1e-6f || bDirWorld.sqrMagnitude < 1e-6f)
                    {
                        angle = PenaltyDegenerate;
                    }
                    else
                    {
                        Vector3 aDir = aInv * aDirWorld;
                        Vector3 bDir = bInv * bDirWorld;
                        angle = Vector3.Angle(aDir, bDir);
                    }
                }

                _segmentDev[k]  += angle;
                _segmentHits[k] += 1;
            }
        }
    }
}