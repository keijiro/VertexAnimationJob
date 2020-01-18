//
// Tunnel - An example of vertex animation with C# Job System and New Mesh API
// https://github.com/keijiro/VertexAnimationJob
//

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[ExecuteInEditMode]
public sealed class Tunnel : MonoBehaviour
{
    #region Editable attributes

    [SerializeField] int2 _resolution = math.int2(128, 512);
    [SerializeField] float _radius = 1;
    [SerializeField] float _depth = 10;
    [SerializeField] int _noiseRepeat = 6;
    [SerializeField] float _noiseAmplitude = 0.1f;
    [SerializeField] float _noiseAnimation = 0.5f;
    [SerializeField] Material _material = null;

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

        _mesh.SetVertexBufferParams(
            vertexArray.Length,
            new VertexAttributeDescriptor
                (VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor
                (VertexAttribute.Normal, VertexAttributeFormat.SNorm16, 4)
        );
        _mesh.SetVertexBufferData(vertexArray, 0, 0, vertexArray.Length);

        using (var indexArray = CreateIndexArray())
        {
            _mesh.SetIndexBufferParams(indexArray.Length, IndexFormat.UInt16);
            _mesh.SetIndexBufferData(indexArray, 0, 0, indexArray.Length);
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexArray.Length));
        }

        _meshResolution = _resolution;
    }

    void UpdateVerticesOnMesh(NativeArray<Vertex> vertexArray)
    {
        _mesh.SetVertexBufferData(vertexArray, 0, 0, vertexArray.Length);
    }

    void UpdateMeshBounds()
    {
        var e = _radius * 5;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(e, e, _depth));
    }

    #endregion

    #region Index array operations

    NativeArray<ushort> CreateIndexArray()
    {
        var buffer = new NativeArray<ushort>(
            2 * _resolution.x * (_resolution.y - 1) * 3,
            Allocator.Temp, NativeArrayOptions.UninitializedMemory
        );

        var offs = 0;
        var index = 0;

        for (var ring = 0; ring < _resolution.y - 1; ring++)
        {
            for (var i = 0; i < _resolution.x - 1; i++)
            {
                buffer[offs++] = (ushort)(index);
                buffer[offs++] = (ushort)(index + _resolution.x);
                buffer[offs++] = (ushort)(index + 1);

                buffer[offs++] = (ushort)(index + 1);
                buffer[offs++] = (ushort)(index + _resolution.x);
                buffer[offs++] = (ushort)(index + _resolution.x + 1);

                index++;
            }

            buffer[offs++] = (ushort)(index);
            buffer[offs++] = (ushort)(index + _resolution.x);
            buffer[offs++] = (ushort)(index - _resolution.x + 1);

            buffer[offs++] = (ushort)(index - _resolution.x + 1);
            buffer[offs++] = (ushort)(index + _resolution.x);
            buffer[offs++] = (ushort)(index + 1);

            index++;
        }

        return buffer;
    }

    #endregion

    #region Jobified vertex animation

    struct Vertex
    {
        public float3 position;
        public SNorm16x4 normal;
    }

    NativeArray<Vertex> CreateVertexArray()
    {
        var vertexArray = new NativeArray<Vertex>(
            _resolution.x * _resolution.y,
            Allocator.TempJob, NativeArrayOptions.UninitializedMemory
        );

        var job = new VertexUpdateJob{
            rotation = Time.time * 0.3f,
            resolution = _resolution,
            radius = _radius,
            depth = _depth,
            noiseRepeat = _noiseRepeat,
            noiseAmp = _noiseAmplitude,
            noiseRot = Time.time * _noiseAnimation,
            buffer = vertexArray
        };

        job.Schedule(vertexArray.Length, 64).Complete();

        return vertexArray;
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

        [WriteOnly] public NativeArray<Vertex> buffer;

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
            buffer[i] = new Vertex { position = V, normal = -N };
        }
    }

    #endregion
}
