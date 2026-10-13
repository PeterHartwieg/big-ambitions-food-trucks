#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodTrucks.BuildEditor
{
    // Entry point for `Unity -batchmode -executeMethod FoodTrucks.BuildEditor.BatchBuild.Build -modId <id>`.
    // Runs the SDK's own validator and packager, then exits with 0 on success and 1 on failure.
    public static class BatchBuild
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);
        private static bool finished;
        // The SDK's HDRP assets support deferred lit shaders only (supportedLitShaderMode 2), so the build strips
        // every opaque Forward variant and our copy of the game's SH_Vehicle has no Forward pass at all. The
        // game's hover outline (a DrawRenderers custom pass on the outlined layers) only draws objects whose shader
        // has a Forward pass, so the truck got none. Both modes (3) for the build, as the game's own shaders keep
        // Forward. The values go back before Unity exits; tools/build-mod.sh then restores the files Unity rewrote.
        private const string LitShaderMode = "m_RenderPipelineSettings.supportedLitShaderMode";
        private const int BothLitShaderModes = 3;
        private static readonly List<(UnityEngine.Object Asset, int Mode)> changedPipelines = new List<(UnityEngine.Object, int)>();

        public static void Build()
        {
            var deadline = DateTime.UtcNow + Timeout;
            EditorApplication.update += () =>
            {
                if (!finished && DateTime.UtcNow > deadline) Finish(1, $"No result from the packager after {Timeout.TotalMinutes} minutes.");
            };
            var modId = ArgValue("-modId") ?? "FoodTrucks";
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                var all = ModDiscovery.DiscoverAll();
                var mod = all.FirstOrDefault(m => m.Manifest.ModId == modId);
                if (mod == null) { Finish(1, $"No mod with ModId '{modId}' under Assets/Mods."); return; }

                var issues = ModValidator.Validate(mod, all);
                foreach (var issue in issues) Debug.Log($"[FoodTrucksBuild] {issue.Severity}: {issue.Message}");
                if (issues.Any(i => i.Severity == Severity.Error)) { Finish(1, "Validation failed."); return; }

                ModPackager.JobChanged += job =>
                {
                    if (job.Mod.Manifest.ModId != modId || !job.IsTerminal) return;
                    foreach (var line in job.Log) Debug.Log("[FoodTrucksBuild] " + line);
                    foreach (var msg in job.CompilerMessages) Debug.Log($"[FoodTrucksBuild] {msg.type}: {msg.message}");
                    Finish(job.State == BuildState.Done ? 0 : 1, job.StatusText + " Output: " + job.OutputDirectoryAbsolute);
                };
                KeepForwardPasses();
                ModPackager.Enqueue(mod, installAfterBuild: false, revealWhenDone: false);
            }
            catch (Exception ex)
            {
                Finish(1, ex.ToString());
            }
        }

        private static void Finish(int code, string message)
        {
            if (finished) return;
            finished = true;
            try { RestorePipelines(); }
            catch (Exception ex) { Debug.Log("[FoodTrucksBuild] Restoring the HDRP assets failed (tools/build-mod.sh restores the files): " + ex); }
            Debug.Log($"[FoodTrucksBuild] {(code == 0 ? "OK" : "FAILED")}: {message}");
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }

        private static void KeepForwardPasses()
        {
            var assets = new List<RenderPipelineAsset?> { GraphicsSettings.defaultRenderPipeline };
            for (var i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (var asset in assets.Where(a => a != null).Distinct())
            {
                var serialized = new SerializedObject(asset);
                var mode = serialized.FindProperty(LitShaderMode)
                           ?? throw new InvalidOperationException($"{asset!.name} has no {LitShaderMode}.");
                if (mode.intValue == BothLitShaderModes) continue;
                var was = mode.intValue;
                changedPipelines.Add((asset!, was));
                mode.intValue = BothLitShaderModes;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[FoodTrucksBuild] {asset!.name}: lit shader mode {was} -> {BothLitShaderModes} for this build (Forward passes kept).");
            }
        }

        private static void RestorePipelines()
        {
            foreach (var (asset, mode) in changedPipelines)
            {
                if (asset == null) continue;
                var serialized = new SerializedObject(asset);
                serialized.FindProperty(LitShaderMode).intValue = mode;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // Not saved: the change was never written, and saving would reserialize the SDK's settings files.
            changedPipelines.Clear();
        }

        private static string? ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
