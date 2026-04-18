using Pathfinding.BehaviourTrees;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class BehaviourTreeDebuggerWindow : EditorWindow
{
    private const float NodeWidth = 108f;
    private const float NodeHeight = 64f;
    private const float HorizontalGap = 16f;
    private const float VerticalGap = 52f;
    private const float CanvasPadding = 16f;

    private BombermanAI targetAI;
    private Vector2 scroll;
    private double lastRepaintTime;
    private readonly Dictionary<Node, float> subtreeWidths = new Dictionary<Node, float>();
    private readonly Dictionary<Node, Rect> nodeRects = new Dictionary<Node, Rect>();
    private float graphWidth;
    private float graphHeight;

    [MenuItem("Tools/AI/Behavior Tree Debugger")]
    public static void OpenWindow()
    {
        BehaviourTreeDebuggerWindow window = GetWindow<BehaviourTreeDebuggerWindow>("BT Debugger");
        window.minSize = new Vector2(380f, 280f);

        Rect p = window.position;
        if (p.width > 600f)
        {
            p.width *= 0.5f;
            window.position = p;
        }
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup - lastRepaintTime > 0.1d)
        {
            lastRepaintTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawRuntimeInfo();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        targetAI = EditorGUILayout.ObjectField(
            "Target AI",
            targetAI,
            typeof(BombermanAI),
            true,
            GUILayout.MinWidth(260f)) as BombermanAI;

        if (GUILayout.Button("Find First AI", EditorStyles.toolbarButton, GUILayout.Width(90f)))
        {
            targetAI = FindFirstObjectByType<BombermanAI>();
        }

        if (GUILayout.Button("Focus Selection", EditorStyles.toolbarButton, GUILayout.Width(100f)))
        {
            if (Selection.activeGameObject != null)
            {
                targetAI = Selection.activeGameObject.GetComponent<BombermanAI>();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawRuntimeInfo()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to see runtime statuses.", MessageType.Info);
            return;
        }

        if (targetAI == null)
        {
            EditorGUILayout.HelpBox("Assign a BombermanAI target or click Find First AI.", MessageType.Warning);
            return;
        }

        BehaviourTree tree = targetAI.Tree;
        if (tree == null)
        {
            EditorGUILayout.HelpBox("Behavior tree has not been initialized yet.", MessageType.Warning);
            return;
        }

        DrawLegend();

        BuildLayout(tree);

        float viewHeight = Mathf.Max(200f, position.height - 90f);
        float viewWidth = Mathf.Max(200f, position.width - 16f);
        Rect viewportRect = GUILayoutUtility.GetRect(viewWidth, viewHeight, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        scroll = GUI.BeginScrollView(viewportRect, scroll, new Rect(0f, 0f, graphWidth, graphHeight));
        DrawConnections(tree);
        DrawNodeBoxes(tree);
        GUI.EndScrollView();

    }

    private static void DrawLegend()
    {
        EditorGUILayout.BeginHorizontal();
        DrawLegendItem("Running", Color.yellow);
        DrawLegendItem("Success", Color.green);
        DrawLegendItem("Failure", Color.red);
        DrawLegendItem("Not Tick", Color.gray);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
    }

    private static void DrawLegendItem(string label, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(70f));
        GUI.color = previous;
    }

    private void BuildLayout(Node root)
    {
        subtreeWidths.Clear();
        nodeRects.Clear();

        float rootSubtreeWidth = CalculateSubtreeWidth(root);
        graphWidth = Mathf.Max(position.width - 24f, rootSubtreeWidth + (CanvasPadding * 2f));

        float rootX = (graphWidth - rootSubtreeWidth) * 0.5f;
        float maxBottom = LayoutNodeRecursive(root, rootX, CanvasPadding);
        graphHeight = Mathf.Max(position.height - 100f, maxBottom + CanvasPadding);
    }

    private float CalculateSubtreeWidth(Node node)
    {
        if (node.children.Count == 0)
        {
            subtreeWidths[node] = NodeWidth;
            return NodeWidth;
        }

        float childrenWidth = 0f;
        for (int i = 0; i < node.children.Count; i++)
        {
            childrenWidth += CalculateSubtreeWidth(node.children[i]);
            if (i < node.children.Count - 1)
            {
                childrenWidth += HorizontalGap;
            }
        }

        float width = Mathf.Max(NodeWidth, childrenWidth);
        subtreeWidths[node] = width;
        return width;
    }

    private float LayoutNodeRecursive(Node node, float subtreeLeft, float y)
    {
        float subtreeWidth = subtreeWidths[node];
        float nodeX = subtreeLeft + (subtreeWidth - NodeWidth) * 0.5f;
        Rect nodeRect = new Rect(nodeX, y, NodeWidth, NodeHeight);
        nodeRects[node] = nodeRect;

        float maxBottom = nodeRect.yMax;
        float childSubtreeLeft = subtreeLeft;
        float childY = y + NodeHeight + VerticalGap;

        foreach (Node child in node.children)
        {
            float childWidth = subtreeWidths[child];
            float childBottom = LayoutNodeRecursive(child, childSubtreeLeft, childY);
            if (childBottom > maxBottom)
            {
                maxBottom = childBottom;
            }

            childSubtreeLeft += childWidth + HorizontalGap;
        }

        return maxBottom;
    }

    private void DrawConnections(Node node)
    {
        if (!nodeRects.TryGetValue(node, out Rect parentRect))
        {
            return;
        }

        foreach (Node child in node.children)
        {
            if (nodeRects.TryGetValue(child, out Rect childRect))
            {
                Vector3 start = new Vector3(parentRect.center.x, parentRect.yMax, 0f);
                Vector3 end = new Vector3(childRect.center.x, childRect.yMin, 0f);
                Vector3 startTangent = start + Vector3.down * 24f;
                Vector3 endTangent = end + Vector3.up * 24f;

                Handles.DrawBezier(start, end, startTangent, endTangent, new Color(0.7f, 0.7f, 0.7f, 1f), null, 2f);
                DrawConnections(child);
            }
        }
    }

    private void DrawNodeBoxes(Node node)
    {
        if (!nodeRects.TryGetValue(node, out Rect rect))
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = GetNodeColor(node);

        string statusText = node.HasLastStatus ? node.LastStatus.ToString() : "NotTicked";
        string sinceLastTick = node.HasLastStatus ? (Time.time - node.LastTickTime).ToString("0.00") + "s" : "-";

        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUI.color = previous;

        Rect titleRect = new Rect(rect.x + 5f, rect.y + 5f, rect.width - 10f, 18f);
        GUI.Label(titleRect, node.name, EditorStyles.boldLabel);

        Rect detailRect = new Rect(rect.x + 5f, rect.y + 24f, rect.width - 10f, rect.height - 28f);
        GUI.Label(detailRect, $"{statusText} | p:{node.priority}\nTicks: {node.TickCount} | Last: {sinceLastTick}", EditorStyles.miniLabel);

        foreach (Node child in node.children)
        {
            DrawNodeBoxes(child);
        }
    }

    private static Color GetNodeColor(Node node)
    {
        if (!node.HasLastStatus)
        {
            return Color.gray;
        }

        switch (node.LastStatus)
        {
            case Node.Status.Running:
                return new Color(1f, 0.85f, 0.2f, 1f);
            case Node.Status.Success:
                return new Color(0.35f, 0.95f, 0.35f, 1f);
            default:
                return new Color(1f, 0.45f, 0.45f, 1f);
        }
    }
}
