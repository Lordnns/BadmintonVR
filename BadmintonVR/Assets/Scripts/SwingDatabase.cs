// ============================================================
//  SwingDatabase.cs
//
//  Saves and loads named reference swings to/from disk.
//  Each swing is a JSON file in StreamingAssets/Swings/.
//
//  Dev workflow:
//    db.Save("smash_overhead", capture);   // writes JSON
//    db.Save("serve_lob",      capture);   // etc.
//
//  Gameplay workflow:
//    PoseCapture ref = db.Load("smash_overhead");   // reads JSON
//    db.LoadAll();                                   // bulk load
//
//  The database keeps a Dictionary<string, PoseCapture> in memory
//  so repeated lookups are O(1) with no disk I/O.
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BadmintonPoseTracking
{
    // ── JSON wrappers ──────────────────────────────────────────────────────
    //    JsonUtility can't handle PoseCapture directly (sealed class with
    //    a readonly array set in constructor).  We round-trip via a
    //    serialisable DTO.

    [Serializable]
    internal sealed class SwingDto
    {
        public string      swingName;
        public float       captureRateFps;
        public float       durationSeconds;
        public PoseFrame[] frames;
    }

    // ── Database ───────────────────────────────────────────────────────────

    public sealed class SwingDatabase
    {
        private readonly Dictionary<string, PoseCapture> _swings
            = new Dictionary<string, PoseCapture>(StringComparer.OrdinalIgnoreCase);

        private static string RootFolder =>
            Path.Combine(Application.streamingAssetsPath, "Swings");

        // ── Write ──────────────────────────────────────────────────────────

        /// <summary>
        /// Saves a capture to disk as StreamingAssets/Swings/{name}.json.
        /// Also updates the in-memory cache.
        /// </summary>
        public void Save(string swingName, PoseCapture capture)
        {
            if (string.IsNullOrEmpty(swingName))
                throw new ArgumentException("swingName must not be empty.");

            Directory.CreateDirectory(RootFolder);

            var dto = new SwingDto
            {
                swingName      = swingName,
                captureRateFps = capture.CaptureRateFps,
                durationSeconds = capture.DurationSeconds,
                frames         = capture.Frames
            };

            string path = SwingPath(swingName);
            File.WriteAllText(path, JsonUtility.ToJson(dto, prettyPrint: false));

            _swings[swingName] = capture;

            Debug.Log($"[SwingDatabase] Saved '{swingName}' → {capture.FrameCount} frames  " +
                      $"{capture.DurationSeconds:F2}s → {path}");
        }

        // ── Read ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the named swing.  Loads from disk on the first call,
        /// then serves from the in-memory cache thereafter.
        /// Returns null if the file does not exist.
        /// </summary>
        public PoseCapture Load(string swingName)
        {
            if (_swings.TryGetValue(swingName, out var cached))
                return cached;

            string path = SwingPath(swingName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SwingDatabase] '{swingName}' not found at {path}");
                return null;
            }

            return LoadFromDisk(path, swingName);
        }

        /// <summary>Loads every *.json file in StreamingAssets/Swings/ into memory.</summary>
        public void LoadAll()
        {
            if (!Directory.Exists(RootFolder)) return;

            foreach (string path in Directory.GetFiles(RootFolder, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!_swings.ContainsKey(name))
                    LoadFromDisk(path, name);
            }

            Debug.Log($"[SwingDatabase] Loaded {_swings.Count} swing(s).");
        }

        /// <summary>Returns all swing names currently in memory.</summary>
        public IEnumerable<string> Names => _swings.Keys;

        /// <summary>True if a swing with this name is available (in memory or on disk).</summary>
        public bool Exists(string swingName)
        {
            if (_swings.ContainsKey(swingName)) return true;
            return File.Exists(SwingPath(swingName));
        }

        /// <summary>Removes from memory (not from disk).</summary>
        public void Unload(string swingName) => _swings.Remove(swingName);

        /// <summary>Deletes both from memory and from disk.</summary>
        public void Delete(string swingName)
        {
            _swings.Remove(swingName);
            string path = SwingPath(swingName);
            if (File.Exists(path)) File.Delete(path);
            Debug.Log($"[SwingDatabase] Deleted '{swingName}'.");
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private PoseCapture LoadFromDisk(string path, string expectedName)
        {
            try
            {
                string    json = File.ReadAllText(path);
                SwingDto  dto  = JsonUtility.FromJson<SwingDto>(json);

                if (dto?.frames == null)
                {
                    Debug.LogError($"[SwingDatabase] Corrupt file: {path}");
                    return null;
                }

                // Reconstruct PoseCapture from the DTO
                var frameList = new System.Collections.Generic.List<PoseFrame>(dto.frames);
                var capture   = new PoseCapture(frameList, dto.captureRateFps, dto.durationSeconds);

                string name = string.IsNullOrEmpty(dto.swingName) ? expectedName : dto.swingName;
                _swings[name] = capture;

                Debug.Log($"[SwingDatabase] Loaded '{name}'  {capture.FrameCount} frames.");
                return capture;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SwingDatabase] Failed to load {path}: {ex.Message}");
                return null;
            }
        }

        private static string SwingPath(string swingName)
        {
            // Sanitise name to a safe filename
            foreach (char c in Path.GetInvalidFileNameChars())
                swingName = swingName.Replace(c, '_');
            return Path.Combine(RootFolder, swingName + ".json");
        }
    }
}