using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Batch-mode entry points. Example:
    /// <code>
    /// Unity -batchmode -nographics -quit -projectPath &lt;project&gt;
    ///       -executeMethod UISpriteMaker.Editor.UISpriteMakerCli.Bake
    ///       -spec sprites.json [-preview &lt;dir&gt;] [-validateOnly]
    /// </code>
    /// The spec file holds one spec, an array of specs, or { "sprites": [ ... ] }.
    /// Every spec needs an "output" asset path (unless -validateOnly or only previewing).
    /// Output lines are prefixed with "[UISpriteMaker]". Exit code 0 = success, 1 = error.
    /// </summary>
    public static class UISpriteMakerCli
    {
        const string LogPrefix = "[UISpriteMaker]";

        public static void Bake()
        {
            int code = 1;
            try
            {
                code = Run(Environment.GetCommandLineArgs()) ? 0 : 1;
            }
            catch (Exception e)
            {
                LogError($"ERROR {e.Message}");
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        /// <summary>Prints the spec of each sprite given by -asset (repeatable).</summary>
        public static void Describe()
        {
            int code = 0;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-asset") continue;
                string spec = UISpriteMakerApi.GetSpec(args[i + 1]);
                if (spec == null)
                {
                    LogError($"ERROR {args[i + 1]}: not a sprite made by UI Sprite Maker");
                    code = 1;
                }
                else
                {
                    Log($"SPEC {args[i + 1]}\n{spec}");
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        internal static bool Run(string[] args)
        {
            string specPath = GetArg(args, "-spec") ?? throw new ArgumentException("Missing -spec <file.json>");
            string previewDir = GetArg(args, "-preview");
            bool validateOnly = Array.IndexOf(args, "-validateOnly") >= 0;

            var items = ReadSpecs(File.ReadAllText(specPath, Encoding.UTF8));
            bool ok = true;

            var baked = new List<(string path, UISpriteStyle style)>();
            for (int i = 0; i < items.Count; i++)
            {
                string itemPath = $"sprites[{i}]";
                UISpriteStyle style = null;
                try
                {
                    var obj = SpriteSpec.AsObject(items[i], itemPath, SpriteSpec.RootKeys);
                    string output = obj.TryGetValue("output", out var outValue) ? outValue as string : null;
                    if (output == null && !validateOnly && previewDir == null)
                        throw new SpriteSpecException($"{itemPath}.output: required, e.g. \"output\": \"Assets/UI/Button.png\"");

                    style = SpriteSpec.FromObject(obj, itemPath);
                    if (validateOnly)
                    {
                        Log($"VALID {output ?? itemPath}");
                        continue;
                    }

                    if (previewDir != null)
                    {
                        string name = output != null ? Path.GetFileNameWithoutExtension(output) : $"sprite_{i}";
                        WritePreview(style, Path.Combine(previewDir, name + ".png"));
                    }

                    if (output != null)
                    {
                        baked.Add((output, style));
                        style = null; // ownership moves to the bake list
                    }
                }
                catch (SpriteSpecException e)
                {
                    LogError($"ERROR {e.Message}");
                    ok = false;
                }
                finally
                {
                    if (style != null) UnityEngine.Object.DestroyImmediate(style);
                }
            }

            foreach (var (path, style) in baked)
            {
                try
                {
                    var layout = UISpriteMakerApi.Bake(style, path);
                    var b = layout.Border;
                    Log($"OK {path} {layout.Width}x{layout.Height}px @{layout.Scale}x border(L{b.x} B{b.y} R{b.z} T{b.w})");
                }
                catch (Exception e)
                {
                    LogError($"ERROR {path}: {e.Message}");
                    ok = false;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(style);
                }
            }
            return ok;
        }

        static List<object> ReadSpecs(string json)
        {
            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (FormatException e)
            {
                throw new SpriteSpecException(e.Message);
            }

            return root switch
            {
                List<object> list => list,
                Dictionary<string, object> obj when obj.TryGetValue("sprites", out var s) && s is List<object> list => list,
                Dictionary<string, object> obj => new List<object> { obj },
                _ => throw new SpriteSpecException("$: expected a spec object, an array of specs, or { \"sprites\": [...] }"),
            };
        }

        static void WritePreview(UISpriteStyle style, string filePath)
        {
            var result = SpriteRasterizer.Render(style);
            var tex = result.ToTexture();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));
                File.WriteAllBytes(filePath, tex.EncodeToPNG());
                Log($"PREVIEW {filePath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // No stack traces: keeps batch-mode logs easy to grep for "[UISpriteMaker]".
        static void Log(string message) =>
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0} {1}", LogPrefix, message);

        static void LogError(string message) =>
            Debug.LogFormat(LogType.Error, LogOption.NoStacktrace, null, "{0} {1}", LogPrefix, message);

        static string GetArg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
