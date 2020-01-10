//
// Tunnel - An example of vertex animation with C# Job System and New Mesh API
// https://github.com/keijiro/VertexAnimationJob
//

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

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

    Mesh.MeshDataArray _meshDataArray;
    JobHandle _previousJobs;

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

        _previousJobs.Complete();
        if (_meshDataArray.Length > 0) ApplyMeshData();
        UpdateMeshBounds();

        ScheduleMeshDataJobs();
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
        _previousJobs.Complete();

        if (_meshDataArray.Length > 0) _meshDataArray.Dispose();

        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        _mesh = null;
    }

    void UpdateMeshBounds()
    {
        var e = _radius * 5;
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(e, e, _depth));
    }

    #endregion

    #region Mesh object operations

    void ScheduleMeshDataJobs()
    {
        _meshDataArray = Mesh.AllocateWritableMeshData(1);
        var data = _meshDataArray[0];

        data.SetVertexBufferParams(
            VertexCount,
            new VertexAttributeDescriptor
                (VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor
                (VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
        );
        data.SetIndexBufferParams(IndexCount, IndexFormat.UInt32);

        var vd = data.GetVertexData<float3>();
        var id = data.GetIndexData<int>();
        _previousJobs = ScheduleIndexJob(id, ScheduleVertexJob(vd));

        JobHandle.ScheduleBatchedJobs();
    }

    void ApplyMeshData()
    {
        var data = _meshDataArray[0];

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor(0, IndexCount));

        Mesh.ApplyAndDisposeWritableMeshData(_meshDataArray, _mesh);
    }

    #endregion

    #region Jobified index array generation

    JobHandle ScheduleIndexJob(NativeArray<int> buffer, JobHandle dep)
    {
        var job = new IndexUpdateJob()
        {
            resolution = _resolution,
            buffer = buffer
        };

        return job.Schedule(dep);
    }

    [Unity.Burst.BurstCompile(CompileSynchronously = true)]
    struct IndexUpdateJob : IJob
    {
        [ReadOnly] public int2 resolution;
        [WriteOnly] public NativeArray<int> buffer;

        public void Execute()
        {
            var offs = 0;
            var index = 0;

            for (var ring = 0; ring < resolution.y - 1; ring++)
            {
                for (var i = 0; i < resolution.x - 1; i++)
                {
                    buffer[offs++] = index;
                    buffer[offs++] = index + resolution.x;
                    buffer[offs++] = index + 1;

                    buffer[offs++] = index + 1;
                    buffer[offs++] = index + resolution.x;
                    buffer[offs++] = index + resolution.x + 1;

                    index++;
                }

                buffer[offs++] = index;
                buffer[offs++] = index + resolution.x;
                buffer[offs++] = index - resolution.x + 1;

                buffer[offs++] = index - resolution.x + 1;
                buffer[offs++] = index + resolution.x;
                buffer[offs++] = index + 1;

                index++;
            }
        }
    }

    #endregion

    #region Jobified vertex animation

    JobHandle ScheduleVertexJob(NativeArray<float3> buffer)
    {
        var job = new VertexUpdateJob{
            rotation = Time.time * 0.3f,
            resolution = _resolution,
            radius = _radius,
            depth = _depth,
            noiseRepeat = _noiseRepeat,
            noiseAmp = _noiseAmplitude,
            noiseRot = Time.time * _noiseAnimation,
            buffer = buffer
        };

        return job.Schedule((int)VertexCount, 64);
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

        [NativeDisableParallelForRestriction, WriteOnly]
        public NativeArray<float3> buffer;

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

    #endregion
}
