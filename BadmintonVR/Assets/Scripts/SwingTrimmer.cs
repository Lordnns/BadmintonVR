// ============================================================
//  SwingTrimmer.cs
//
//  Extracts the "active swing" window from a player capture that
//  may contain idle frames before/after the actual movement.
//
//  PROBLEM
//  ────────────────────────────────────────────────────────────
//  Reference swing: ~0.4s   Player capture: ~1.0s
//  Half the capture is the player standing still.  DTW tries to
//  align idle frames against the reference wind-up and scores
//  them as garbage, dragging the overall score down.
//
//  SOLUTION
//  ────────────────────────────────────────────────────────────
//  1. Compute per-frame "motion energy" from angular velocity
//     of the scoring joints (same joints used by SwingMatcher).
//  2. Smooth the signal to kill single-frame spikes.
//  3. Slide a window whose duration ≈ reference swing length,
//     find the window with the highest cumulative energy.
//  4. Optionally expand the window edges to catch the very
//     start/end of motion (where energy is still above a
//     threshold).
//  5. Return the trimmed frame slice as a new PoseCapture.
//
//  The existing DTW in SwingMatcher is untouched — it just
//  receives cleaner, tighter input.
//
//  USAGE
//  ────────────────────────────────────────────────────────────
//  Called automatically by SwingMatcher.Compare() when
//  autoTrim is enabled (default: true).
//
//  Can also be called manually:
//    var trimmed = SwingTrimmer.Trim(playerCapture, refDurationSec);
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BadmintonPoseTracking
{
    public static class SwingTrimmer
    {
        // ── Configuration ───────────────────────────────────────────────
        public const float WindowStretchFactor = 1.6f;
        public const float TrimActivationRatio = 1.4f;
        public const int SmoothHalfWidth = 2;
        public const float EdgeThresholdFraction = 0.10f;

        // ── Main entry point ────────────────────────────────────────────
        public static PoseCapture Trim(
            PoseCapture player,
            float       referenceDurationSec,
            float       captureRateFps,
            int[]       jointIds = null)
        {
            if (player == null || player.FrameCount < 3)
                return player;

            // How many frames does the reference represent?
            int refFrames    = Mathf.Max(1, Mathf.RoundToInt(referenceDurationSec * captureRateFps));
            int playerFrames = player.FrameCount;

            Debug.Log($"[SwingTrimmer] player={playerFrames}f  ref={refFrames}f ({referenceDurationSec:F2}s @ {captureRateFps}fps)  " +
                      $"ratio={playerFrames / (float)refFrames:F2}  threshold={TrimActivationRatio:F2}");

            // Skip trimming if the player capture isn't significantly longer
            if (playerFrames <= refFrames * TrimActivationRatio)
            {
                Debug.Log("[SwingTrimmer] Skipped — player capture is close enough to reference length.");
                return player;
            }

            jointIds ??= DefaultActivityJoints();

            // Step 1 — compute raw per-frame angular velocity energy
            float[] rawEnergy = ComputeEnergy(player.Frames, playerFrames, jointIds);

            // Step 2 — smooth
            float[] energy = Smooth(rawEnergy, playerFrames, SmoothHalfWidth);

            // Step 3 — find best window
            int windowSize = Mathf.Min(
                playerFrames,
                Mathf.RoundToInt(refFrames * WindowStretchFactor));

            // Ensure window is at least as big as the reference
            windowSize = Mathf.Max(windowSize, refFrames);

            (int bestStart, float peakEnergy) = FindBestWindow(energy, playerFrames, windowSize);

            if (peakEnergy < 0.001f)
            {
                // Flat signal — no detectable motion. Return as-is.
                Debug.Log("[SwingTrimmer] No motion detected — skipping trim.");
                return player;
            }

            // Step 4 — expand edges outward while energy is above threshold
            float edgeThreshold = peakEnergy / windowSize * EdgeThresholdFraction;
            int start = bestStart;
            int end   = Mathf.Min(playerFrames - 1, bestStart + windowSize - 1);

            while (start > 0 && energy[start - 1] > edgeThreshold)
                start--;
            while (end < playerFrames - 1 && energy[end + 1] > edgeThreshold)
                end++;

            // Clamp: don't let expansion make us bigger than the original
            int trimmedCount = end - start + 1;
            if (trimmedCount >= playerFrames)
                return player;

            // Step 5 — build trimmed capture
            var trimmedFrames = new List<PoseFrame>(trimmedCount);
            for (int i = start; i <= end; i++)
                trimmedFrames.Add(player.Frames[i]);

            float trimmedDuration = trimmedCount / captureRateFps;

            Debug.Log($"[SwingTrimmer] Trimmed {playerFrames} → {trimmedCount} frames  " +
                      $"(window [{start}..{end}], ref={refFrames}f)  " +
                      $"{player.DurationSeconds:F2}s → {trimmedDuration:F2}s");

            return new PoseCapture(trimmedFrames, captureRateFps, trimmedDuration);
        }

        // ── Energy computation ──────────────────────────────────────────
        //
        //    Per-frame energy = sum of angular differences between this
        //    frame and the previous frame for each scoring joint.
        //    Units: degrees.  Frame 0 gets energy = 0.

        private static float[] ComputeEnergy(PoseFrame[] frames, int count, int[] jointIds)
        {
            float[] energy = new float[count];
            energy[0] = 0f;

            for (int i = 1; i < count; i++)
            {
                float sum = 0f;
                PoseFrame prev = frames[i - 1];
                PoseFrame curr = frames[i];

                for (int j = 0; j < jointIds.Length; j++)
                {
                    int id = jointIds[j];
                    var pj = prev.joints[id];
                    var cj = curr.joints[id];

                    if (!pj.isTracked || !cj.isTracked) continue;

                    // Angular velocity approximation (degrees per frame)
                    sum += Quaternion.Angle(pj.rotation, cj.rotation);
                }

                energy[i] = sum;
            }

            return energy;
        }

        // ── Smoothing ───────────────────────────────────────────────────

        private static float[] Smooth(float[] raw, int count, int halfWidth)
        {
            float[] smoothed = new float[count];

            for (int i = 0; i < count; i++)
            {
                float sum   = 0f;
                int   n     = 0;
                int   lo    = Mathf.Max(0, i - halfWidth);
                int   hi    = Mathf.Min(count - 1, i + halfWidth);

                for (int k = lo; k <= hi; k++)
                {
                    sum += raw[k];
                    n++;
                }

                smoothed[i] = sum / n;
            }

            return smoothed;
        }

        // ── Sliding-window peak finder ──────────────────────────────────
        //
        //    Returns (startIndex, totalEnergy) for the window with the
        //    highest cumulative energy.  Uses an O(n) running-sum approach.

        private static (int start, float energy) FindBestWindow(
            float[] energy, int count, int windowSize)
        {
            if (windowSize >= count)
                return (0, CumulativeSum(energy, 0, count - 1));

            // Seed with the first window
            float windowSum = CumulativeSum(energy, 0, windowSize - 1);
            float bestSum   = windowSum;
            int   bestStart = 0;

            // Slide one frame at a time
            for (int s = 1; s + windowSize - 1 < count; s++)
            {
                windowSum -= energy[s - 1];
                windowSum += energy[s + windowSize - 1];

                if (windowSum > bestSum)
                {
                    bestSum   = windowSum;
                    bestStart = s;
                }
            }

            return (bestStart, bestSum);
        }

        private static float CumulativeSum(float[] arr, int from, int to)
        {
            float sum = 0f;
            for (int i = from; i <= to; i++) sum += arr[i];
            return sum;
        }

        // ── Default joints for activity detection ───────────────────────
        //
        //    Racket arm + shoulders — the joints that move most during
        //    any badminton stroke.  We deliberately weight them equally
        //    here since we just want a motion/no-motion signal.

        private static int[] DefaultActivityJoints() => new int[]
        {
            (int)TrackedJoint.RightShoulder,
            (int)TrackedJoint.RightUpperArm,
            (int)TrackedJoint.RightForearm,
            (int)TrackedJoint.RightWrist,
            (int)TrackedJoint.LeftShoulder,
            (int)TrackedJoint.LeftUpperArm,
        };
    }
}