//
// Lattice - An example of vertex animation with C# Job System and New Mesh API
// https://github.com/keijiro/VertexAnimationJob
//

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[ExecuteInEditMode]
public sealed class Lattice : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] int2 _resolution = math.int2(128, 128);
    [SerializeField] float2 _extent = math.float2(10, 10);
    [SerializeField] int _noiseRepeat = 6;
    [SerializeField] float _noiseAmplitude = 0.1f;
    [SerializeField] float _noiseAnimation = 0.5f;
    [SerializeField] Material _material = null;

    void OnValidate()
    {
        _resolution = math.max(_resolution, math.int2(3, 3));
        _extent = math.max(_extent, float2.zero);
        _noiseRepeat = math.max(_noiseRepeat, 1);
    }

    #endregion

    #region Internal objects

    Mesh _mesh;
    int2 _meshResolution;

    #endregion

    #region MonoBehaviour implementation

    void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        _mesh = null;
    }

    void Update()
    {
        using (var vertexArray = CreateVertexArray())
        {
            if (_meshResolution.Equals(_resolution))
                UpdateVerticesOnMesh(vertexArray);
            else
                ResetMesh(vertexArray);
        }

        UpdateMeshBounds();

        Graphics.DrawMesh(_mesh, transform.localToWorldMatrix, _material, gameObject.layer);
    }

    #endregion

    #region Mesh object operations

    void ResetMesh(NativeArray<Vertex> vertexArray)
    {
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.hideFlags = HideFlags.DontSave;
        }
        else
        {
            _mesh.Clear();
        }

        var vertexCount = vertexArray.Length;

        _mesh.SetVertexBufferParams(
            vertexCount,
            new VertexAttributeDescriptor
                (VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor
                (VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
        );
        _mesh.SetVertexBufferData(vertexArray, 0, 0, vertexCount);

        using (var indexArray = CreateIndexArray(vertexCount))
        {
            _mesh.SetIndexBufferParams(vertexCount, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(indexArray, 0, 0, vertexCount);
        }

        _mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount));

        _meshResolution = _resolution;
    }

    void UpdateVerticesOnMesh(NativeArray<Vertex> vertexArray)
    {
        _mesh.SetVertexBufferData(vertexArray, 0, 0, vertexArray.Length);
    }

    void UpdateMeshBounds()
    {
        // TODO
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
    }

    #endregion

    #region Index array operations

    NativeArray<int> CreateIndexArray(int length)
    {
        var buffer = new NativeArray<int>(
            length, Allocator.Temp,
            NativeArrayOptions.UninitializedMemory
        );

        for (var i = 0; i < length; i++) buffer[i] = i;

        return buffer;
    }

    #endregion

    #region Jobified vertex animation

    struct Vertex
    {
        public float3 position;
        public float3 normal;
    }

    NativeArray<Vertex> CreateVertexArray()
    {
        var triangleCount = 2 * (_resolution.x - 1) * (_resolution.y - 1);

        var points = new NativeArray<float3>(
            _resolution.x * _resolution.y,
            Allocator.TempJob, NativeArrayOptions.UninitializedMemory
        );

        var vertices = new NativeArray<Vertex>(
            triangleCount * 3,
            Allocator.TempJob, NativeArrayOptions.UninitializedMemory
        );

        var job_p = new PointCalculationJob {
            rotation = Time.time * 0.3f,
            resolution = _resolution,
            extent = _extent,
            noiseRepeat = _noiseRepeat,
            noiseAmp = _noiseAmplitude,
            noiseRot = Time.time * _noiseAnimation,
            output = points
        };

        var job_v = new VertexConstructionJob {
            resolution = _resolution,
            points = points,
            output = vertices
        };

        var handle_p = job_p.Schedule(points.Length, 64);
        var handle_v = job_v.Schedule(triangleCount, 64, handle_p);
        
        handle_v.Complete();
        points.Dispose();

        return vertices;
    }

    [Unity.Burst.BurstCompile(CompileSynchronously = true)]
    struct PointCalculationJob : IJobParallelFor
    {
        [ReadOnly] public float rotation;
        [ReadOnly] public int2 resolution;
        [ReadOnly] public float2 extent;
        [ReadOnly] public float noiseRepeat;
        [ReadOnly] public float noiseAmp;
        [ReadOnly] public float noiseRot;

        [WriteOnly] public NativeArray<float3> output;

        public void Execute(int i)
        {
            var idx = math.float2(i % resolution.x, i / resolution.x);

            var p = (idx / resolution - 0.5f) * extent;
            var z = noise.snoise(p * noiseRepeat) * noiseAmp;

            output[i] = math.float3(p, z);
        }
    }

    [Unity.Burst.BurstCompile(CompileSynchronously = true)]
    struct VertexConstructionJob : IJobParallelFor
    {
        [ReadOnly] public int2 resolution;
        [ReadOnly] public NativeArray<float3> points;

        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<Vertex> output;

        public void Execute(int i)
        {
            var it = i / 2;
            var ix = it % (resolution.x - 1);
            var iy = it / (resolution.x - 1);

            var idx1 = iy * resolution.x + ix;
            var idx2 = idx1;
            var idx3 = idx1;

            if ((i & 1) == 0)
            {
                idx2 += 1;
                idx3 += resolution.x;
            }
            else
            {
                idx1 += resolution.x;
                idx2 += 1;
                idx3 += resolution.x + 1;
            }

            var V1 = points[idx1];
            var V2 = points[idx2];
            var V3 = points[idx3];

            var N = math.normalize(math.cross(V2 - V1, V3 - V1));

            output[i * 3 + 0] = new Vertex { position = V1, normal = N };
            output[i * 3 + 1] = new Vertex { position = V2, normal = N };
            output[i * 3 + 2] = new Vertex { position = V3, normal = N };
        }
    }

    #endregion
}
