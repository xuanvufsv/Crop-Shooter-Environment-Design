using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Hierarchy Analyzer — Editor tool to analyze all GameObjects in the loaded scene.
/// Features: filter by name, static flags, layer, tag, component type.
/// Displays per-object details and aggregate statistics including static flag breakdown.
/// Usage: Tools -> Hierarchy Analyzer
/// </summary>
public class HierarchyAnalyzer : EditorWindow
{
    // -------------------------------------------------------------------------
    // Data
    // -------------------------------------------------------------------------
    private List<GOData> allObjects      = new List<GOData>();
    private List<GOData> filteredObjects = new List<GOData>();

    // -------------------------------------------------------------------------
    // Filter state
    // -------------------------------------------------------------------------
    private string       searchName            = "";
    private FilterStatic filterStatic          = FilterStatic.All;
    private string       filterLayer           = "All";
    private string       filterTag             = "All";
    private string       filterComponent       = "";
    private bool         filterHasMeshRenderer = false;
    private bool         filterHasCollider      = false;
    private bool         filterHasRigidbody     = false;

    // Static flag quick-filter checkboxes (one per flag)
    private bool filterContributeGI         = false;
    private bool filterOccluderStatic       = false;
    private bool filterOccludeeStatic       = false;
    private bool filterBatchingStatic       = false;
    // private bool filterNavigationStatic     = false;
    // private bool filterOffMeshLinkGeneration = false;
    private bool filterReflectionProbeStatic = false;

    // -------------------------------------------------------------------------
    // UI state
    // -------------------------------------------------------------------------
    private Vector2 scrollPos;
    private Vector2 scrollStats;
    private GOData  selectedObject;
    private bool    showStats      = true;
    private bool    showFilters    = true;
    private bool    showFlagFilter = false;

    // -------------------------------------------------------------------------
    // Aggregated statistics
    // -------------------------------------------------------------------------
    private int totalStatic;
    private int totalNonStatic;
    private Dictionary<string, int> layerCount     = new Dictionary<string, int>();
    private Dictionary<string, int> tagCount       = new Dictionary<string, int>();
    private Dictionary<string, int> componentCount = new Dictionary<string, int>();
    private Dictionary<string, int> shaderCount    = new Dictionary<string, int>();

    // Per static-flag counts (how many objects have each flag set)
    private Dictionary<StaticEditorFlags, int> staticFlagCount = new Dictionary<StaticEditorFlags, int>();

    // All static flags we care about, in display order
    private static readonly StaticEditorFlags[] ALL_FLAGS = new[]
    {
        StaticEditorFlags.ContributeGI,
        StaticEditorFlags.OccluderStatic,
        StaticEditorFlags.OccludeeStatic,
        StaticEditorFlags.BatchingStatic,
        // StaticEditorFlags.NavigationStatic,
        // StaticEditorFlags.OffMeshLinkGeneration,
        StaticEditorFlags.ReflectionProbeStatic
    };

    // -------------------------------------------------------------------------
    // Enums
    // -------------------------------------------------------------------------
    private enum FilterStatic { All, StaticOnly, NonStaticOnly }

    // -------------------------------------------------------------------------
    // Internal data model for a single GameObject
    // -------------------------------------------------------------------------
    private class GOData
    {
        public GameObject        go;
        public string            name;
        public bool              isStatic;
        public StaticEditorFlags staticFlags;
        public string            layer;
        public string            tag;
        public int               depth;
        public List<string>      components = new List<string>();
        public List<string>      materials  = new List<string>();
        public List<string>      shaders    = new List<string>();
        public string            meshName;
        public bool              hasRenderer;
        public bool              hasCollider;
        public bool              hasRigidbody;
        public bool              hasLOD;
        public bool              active;
    }

    // -------------------------------------------------------------------------
    // Window entry point
    // -------------------------------------------------------------------------
    [MenuItem("Tools/Hierarchy Analyzer")]
    public static void OpenWindow()
    {
        var window = GetWindow<HierarchyAnalyzer>("Hierarchy Analyzer");
        window.minSize = new Vector2(960, 620);
        window.Refresh();
        Debug.Log("[HierarchyAnalyzer] Window opened.");
    }

    void OnEnable() => Refresh();

    // -------------------------------------------------------------------------
    // Collect data from all loaded scene GameObjects
    // -------------------------------------------------------------------------
    void Refresh()
    {
        allObjects.Clear();
        layerCount.Clear();
        tagCount.Clear();
        componentCount.Clear();
        shaderCount.Clear();
        staticFlagCount.Clear();

        totalStatic    = 0;
        totalNonStatic = 0;

        // Initialize flag counters
        foreach (var flag in ALL_FLAGS)
            staticFlagCount[flag] = 0;

        // Collect all GameObjects belonging to a loaded scene
        var allGOs = Resources.FindObjectsOfTypeAll<GameObject>()
            .Where(g => g.scene.isLoaded && !string.IsNullOrEmpty(g.scene.name))
            .ToList();

        foreach (var go in allGOs)
        {
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);

            var data = new GOData
            {
                go          = go,
                name        = go.name,
                isStatic    = go.isStatic,
                staticFlags = flags,
                layer       = LayerMask.LayerToName(go.layer),
                tag         = go.tag,
                active      = go.activeInHierarchy,
                depth       = GetDepth(go.transform)
            };

            // -- Components
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                data.components.Add(typeName);
                if (!componentCount.ContainsKey(typeName)) componentCount[typeName] = 0;
                componentCount[typeName]++;
            }

            // -- Materials and shaders from MeshRenderer
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                data.hasRenderer = true;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    data.materials.Add(mat.name);
                    if (mat.shader != null)
                    {
                        string shaderName = mat.shader.name;
                        data.shaders.Add(shaderName);
                        if (!shaderCount.ContainsKey(shaderName)) shaderCount[shaderName] = 0;
                        shaderCount[shaderName]++;
                    }
                }
            }

            // -- Mesh name
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                data.meshName = mf.sharedMesh.name;

            data.hasCollider  = go.GetComponent<Collider>()  != null;
            data.hasRigidbody = go.GetComponent<Rigidbody>() != null;
            data.hasLOD       = go.GetComponent<LODGroup>()  != null;

            // -- Static / Dynamic counters
            if (go.isStatic) totalStatic++;
            else             totalNonStatic++;

            // -- Per static-flag counts
            foreach (var flag in ALL_FLAGS)
                if ((flags & flag) != 0) staticFlagCount[flag]++;

            // -- Layer counts
            if (!layerCount.ContainsKey(data.layer)) layerCount[data.layer] = 0;
            layerCount[data.layer]++;

            // -- Tag counts
            if (!tagCount.ContainsKey(data.tag)) tagCount[data.tag] = 0;
            tagCount[data.tag]++;

            allObjects.Add(data);
        }

        ApplyFilter();
        Debug.Log($"[HierarchyAnalyzer] Refresh complete — {allObjects.Count} objects collected.");
    }

    // -------------------------------------------------------------------------
    // Apply all active filters
    // -------------------------------------------------------------------------
    void ApplyFilter()
    {
        filteredObjects = allObjects.Where(d =>
        {
            // Name search (case-insensitive)
            if (!string.IsNullOrEmpty(searchName) &&
                !d.name.ToLower().Contains(searchName.ToLower())) return false;

            // Static flag category
            if (filterStatic == FilterStatic.StaticOnly    && !d.isStatic) return false;
            if (filterStatic == FilterStatic.NonStaticOnly &&  d.isStatic) return false;

            // Layer filter
            if (filterLayer != "All" && d.layer != filterLayer) return false;

            // Tag filter
            if (filterTag != "All" && d.tag != filterTag) return false;

            // Component name filter (partial match)
            if (!string.IsNullOrEmpty(filterComponent) &&
                !d.components.Any(c => c.ToLower().Contains(filterComponent.ToLower()))) return false;

            // Quick-toggle component filters
            if (filterHasMeshRenderer && !d.hasRenderer)  return false;
            if (filterHasCollider     && !d.hasCollider)   return false;
            if (filterHasRigidbody    && !d.hasRigidbody)  return false;

            // Static flag quick-filters — object must have ALL ticked flags set
            if (filterContributeGI          && (d.staticFlags & StaticEditorFlags.ContributeGI)          == 0) return false;
            if (filterOccluderStatic        && (d.staticFlags & StaticEditorFlags.OccluderStatic)        == 0) return false;
            if (filterOccludeeStatic        && (d.staticFlags & StaticEditorFlags.OccludeeStatic)        == 0) return false;
            if (filterBatchingStatic        && (d.staticFlags & StaticEditorFlags.BatchingStatic)        == 0) return false;
            // if (filterNavigationStatic      && (d.staticFlags & StaticEditorFlags.NavigationStatic)      == 0) return false;
            // if (filterOffMeshLinkGeneration && (d.staticFlags & StaticEditorFlags.OffMeshLinkGeneration) == 0) return false;
            if (filterReflectionProbeStatic && (d.staticFlags & StaticEditorFlags.ReflectionProbeStatic) == 0) return false;

            return true;
        }).ToList();
    }

    // -------------------------------------------------------------------------
    // GUI root
    // -------------------------------------------------------------------------
    void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.BeginHorizontal();

        // Left panel: filters + list
        EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.55f));
        DrawFilters();
        DrawObjectList();
        EditorGUILayout.EndVertical();

        // Right panel: details + statistics
        EditorGUILayout.BeginVertical();
        DrawDetailPanel();
        DrawStatsPanel();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Toolbar
    // -------------------------------------------------------------------------
    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(
            $"Hierarchy Analyzer  |  Total: {allObjects.Count}  |  Filtered: {filteredObjects.Count}",
            EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            Refresh();

        if (GUILayout.Button("Select All Filtered", EditorStyles.toolbarButton, GUILayout.Width(130)))
        {
            Selection.objects = filteredObjects.Select(d => d.go).Cast<Object>().ToArray();
            Debug.Log($"[HierarchyAnalyzer] Selected {filteredObjects.Count} filtered objects.");
        }

        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Filter panel
    // -------------------------------------------------------------------------
    void DrawFilters()
    {
        showFilters = EditorGUILayout.Foldout(showFilters, "Filters", true, EditorStyles.foldoutHeader);
        if (!showFilters) return;

        EditorGUILayout.BeginVertical("box");

        // -- Name
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Name:", GUILayout.Width(80));
        var newSearch = EditorGUILayout.TextField(searchName);
        if (newSearch != searchName) { searchName = newSearch; ApplyFilter(); }
        EditorGUILayout.EndHorizontal();

        // -- Static category
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Static:", GUILayout.Width(80));
        var newStatic = (FilterStatic)EditorGUILayout.EnumPopup(filterStatic, GUILayout.Width(130));
        if (newStatic != filterStatic) { filterStatic = newStatic; ApplyFilter(); }
        EditorGUILayout.EndHorizontal();

        // -- Layer — all 32 Unity layers (skip unnamed ones)
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Layer:", GUILayout.Width(80));
        var allLayerNames = Enumerable.Range(0, 32)
            .Select(i => LayerMask.LayerToName(i))
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();
        var layerOptions  = new[] { "All" }.Concat(allLayerNames).ToArray();
        int layerIdx      = System.Array.IndexOf(layerOptions, filterLayer);
        if (layerIdx < 0) layerIdx = 0;
        int newLayerIdx = EditorGUILayout.Popup(layerIdx, layerOptions, GUILayout.Width(130));
        if (layerOptions[newLayerIdx] != filterLayer) { filterLayer = layerOptions[newLayerIdx]; ApplyFilter(); }
        EditorGUILayout.EndHorizontal();

        // -- Tag — from UnityEditorInternal
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Tag:", GUILayout.Width(80));
        var tagOptions = new[] { "All" }.Concat(UnityEditorInternal.InternalEditorUtility.tags).ToArray();
        int tagIdx     = System.Array.IndexOf(tagOptions, filterTag);
        if (tagIdx < 0) tagIdx = 0;
        int newTagIdx = EditorGUILayout.Popup(tagIdx, tagOptions, GUILayout.Width(130));
        if (tagOptions[newTagIdx] != filterTag) { filterTag = tagOptions[newTagIdx]; ApplyFilter(); }
        EditorGUILayout.EndHorizontal();

        // -- Component name text search
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Component:", GUILayout.Width(80));
        var newComp = EditorGUILayout.TextField(filterComponent, GUILayout.Width(130));
        if (newComp != filterComponent) { filterComponent = newComp; ApplyFilter(); }
        EditorGUILayout.EndHorizontal();

        // -- Quick component toggles
        EditorGUILayout.BeginHorizontal();
        bool newMR  = GUILayout.Toggle(filterHasMeshRenderer, " MeshRenderer", GUILayout.Width(120));
        bool newCol = GUILayout.Toggle(filterHasCollider,     " Collider",     GUILayout.Width(80));
        bool newRb  = GUILayout.Toggle(filterHasRigidbody,    " Rigidbody",    GUILayout.Width(80));
        if (newMR != filterHasMeshRenderer || newCol != filterHasCollider || newRb != filterHasRigidbody)
        {
            filterHasMeshRenderer = newMR; filterHasCollider = newCol; filterHasRigidbody = newRb;
            ApplyFilter();
        }
        EditorGUILayout.EndHorizontal();

        // -- Static flags sub-filter (collapsible)
        showFlagFilter = EditorGUILayout.Foldout(showFlagFilter, "Filter by Static Flags", true);
        if (showFlagFilter)
        {
            EditorGUI.indentLevel++;
            bool changed = false;

            bool newCGI  = EditorGUILayout.ToggleLeft("Contribute GI",           filterContributeGI);
            bool newOccR = EditorGUILayout.ToggleLeft("Occluder Static",          filterOccluderStatic);
            bool newOccE = EditorGUILayout.ToggleLeft("Occludee Static",          filterOccludeeStatic);
            bool newBat  = EditorGUILayout.ToggleLeft("Batching Static",          filterBatchingStatic);
            // bool newNav  = EditorGUILayout.ToggleLeft("Navigation Static",        filterNavigationStatic);
            // bool newOML  = EditorGUILayout.ToggleLeft("Off Mesh Link Generation", filterOffMeshLinkGeneration);
            bool newRP   = EditorGUILayout.ToggleLeft("Reflection Probe Static",  filterReflectionProbeStatic);

            if (newCGI  != filterContributeGI          ||
                newOccR != filterOccluderStatic        ||
                newOccE != filterOccludeeStatic        ||
                newBat  != filterBatchingStatic        ||
                // newNav  != filterNavigationStatic      ||
                // newOML  != filterOffMeshLinkGeneration ||
                newRP   != filterReflectionProbeStatic)
            {
                filterContributeGI          = newCGI;
                filterOccluderStatic        = newOccR;
                filterOccludeeStatic        = newOccE;
                filterBatchingStatic        = newBat;
                // filterNavigationStatic      = newNav;
                // filterOffMeshLinkGeneration = newOML;
                filterReflectionProbeStatic = newRP;
                changed = true;
            }

            if (changed) ApplyFilter();
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    // -------------------------------------------------------------------------
    // Scrollable object list
    // -------------------------------------------------------------------------
    void DrawObjectList()
    {
        GUILayout.Label($"Objects ({filteredObjects.Count})", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        foreach (var data in filteredObjects)
        {
            bool  isSelected = selectedObject == data;
            Color prevBg     = GUI.backgroundColor;

            if (isSelected)        GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            else if (!data.active) GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Space(data.depth * 12);

            // Static / Dynamic badge
            GUI.color = data.isStatic ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.5f, 0.3f);
            GUILayout.Label(data.isStatic ? "S" : "D", GUILayout.Width(14));
            GUI.color = Color.white;

            // Clickable name
            if (GUILayout.Button(data.name, EditorStyles.label, GUILayout.MinWidth(120)))
            {
                selectedObject = data;
                Selection.activeGameObject = data.go;
                EditorGUIUtility.PingObject(data.go);
            }

            GUILayout.FlexibleSpace();

            // Tag (small, muted)
            GUI.color = new Color(1f, 0.85f, 0.5f);
            GUILayout.Label(data.tag, EditorStyles.miniLabel, GUILayout.Width(70));
            GUI.color = Color.white;

            // Layer
            GUI.color = new Color(0.8f, 0.8f, 1f);
            GUILayout.Label(data.layer, GUILayout.Width(70));
            GUI.color = Color.white;

            // Component badges
            if (data.hasRenderer)  DrawBadge("MR",  new Color(0.3f, 0.8f, 0.3f));
            if (data.hasCollider)  DrawBadge("COL", new Color(0.8f, 0.6f, 0.2f));
            if (data.hasRigidbody) DrawBadge("RB",  new Color(0.8f, 0.3f, 0.3f));
            if (data.hasLOD)       DrawBadge("LOD", new Color(0.5f, 0.3f, 0.8f));

            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = prevBg;
        }

        EditorGUILayout.EndScrollView();
    }

    void DrawBadge(string label, Color color)
    {
        GUI.color = color;
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(28));
        GUI.color = Color.white;
    }

    // -------------------------------------------------------------------------
    // Detail panel
    // -------------------------------------------------------------------------
    void DrawDetailPanel()
    {
        if (selectedObject == null)
        {
            EditorGUILayout.HelpBox("Select an object to view its details.", MessageType.Info);
            return;
        }

        var d = selectedObject;
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label(d.name, EditorStyles.boldLabel);

        DrawRow("Active",  d.active   ? "Yes"     : "No");
        DrawRow("Static",  d.isStatic ? "Static"  : "Dynamic");
        DrawRow("Layer",   d.layer);
        DrawRow("Tag",     d.tag);

        if (!string.IsNullOrEmpty(d.meshName))
            DrawRow("Mesh", d.meshName);

        // Individual static flags breakdown
        if (d.isStatic)
        {
            GUILayout.Label("Static Flags:", EditorStyles.miniLabel);
            EditorGUILayout.BeginVertical();
            foreach (var flag in ALL_FLAGS)
            {
                bool hasFlag = (d.staticFlags & flag) != 0;
                GUI.color = hasFlag ? new Color(0.4f, 1f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label($"  {(hasFlag ? "+" : "-")}  {FlagDisplayName(flag)}", EditorStyles.miniLabel);
            }
            GUI.color = Color.white;
            EditorGUILayout.EndVertical();
        }

        GUILayout.Label("Components:", EditorStyles.miniLabel);
        GUILayout.Label(string.Join(", ", d.components), EditorStyles.wordWrappedLabel);

        if (d.materials.Count > 0)
        {
            GUILayout.Label("Materials:", EditorStyles.miniLabel);
            GUILayout.Label(string.Join(", ", d.materials), EditorStyles.wordWrappedLabel);
        }

        if (d.shaders.Count > 0)
        {
            GUILayout.Label("Shaders:", EditorStyles.miniLabel);
            GUILayout.Label(string.Join(", ", d.shaders), EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.EndVertical();
    }

    void DrawRow(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label + ":", GUILayout.Width(80));
        GUILayout.Label(value);
        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Statistics panel
    // -------------------------------------------------------------------------
    void DrawStatsPanel()
    {
        showStats = EditorGUILayout.Foldout(showStats, "Statistics", true, EditorStyles.foldoutHeader);
        if (!showStats) return;

        scrollStats = EditorGUILayout.BeginScrollView(scrollStats, GUILayout.MaxHeight(320));
        EditorGUILayout.BeginVertical("box");

        // Static vs Dynamic
        GUILayout.Label("Static vs Dynamic", EditorStyles.boldLabel);
        DrawStatRow("Static",  totalStatic,    allObjects.Count, new Color(0.4f, 1f,   0.4f));
        DrawStatRow("Dynamic", totalNonStatic, allObjects.Count, new Color(1f,   0.5f, 0.3f));

        EditorGUILayout.Space(4);

        // Static flags breakdown — how many objects have each flag enabled
        GUILayout.Label("Static Flags Breakdown", EditorStyles.boldLabel);
        foreach (var flag in ALL_FLAGS)
            DrawStatRow(FlagDisplayName(flag), staticFlagCount[flag], totalStatic > 0 ? totalStatic : 1, new Color(0.5f, 0.9f, 0.7f));

        EditorGUILayout.Space(4);

        // By Layer
        GUILayout.Label("By Layer", EditorStyles.boldLabel);
        foreach (var kv in layerCount.OrderByDescending(k => k.Value))
            DrawStatRow(kv.Key, kv.Value, allObjects.Count, new Color(0.6f, 0.8f, 1f));

        EditorGUILayout.Space(4);

        // By Tag
        GUILayout.Label("By Tag", EditorStyles.boldLabel);
        foreach (var kv in tagCount.OrderByDescending(k => k.Value))
            DrawStatRow(kv.Key, kv.Value, allObjects.Count, new Color(1f, 0.85f, 0.5f));

        EditorGUILayout.Space(4);

        // Top 10 Components
        GUILayout.Label("Top Components", EditorStyles.boldLabel);
        foreach (var kv in componentCount.OrderByDescending(k => k.Value).Take(10))
            DrawStatRow(kv.Key, kv.Value, allObjects.Count, new Color(0.8f, 0.8f, 0.5f));

        EditorGUILayout.Space(4);

        // Top 8 Shaders
        if (shaderCount.Count > 0)
        {
            GUILayout.Label("Top Shaders", EditorStyles.boldLabel);
            foreach (var kv in shaderCount.OrderByDescending(k => k.Value).Take(8))
                DrawStatRow(kv.Key, kv.Value, allObjects.Count, new Color(0.8f, 0.5f, 0.8f));
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();
    }

    void DrawStatRow(string label, int count, int total, Color color)
    {
        EditorGUILayout.BeginHorizontal();
        GUI.color = color;
        GUILayout.Label(label, GUILayout.MinWidth(180));
        GUI.color = Color.white;
        GUILayout.Label(count.ToString(), EditorStyles.boldLabel, GUILayout.Width(40));

        Rect barRect = EditorGUILayout.GetControlRect(GUILayout.Height(12));
        float ratio  = total > 0 ? (float)count / total : 0f;
        EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
        EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height), color * 0.8f);

        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns a human-readable display name for a StaticEditorFlags value.</summary>
    string FlagDisplayName(StaticEditorFlags flag)
    {
        switch (flag)
        {
            case StaticEditorFlags.ContributeGI:          return "Contribute GI";
            case StaticEditorFlags.OccluderStatic:        return "Occluder Static";
            case StaticEditorFlags.OccludeeStatic:        return "Occludee Static";
            case StaticEditorFlags.BatchingStatic:        return "Batching Static";
            // case StaticEditorFlags.NavigationStatic:      return "Navigation Static";
            // case StaticEditorFlags.OffMeshLinkGeneration: return "Off Mesh Link Generation";
            case StaticEditorFlags.ReflectionProbeStatic: return "Reflection Probe Static";
            default:                                       return flag.ToString();
        }
    }

    /// <summary>Returns the hierarchy depth of a transform (root = 0).</summary>
    int GetDepth(Transform t)
    {
        int depth = 0;
        while (t.parent != null) { depth++; t = t.parent; }
        return depth;
    }
}