using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WireframeToggle : MonoBehaviour
{
    public Transform player;
    public TMP_Text prompt;
    public float triggerDistance = 3f;
    public Material wireMaterial;
    public bool hideTriangleDiagonals = true;

    MeshFilter mf;
    MeshRenderer mr;
    Mesh solidMesh, wireMesh;
    Material[] solidMats;
    bool isWire;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        solidMesh = mf.sharedMesh;
        solidMats = mr.sharedMaterials;
        wireMesh = BuildWireMesh(solidMesh, hideTriangleDiagonals);

        if (wireMaterial == null)
        {
            wireMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wireMaterial.color = Color.green;
        }
        if (prompt) prompt.gameObject.SetActive(false);
    }

    void Update()
    {
        bool inRange = Vector3.Distance(player.position, transform.position) <= triggerDistance;
        if (prompt) prompt.gameObject.SetActive(inRange);

        if (inRange && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            SetWireframe(!isWire);
    }

    void SetWireframe(bool on)
    {
        isWire = on;
        mf.sharedMesh = on ? wireMesh : solidMesh;
        mr.sharedMaterials = on ? new[] { wireMaterial } : solidMats;
    }

    static Mesh BuildWireMesh(Mesh src, bool hideDiagonals)
    {
        Vector3[] v = src.vertices;
        int[] t = src.triangles;

        // Weld duplicate vertices so adjacent faces share edges
        var weld = new Dictionary<Vector3, int>();
        var pos = new List<Vector3>();
        int[] remap = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            if (!weld.TryGetValue(v[i], out int idx))
            {
                idx = pos.Count;
                pos.Add(v[i]);
                weld[v[i]] = idx;
            }
            remap[i] = idx;
        }

        // Edge -> normals of triangles touching it
        var edges = new Dictionary<(int, int), List<Vector3>>();
        for (int i = 0; i < t.Length; i += 3)
        {
            int a = remap[t[i]], b = remap[t[i + 1]], c = remap[t[i + 2]];
            Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]).normalized;
            AddEdge(edges, a, b, n);
            AddEdge(edges, b, c, n);
            AddEdge(edges, c, a, n);
        }

        var lines = new List<int>();
        foreach (var kv in edges)
        {
            var ns = kv.Value;
            bool coplanar = ns.Count == 2 && Vector3.Dot(ns[0], ns[1]) > 0.999f;
            if (hideDiagonals && coplanar) continue;
            lines.Add(kv.Key.Item1);
            lines.Add(kv.Key.Item2);
        }

        var m = new Mesh { name = src.name + "_Wire" };
        m.SetVertices(pos);
        m.SetIndices(lines, MeshTopology.Lines, 0);
        m.RecalculateBounds();
        return m;
    }

    static void AddEdge(Dictionary<(int, int), List<Vector3>> e, int a, int b, Vector3 n)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!e.TryGetValue(key, out var list)) e[key] = list = new List<Vector3>();
        list.Add(n);
    }
}