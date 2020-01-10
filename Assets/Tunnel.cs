//
// Tunnel - An example of vertex animation with C# Job System and New Mesh API
// https://github.com/keijiro/VertexAnimationJob
//

//#define USE_NEW_API

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;
using System.Collections.Generic;

[ExecuteInEditMode, RequireComponent(typeof(MeshRenderer))]
public sealed class Tunnel : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] int2 _resolution = math.int2(128, 512);
    [SerializeField] float _radius = 1;
    [SerializeField] float _depth = 10;
    [SerializeField] int _noiseRepeat = 6;
    [SerializeField] float _noiseAmplitude = 0.1f;
    [SerializeField] float _noiseAnimation = 0.5f;

    void OnValidate()
    {
        _resolution = math.max(_resolution, math.int2(8, 8));
        _radius = math.max(_radius, 0);
        _depth = math.max(_depth, 0);
        _noiseRepeat = math.max(_noiseRepeat, 1);
    }

    #endregion

    #region Internal objects

    Mesh _mesh;

#if USE_NEW_API
    NativeArray<int> _indexBuffer;
    NativeArray<float3> _vertexBuffer;
#else
    int[] _indexBuffer = new int [0];
    Vector3[] _vertexBuffer;
    Vector3[] _normalBuffer;
#endif

    #endregion

    #region MonoBehaviour implementation

    void OnDisable()
    {
        // Required in edit mode.
        ReleaseInternals();
    }

    void OnDestroy()
    {
        ReleaseInternals();
    }

    void Update()
    {
        SetUpInternals();

        if (_indexBuffer.Length != IndexCount)
        {
            // Mesh reallocation and reconstruction
            _mesh.Clear();
            DisposeBuffers();
            AllocateBuffers();
            UpdateVertexBuffer();
            InitializeMesh();
        }
        else
        {
            // Only update the vertex data.
            UpdateVertexBuffer();
            UpdateVerticesOnMesh();
        }

        UpdateMeshBounds();
    }

    #endregion

    #region Internal-use properties and methods

    int TriangleCount => 2 * _resolution.x * (_resolution.y - 1);
    int IndexCount => 3 * TriangleCount;
    int VertexCount => _resolution.x * _resolution.y;

    void SetUpInternals()
    {
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.hideFlags = HideFlags.DontSave;

            var meshFilter = GetComponent<MeshFilter>();

            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
                meshFilter.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }

            meshFilter.sharedMesh = _mesh;
        }
    }

    void ReleaseInternals()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        _mesh = null;

        DisposeBuffers();
    }

    void UpdateMeshBounds()
    {
        var e = _radius * 5;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(e, e, _depth));
    }

    #endregion

    #region Index/vertex buffer operations

    void AllocateBuffers()
    {
#if USE_NEW_API
        _indexBuffer = new NativeArray<int>(
            IndexCount, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory
        );
        _vertexBuffer = new NativeArray<float3>(
            VertexCount * 2, Allocator.Persistent,
            NativeArrayOptions.UninitializedMemory
        );
#else
        _indexBuffer = new int [IndexCount];
        _vertexBuffer = new Vector3 [VertexCount];
        _normalBuffer = new Vector3 [VertexCount];
#endif

        InitializeIndexArray();
    }

    void DisposeBuffers()
    {
#if USE_NEW_API
        if (_indexBuffer.IsCreated) _indexBuffer.Dispose();
        if (_vertexBuffer.IsCreated) _vertexBuffer.Dispose();
#else
        _indexBuffer = new int [0];
#endif
    }

    void InitializeIndexArray()
    {
        var offs = 0;
        var index = 0;

        for (var ring = 0; ring < _resolution.y - 1; ring++)
        {
            for (var i = 0; i < _resolution.x - 1; i++)
            {
                _indexBuffer[offs++] = index;
                _indexBuffer[offs++] = index + _resolution.x;
                _indexBuffer[offs++] = index + 1;

                _indexBuffer[offs++] = index + 1;
                _indexBuffer[offs++] = index + _resolution.x;
                _indexBuffer[offs++] = index + _resolution.x + 1;

                index++;
            }

            _indexBuffer[offs++] = index;
            _indexBuffer[offs++] = index + _resolution.x;
            _indexBuffer[offs++] = index - _resolution.x + 1;

            _indexBuffer[offs++] = index - _resolution.x + 1;
            _indexBuffer[offs++] = index + _resolution.x;
            _indexBuffer[offs++] = index + 1;

            index++;
        }
    }

    #endregion

    #region Mesh object operations

    void InitializeMesh()
    {
#if USE_NEW_API
        _mesh.SetVertexBufferParams(
            VertexCount,
            new VertexAttributeDescriptor
                (VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor
                (VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
        );
        _mesh.SetVertexBufferData(_vertexBuffer, 0, 0, VertexCount * 2);

        _mesh.SetIndexBufferParams(IndexCount, IndexFormat.UInt32);
        _mesh.SetIndexBufferData(_indexBuffer, 0, 0, IndexCount);

        _mesh.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount));
#else
        _mesh.SetVertices(_vertexBuffer);
        _mesh.SetNormals(_normalBuffer);
        _mesh.SetIndices(_indexBuffer, MeshTopology.Triangles, 0);
#endif
    }

    void UpdateVerticesOnMesh()
    {
#if USE_NEW_API
        _mesh.SetVertexBufferData(_vertexBuffer, 0, 0, VertexCount * 2);
#else
        _mesh.SetVertices(_vertexBuffer);
#endif
    }

    #endregion

    #region Jobified vertex animation

#if USE_NEW_API

    void UpdateVertexBuffer()
    {
        // Job object
        var job = new VertexUpdateJob{
            rotation = Time.time * 0.3f,
            resolution = _resolution,
            radius = _radius,
            depth = _depth,
            noiseRepeat = _noiseRepeat,
            noiseAmp = _noiseAmplitude,
            noiseRot = Time.time * _noiseAnimation,
            buffer = _vertexBuffer
        };

        // Run and wait until completed
        job.Schedule((int)VertexCount, 64).Complete();
    }

    [Unity.Burst.BurstCompile(CompileSynchronously = true)]
    struct VertexUpdateJob : IJobParallelFor
    {
        [ReadOnly] public float rotation;
        [ReadOnly] public int2 resolution;
        [ReadOnly] public float radius;
        [ReadOnly] public float depth;
        [ReadOnly] public float noiseRepeat;
        [ReadOnly] public float noiseAmp;
        [ReadOnly] public float noiseRot;

        [NativeDisableParallelForRestriction]
        [WriteOnly] public NativeArray<float3> buffer;

        // Calculate a vertex position from polar coordinates and a
        // displacement value.
        float3 Vertex(float2 polar, float disp)
        {
            var theta = math.PI * 2 * polar.x;
            var r = math.float2(math.cos(theta), math.sin(theta));
            var z = (polar.y - 0.5f) * depth;
            return math.float3(r * (radius + disp), z);
        }

        public void Execute(int i)
        {
            // Index -> Polar coodinates
            var polar = math.float2(
                math.frac((float)i / resolution.x),
                (float)(i / resolution.x) / resolution.y
            );

            // Polar coodinates -> Noise position
            // The offset (1, 0) is needed for correct tiling.
            var npos = (polar + math.float2(1, 0)) *
                math.float2(1, depth / (math.PI * 2 * radius)) * noiseRepeat;

            // Noise field repeating period
            var nrep = math.float2(noiseRepeat, 1000);

            // Noise sample with analytical gradients
            var psrdn = noise.psrdnoise(npos, nrep, noiseRot) * noiseAmp;
            var disp = psrdn.x;
            var grad = psrdn.yz;

            // Vertex position
            var V = Vertex(polar, disp);

            // Normal calculation using the gradients
            var D = 0.001f;
            var Dp = D / noiseRepeat;
            var Vdx0 = Vertex(polar - math.float2(Dp, 0), disp - grad.x * D);
            var Vdx1 = Vertex(polar + math.float2(Dp, 0), disp + grad.x * D);
            var Vdy0 = Vertex(polar - math.float2(0, Dp), disp - grad.y * D);
            var Vdy1 = Vertex(polar + math.float2(0, Dp), disp + grad.y * D);
            var N = math.normalize(math.cross(Vdx1 - Vdx0, Vdy1 - Vdy0));

            // Output
            buffer[i * 2 + 0] = V;
            buffer[i * 2 + 1] = -N;
        }
    }

#else

    // Calculate a vertex position from polar coordinates and a
    // displacement value.
    float3 CalculateVertex(float2 polar, float disp)
    {
        var theta = math.PI * 2 * polar.x;
        var r = math.float2(math.cos(theta), math.sin(theta));
        var z = (polar.y - 0.5f) * _depth;
        return math.float3(r * (_radius + disp), z);
    }

    void UpdateVertexBuffer()
    {
        var noiseRot = Time.time * _noiseAnimation;

        for (var i = 0; i < VertexCount; i++)
        {
            // Index -> Polar coodinates
            var polar = math.float2(
                math.frac((float)i / _resolution.x),
                (float)(i / _resolution.x) / _resolution.y
            );

            // Polar coodinates -> Noise position
            // The offset (1, 0) is needed for correct tiling.
            var npos = (polar + math.float2(1, 0)) *
                math.float2(1, _depth / (math.PI * 2 * _radius)) * _noiseRepeat;

            // Noise field repeating period
            var nrep = math.float2(_noiseRepeat, 1000);

            // Noise sample with analytical gradients
            var psrdn = noise.psrdnoise(npos, nrep, noiseRot) * _noiseAmplitude;
            var disp = psrdn.x;
            var grad = psrdn.yz;

            // Vertex position
            var V = CalculateVertex(polar, disp);

            // Normal calculation using the gradients
            var D = 0.001f;
            var Dp = D / _noiseRepeat;
            var Vdx0 = CalculateVertex(polar - math.float2(Dp, 0), disp - grad.x * D);
            var Vdx1 = CalculateVertex(polar + math.float2(Dp, 0), disp + grad.x * D);
            var Vdy0 = CalculateVertex(polar - math.float2(0, Dp), disp - grad.y * D);
            var Vdy1 = CalculateVertex(polar + math.float2(0, Dp), disp + grad.y * D);
            var N = math.normalize(math.cross(Vdx1 - Vdx0, Vdy1 - Vdy0));

            // Output
            _vertexBuffer[i] = V;
            _normalBuffer[i] = -N;
        }
    }

#endif

    #endregion
}
