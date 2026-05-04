using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;

public class ShaderCompatibilityFixer : EditorWindow
{
    // ─── Data ────────────────────────────────────────────────────────────────

    private enum FixType { WrapIfGuard, ReplaceSignature }

    private class FixRule
    {
        public string Name;
        public string Description;
        public FixType Type;
        public bool Enabled;

        // For WrapIfGuard: just wrap the line in #if UNITY_VERSION >= threshold
        public string MatchKeyword;

        // For ReplaceSignature: replace old pattern with if/else block
        public string OldPattern;
        public string NewPattern; // supports {INDENT} token

        public int MinVersionThreshold = 60000000;
    }

    private static readonly List<FixRule> AllRules = new List<FixRule>
    {
        // ── Group A: Missing includes ────────────────────────────────────────
        new FixRule
        {
            Name        = "DebugMipmapStreamingMacros.hlsl",
            Description = "Core RP 17+ only. Missing in URP 14 (Unity 2022).",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
        },
        new FixRule
        {
            Name        = "ProbeVolumeVariants.hlsl",
            Description = "APV system. URP 17+ only.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
        },
        new FixRule
        {
            Name        = "MotionVectorsCommon.hlsl",
            Description = "Motion Vectors refactor. URP 17+ only.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
        },

        // ── Group B: Undeclared identifiers ──────────────────────────────────
        new FixRule
        {
            Name        = "ApplyShadowClamping",
            Description = "Shadow edge quality function. URP 17+ only.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "ApplyShadowClamping"
        },
        new FixRule
        {
            Name        = "CalcNdcMotionVectorFromCsPositions",
            Description = "Motion vector utility. URP 17+ only.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "CalcNdcMotionVectorFromCsPositions"
        },
        new FixRule
        {
            Name        = "ApplyMotionVectorZBias",
            Description = "Motion vector depth bias. URP 17+ only.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "ApplyMotionVectorZBias"
        },
        new FixRule
        {
            Name        = "probeOcclusion field",
            Description = "APV struct field. URP 17+ only. Metal compiler will error first.",
            Type        = FixType.WrapIfGuard,
            Enabled     = true,
            MatchKeyword = "probeOcclusion"
        },

        // ── Group C: API signature change — OUTPUT_SH / OUTPUT_SH4 ──────────
        new FixRule
        {
            Name        = "OUTPUT_SH4 → if/else OUTPUT_SH",
            Description = "OUTPUT_SH4 (URP17, 5 args) vs OUTPUT_SH (URP14, 2 args). Replaces call with proper version branch.",
            Type        = FixType.ReplaceSignature,
            Enabled     = true,
            // Match any line that calls OUTPUT_SH4 (but NOT already inside a version guard)
            MatchKeyword = "OUTPUT_SH4",
            // {INDENT} = original indentation, {LINE} = trimmed original call
            NewPattern =
@"{INDENT}#if UNITY_VERSION >= 60000000
{INDENT}    {LINE}
{INDENT}#else
{INDENT}    OUTPUT_SH( ase_worldNormal, output.lightmapUVOrVertexSH.xyz );
{INDENT}#endif"
        },
    };

    // ─── State ───────────────────────────────────────────────────────────────

    private string _targetFolder = "Assets";
    private string[] _extensions = { "*.shader", "*.hlsl", "*.cginc" };
    private Vector2 _scrollRules;
    private Vector2 _scrollLog;
    private string _logText = "";
    private bool _hasRun = false;
    private int _fixedFiles;
    private int _fixedOccurrences;
    private int _skipped;
    private bool _showLog = false;

    // Per-rule result summary
    private Dictionary<string, int> _ruleHits = new Dictionary<string, int>();

    // ─── Window ──────────────────────────────────────────────────────────────

    [MenuItem("Tools/Shader Compatibility Fixer")]
    public static void Open()
    {
        var w = GetWindow<ShaderCompatibilityFixer>("Shader Fixer");
        w.minSize = new Vector2(540, 640);
    }

    // ─── GUI ─────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        DrawHeader();
        DrawFolderPicker();
        DrawRules();
        DrawActions();
        if (_hasRun) DrawResults();
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(8);
        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleCenter
        };
        EditorGUILayout.LabelField("⚙  Shader Compatibility Fixer", titleStyle, GUILayout.Height(24));

        var subStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.LabelField("Unity 2022 ↔ Unity 6  |  URP 14 ↔ URP 17", subStyle);
        EditorGUILayout.Space(6);
        DrawHRule();
    }

    private void DrawFolderPicker()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _targetFolder = EditorGUILayout.TextField(_targetFolder);
        if (GUILayout.Button("Browse…", GUILayout.Width(70)))
        {
            string chosen = EditorUtility.OpenFolderPanel("Select shader folder", _targetFolder, "");
            if (!string.IsNullOrEmpty(chosen))
            {
                // Convert absolute path → relative to project
                if (chosen.StartsWith(Application.dataPath))
                    chosen = "Assets" + chosen.Substring(Application.dataPath.Length);
                _targetFolder = chosen;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Scans all .shader / .hlsl / .cginc files recursively inside this folder.",
            MessageType.Info);
        DrawHRule();
    }

    private void DrawRules()
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Fix Rules", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("All", GUILayout.Width(40)))  SetAll(true);
        if (GUILayout.Button("None", GUILayout.Width(44))) SetAll(false);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        _scrollRules = EditorGUILayout.BeginScrollView(_scrollRules, GUILayout.MaxHeight(260));

        string currentGroup = "";
        foreach (var rule in AllRules)
        {
            string group = GetGroup(rule);
            if (group != currentGroup)
            {
                currentGroup = group;
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(group, EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.BeginHorizontal(GetRowStyle(rule));

            rule.Enabled = EditorGUILayout.Toggle(rule.Enabled, GUILayout.Width(18));

            // Colored type badge
            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = rule.Type == FixType.WrapIfGuard
                    ? new Color(0.3f, 0.8f, 0.4f)
                    : new Color(1f, 0.75f, 0.2f) },
                fontStyle = FontStyle.Bold
            };
            string badge = rule.Type == FixType.WrapIfGuard ? "[WRAP]" : "[REPLACE]";
            EditorGUILayout.LabelField(badge, badgeStyle, GUILayout.Width(72));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(rule.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(rule.Description, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
        DrawHRule();
    }

    private void DrawActions()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("▶  Run Fix", GUILayout.Height(36)))
        {
            RunFix(dryRun: false);
        }

        GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
        if (GUILayout.Button("🔍  Dry Run (scan only)", GUILayout.Height(36), GUILayout.Width(180)))
        {
            RunFix(dryRun: true);
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);
    }

    private void DrawResults()
    {
        DrawHRule();
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

        // Summary cards
        EditorGUILayout.BeginHorizontal();
        DrawCard("Files Fixed",       _fixedFiles.ToString(),       new Color(0.3f, 0.8f, 0.4f));
        DrawCard("Occurrences Fixed", _fixedOccurrences.ToString(), new Color(0.3f, 0.7f, 1f));
        DrawCard("Already Guarded",   _skipped.ToString(),          new Color(0.8f, 0.8f, 0.3f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // Per-rule breakdown
        if (_ruleHits.Count > 0)
        {
            EditorGUILayout.LabelField("Breakdown by Rule", EditorStyles.miniBoldLabel);
            foreach (var kv in _ruleHits)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("  • " + kv.Key, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(kv.Value + " fix(es)", EditorStyles.miniLabel, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space(4);
        }

        // Log toggle
        _showLog = EditorGUILayout.Foldout(_showLog, "Full Log", true);
        if (_showLog)
        {
            _scrollLog = EditorGUILayout.BeginScrollView(_scrollLog, GUILayout.Height(180));
            EditorGUILayout.TextArea(_logText, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy Log to Clipboard", GUILayout.Height(24)))
                EditorGUIUtility.systemCopyBuffer = _logText;
        }
    }

    // ─── Fix Logic ───────────────────────────────────────────────────────────

    private void RunFix(bool dryRun)
    {
        _fixedFiles       = 0;
        _fixedOccurrences = 0;
        _skipped          = 0;
        _ruleHits.Clear();

        var log = new StringBuilder();
        log.AppendLine(dryRun
            ? "=== DRY RUN — no files will be modified ===\n"
            : "=== FIX RUN ===\n");

        if (!Directory.Exists(_targetFolder))
        {
            _logText = $"ERROR: Folder not found: {_targetFolder}";
            _hasRun  = true;
            Repaint();
            return;
        }

        var activeRules = AllRules.Where(r => r.Enabled).ToList();
        if (activeRules.Count == 0)
        {
            _logText = "No rules selected.";
            _hasRun  = true;
            Repaint();
            return;
        }

        // Collect files
        var allFiles = new List<string>();
        foreach (var ext in _extensions)
            allFiles.AddRange(Directory.GetFiles(_targetFolder, ext, SearchOption.AllDirectories));

        log.AppendLine($"Scanning {allFiles.Count} file(s) in '{_targetFolder}'\n");

        foreach (string filePath in allFiles)
        {
            string[] lines = File.ReadAllLines(filePath);
            bool fileModified = false;
            var fileLog = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//")) continue;

                foreach (var rule in activeRules)
                {
                    if (!trimmed.Contains(rule.MatchKeyword)) continue;

                    // Check already guarded (look 1-2 lines above)
                    bool guarded =
                        (i > 0 && lines[i - 1].Trim().StartsWith("#if UNITY_VERSION")) ||
                        (i > 1 && lines[i - 2].Trim().StartsWith("#if UNITY_VERSION"));

                    if (guarded)
                    {
                        _skipped++;
                        fileLog.AppendLine($"  ⏭ line {i + 1}: already guarded [{rule.Name}]");
                        continue;
                    }

                    string indent = GetIndent(lines[i]);
                    string replacement = BuildReplacement(rule, trimmed, indent);

                    if (!dryRun) lines[i] = replacement;

                    fileModified = true;
                    _fixedOccurrences++;

                    if (!_ruleHits.ContainsKey(rule.Name)) _ruleHits[rule.Name] = 0;
                    _ruleHits[rule.Name]++;

                    fileLog.AppendLine($"  ✅ line {i + 1}: [{rule.Name}]");
                    break; // one rule per line
                }
            }

            if (fileModified)
            {
                if (!dryRun)
                    File.WriteAllText(filePath, string.Join("\n", lines));

                _fixedFiles++;
                string fname = Path.GetFileName(filePath);
                log.AppendLine($"📄 {fname}");
                log.Append(fileLog);
                log.AppendLine();
            }
        }

        log.AppendLine("─────────────────────────────────");
        log.AppendLine($"Files {(dryRun ? "found" : "fixed")} : {_fixedFiles}");
        log.AppendLine($"Occurrences      : {_fixedOccurrences}");
        log.AppendLine($"Already guarded  : {_skipped}");

        _logText = log.ToString();
        _hasRun  = true;

        if (!dryRun && _fixedFiles > 0)
            AssetDatabase.Refresh();

        Repaint();
    }

    private string BuildReplacement(FixRule rule, string trimmedLine, string indent)
    {
        if (rule.Type == FixType.WrapIfGuard)
        {
            return
                $"{indent}#if UNITY_VERSION >= {rule.MinVersionThreshold}\n" +
                $"{indent}    {trimmedLine}\n" +
                $"{indent}#endif";
        }
        else // ReplaceSignature
        {
            return rule.NewPattern
                .Replace("{INDENT}", indent)
                .Replace("{LINE}", trimmedLine);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GetIndent(string line)
        => line.Substring(0, line.Length - line.TrimStart().Length);

    private static string GetGroup(FixRule rule)
    {
        if (rule.MatchKeyword.Contains(".hlsl")) return "Group A — Missing Include Files";
        if (rule.Type == FixType.ReplaceSignature)  return "Group C — API Signature Changes";
        return "Group B — Undeclared Identifiers / Fields";
    }

    private static void SetAll(bool value)
    {
        foreach (var r in AllRules) r.Enabled = value;
    }

    private static void DrawHRule()
    {
        var rect = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
    }

    private static GUIStyle GetRowStyle(FixRule rule)
    {
        var s = new GUIStyle();
        s.padding = new RectOffset(4, 4, 3, 3);
        s.margin  = new RectOffset(0, 0, 1, 1);
        if (!rule.Enabled)
            s.normal.background = MakeTex(new Color(0.2f, 0.2f, 0.2f, 0.1f));
        return s;
    }

    private static void DrawCard(string label, string value, Color color)
    {
        var cardStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(8, 8, 8, 8)
        };
        EditorGUILayout.BeginVertical(cardStyle);
        var valStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 22,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = color }
        };
        EditorGUILayout.LabelField(value, valStyle, GUILayout.Height(28));
        EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.EndVertical();
    }

    private static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, col);
        t.Apply();
        return t;
    }
}
