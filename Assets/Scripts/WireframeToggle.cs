using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WireframeToggle : MonoBehaviour
{
    enum State { Solid, Wire, Animating }

    [Header("Interaction")]
    public Transform player;
    public TMP_Text prompt;
    public float triggerDistance = 3f;

    [Header("Wireframe")]
    public Material wireMaterial;
    public bool hideTriangleDiagonals = true;

    [Header("Point Cloud")]
    public Material pointMaterial;
    public int pointCount = 3000;
    public float pointSize = 0.03f;
    public Color pointColor = Color.green;
    public float scatterRadius = 2f;
    [Range(0f, 2f)] public float stagger = 0.5f;
    public float scatterTime = 1.2f;
    public float holdTime = 0.5f;
    public float gatherTime = 1.2f;

    MeshFilter mf;
    MeshRenderer mr;
    Mesh solidMesh, wireMesh;
    Material[] solidMats;
    State state = State.Solid;

    ParticleSystem ps;
    ParticleSystem.Particle[] particles;
    Vector3[] homePos, scatterPos;
    float[] delay;
    bool wasInRange;

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
            wireMaterial.color = pointColor;
        }

        BuildPointCloud();
        if (prompt) prompt.gameObject.SetActive(false);
    }

    void Update()
    {
        bool inRange = state != State.Animating &&
            (player.position - transform.position).sqrMagnitude <= triggerDistance * triggerDistance;
        if (inRange != wasInRange && prompt) prompt.gameObject.SetActive(inRange);
        wasInRange = inRange;

        if (!inRange || Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame) return;

        if (state == State.Solid) SetWireframe(true);
        else if (state == State.Wire) StartCoroutine(ScatterAndGather());
    }

    void SetWireframe(bool on)
    {
        state = on ? State.Wire : State.Solid;
        mf.sharedMesh = on ? wireMesh : solidMesh;
        mr.sharedMaterials = on ? new[] { wireMaterial } : solidMats;
    }

    // ---------- Point cloud animation ----------

    IEnumerator ScatterAndGather()
    {
        state = State.Animating;
        mr.enabled = false;
        ShowPoints();

        yield return Animate(0f, 1f, scatterTime);
        yield return new WaitForSeconds(holdTime);
        yield return Animate(1f, 0f, gatherTime);

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        SetWireframe(false);
        mr.enabled = true;
    }

    IEnumerator Animate(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            ApplyPositions(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        ApplyPositions(to);
    }

    void ApplyPositions(float k)
    {
        for (int i = 0; i < pointCount; i++)
        {
            float p = Mathf.Clamp01(k * (1f + stagger) - delay[i] * stagger);
            p = p * p * (3f - 2f * p); // smoothstep
            particles[i].position = Vector3.Lerp(homePos[i], scatterPos[i], p);
        }
        ps.SetParticles(particles, pointCount);
    }

    void ShowPoints()
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play();
        var ep = new ParticleSystem.EmitParams
        {
            startSize = pointSize,
            startColor = pointColor,
            startLifetime = 9999f,
            velocity = Vector3.zero,
            applyShapeToPosition = false
        };
        ps.Emit(ep, pointCount);
        ps.GetParticles(particles, pointCount);
        ApplyPositions(0f);
    }

    void BuildPointCloud()
    {
        var go = new GameObject("PointCloud");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = pointCount;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;

        if (pointMaterial == null)
            pointMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = pointMaterial;

        particles = new ParticleSystem.Particle[pointCount];
        homePos = SampleSurface(solidMesh, pointCount);
        scatterPos = new Vector3[pointCount];
        delay = new float[pointCount];

        Vector3 center = solidMesh.bounds.center;
        for (int i = 0; i < pointCount; i++)
        {
            Vector3 dir = homePos[i] - center;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Random.onUnitSphere;
            scatterPos[i] = homePos[i] +
                (dir * Random.Range(0.5f, 1f) + Random.insideUnitSphere * 0.5f) * scatterRadius;
            delay[i] = Random.value;
        }
    }

    // Area-weighted random points on the mesh surface
    static Vector3[] SampleSurface(Mesh m, int count)
    {
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        int triCount = t.Length / 3;

        var cumArea = new float[triCount];
        float total = 0f;
        for (int i = 0; i < triCount; i++)
        {
            Vector3 a = v[t[i * 3]], b = v[t[i * 3 + 1]], c = v[t[i * 3 + 2]];
            total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumArea[i] = total;
        }

        var pts = new Vector3[count];
        for (int j = 0; j < count; j++)
        {
            int tri = System.Array.BinarySearch(cumArea, Random.value * total);
            if (tri < 0) tri = ~tri;
            tri = Mathf.Min(tri, triCount - 1);

            Vector3 a = v[t[tri * 3]], b = v[t[tri * 3 + 1]], c = v[t[tri * 3 + 2]];
            float u = Random.value, w = Random.value;
            if (u + w > 1f) { u = 1f - u; w = 1f - w; }
            pts[j] = a + u * (b - a) + w * (c - a);
        }
        return pts;
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