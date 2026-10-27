using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WireframeToggle : MonoBehaviour
{
    enum State { Solid, Wire, Shattering, Gone }

    [Header("Interaction")]
    public Transform player;
    public TMP_Text prompt;
    public float triggerDistance = 3f;

    [Header("Wireframe")]
    public Material wireMaterial;
    public Color wireColor = Color.green;
    public bool hideTriangleDiagonals = true;

    [Header("Shatter")]
    [Range(2, 8)] public int piecesPerAxis = 4;
    [Range(0f, 0.5f)] public float pieceGap = 0.1f;
    public bool wireframePieces = true;
    public float shatterTime = 2.5f;
    [Range(0f, 2f)] public float stagger = 0.6f;

    [Header("Tornado")]
    public float spins = 2.5f;          // revolutions around the Y axis
    public float riseHeight = 4f;
    public float radiusGrowth = 3f;
    public float tumbleSpeed = 360f;    // degrees per second
    public bool clockwise = false;

    MeshFilter mf;
    MeshRenderer mr;
    Collider col;
    Mesh solidMesh, wireMesh;
    Material[] solidMats;
    State state = State.Solid;
    bool wasInRange;

    Transform fragRoot;
    Transform[] frags;
    Vector3[] startPos, tumbleAxis;
    float[] startAngle, startRadius, delay;
    Vector3 pieceScale;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        col = GetComponent<Collider>();
        solidMesh = mf.sharedMesh;
        solidMats = mr.sharedMaterials;
        wireMesh = BuildWireMesh(solidMesh, hideTriangleDiagonals);

        if (wireMaterial == null)
        {
            wireMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            wireMaterial.color = wireColor;
        }

        BuildFragments();
        if (prompt) prompt.gameObject.SetActive(false);
    }

    void Update()
    {
        bool inRange = state != State.Shattering &&
            (player.position - transform.position).sqrMagnitude <= triggerDistance * triggerDistance;
        if (inRange != wasInRange && prompt) prompt.gameObject.SetActive(inRange);
        wasInRange = inRange;

        if (!inRange || Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame) return;

        switch (state)
        {
            case State.Solid: SetWireframe(true); break;
            case State.Wire:  StartCoroutine(Shatter()); break;
            case State.Gone:  Restore(); break;
        }
    }

    void SetWireframe(bool on)
    {
        state = on ? State.Wire : State.Solid;
        mf.sharedMesh = on ? wireMesh : solidMesh;
        mr.sharedMaterials = on ? new[] { wireMaterial } : solidMats;
    }

    void Restore()
    {
        SetWireframe(false);
        mr.enabled = true;
        if (col) col.enabled = true;
    }

    // ---------- Shatter ----------

    IEnumerator Shatter()
    {
        state = State.Shattering;
        mr.enabled = false;
        if (col) col.enabled = false;

        UpdateFragments(0f);
        fragRoot.gameObject.SetActive(true);

        for (float t = 0f; t < shatterTime; t += Time.deltaTime)
        {
            UpdateFragments(t / shatterTime);
            yield return null;
        }

        fragRoot.gameObject.SetActive(false);
        state = State.Gone;
    }

    void UpdateFragments(float k)
    {
        float dir = clockwise ? -1f : 1f;

        for (int i = 0; i < frags.Length; i++)
        {
            float p = Mathf.Clamp01(k * (1f + stagger) - delay[i] * stagger);
            float e = p * p; // ease-in: slow pickup, then accelerating

            float angle  = startAngle[i] + dir * spins * 2f * Mathf.PI * e;
            float radius = startRadius[i] + radiusGrowth * e;
            float height = startPos[i].y + riseHeight * e;

            frags[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            frags[i].localRotation = Quaternion.AngleAxis(tumbleSpeed * shatterTime * p, tumbleAxis[i]);

            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, p));
            frags[i].localScale = pieceScale * (1f - shrink);
        }
    }

    void BuildFragments()
    {
        fragRoot = new GameObject("Fragments").transform;
        fragRoot.SetParent(transform, false);

        int n = piecesPerAxis;
        int count = n * n * n;
        frags = new Transform[count];
        startPos = new Vector3[count];
        tumbleAxis = new Vector3[count];
        startAngle = new float[count];
        startRadius = new float[count];
        delay = new float[count];

        Bounds b = solidMesh.bounds;
        Vector3 cell = new Vector3(b.size.x / n, b.size.y / n, b.size.z / n);
        pieceScale = Vector3.one * (1f - pieceGap) / n;

        Mesh fragMesh = wireframePieces ? wireMesh : solidMesh;
        Material[] fragMats = wireframePieces ? new[] { wireMaterial } : solidMats;

        int i = 0;
        for (int x = 0; x < n; x++)
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++, i++)
        {
            var go = new GameObject($"Frag_{i}");
            go.transform.SetParent(fragRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = fragMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = fragMats;
            frags[i] = go.transform;

            Vector3 p = b.min + Vector3.Scale(cell, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
            startPos[i] = p;
            startAngle[i] = Mathf.Atan2(p.z, p.x);
            startRadius[i] = new Vector2(p.x, p.z).magnitude;

            // Top layers leave first, with some randomness
            delay[i] = (1f - (y + 0.5f) / n) * 0.5f + Random.value * 0.5f;
            tumbleAxis[i] = Random.onUnitSphere;
        }

        fragRoot.gameObject.SetActive(false);
    }

    // ---------- Wireframe mesh ----------

    static Mesh BuildWireMesh(Mesh src, bool hideDiagonals)
    {
        Vector3[] v = src.vertices;
        int[] t = src.triangles;

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