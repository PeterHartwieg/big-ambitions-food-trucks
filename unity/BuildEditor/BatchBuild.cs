#nullable enable
using System;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEngine;

namespace FoodTrucks.BuildEditor
{
    // Entry point for `Unity -batchmode -executeMethod FoodTrucks.BuildEditor.BatchBuild.Build -modId <id>`.
    // Runs the SDK's own validator and packager, then exits with 0 on success and 1 on failure.
    public static class BatchBuild
    {
        public static void Build()
        {
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
                ModPackager.Enqueue(mod, installAfterBuild: false, revealWhenDone: false);
            }
            catch (Exception ex)
            {
                Finish(1, ex.ToString());
            }
        }

        private static void Finish(int code, string message)
        {
            Debug.Log($"[FoodTrucksBuild] {(code == 0 ? "OK" : "FAILED")}: {message}");
            EditorApplication.delayCall += () => EditorApplication.Exit(code);
        }

        private static string? ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
