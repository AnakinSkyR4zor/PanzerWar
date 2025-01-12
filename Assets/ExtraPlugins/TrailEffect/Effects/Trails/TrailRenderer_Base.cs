using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.PigeonCoopUtil;
using UnityEngine;

namespace PigeonCoopToolkit.Effects.Trails
{
    public abstract class TrailRenderer_Base : MonoBehaviour
    {
        [ContextMenu("CLEARER")]
        public void NewClear()
        {
            if(Application.isPlaying == true)
                ClearSystem(true);
        }

        public PCTrailRendererData TrailData;
        public bool Emit = false;
        public int MaxNumberOfPoints = 50;

        protected bool _emit;
        private PCTrail _activeTrail;
        private List<PCTrail> _fadingTrails;
        protected Transform _t;


        private static Dictionary<Material, List<PCTrail>> _matToTrailList;
        private static List<Mesh> _toClean; 
 
        private static bool _hasRenderer = false;
        private static int GlobalTrailRendererCount = 0;

        protected virtual void Awake()
        {
            GlobalTrailRendererCount++;

            if(GlobalTrailRendererCount == 1)
            {
                _matToTrailList = new Dictionary<Material, List<PCTrail>>();
                _toClean = new List<Mesh>();
            }

            _activeTrail = new PCTrail(MaxNumberOfPoints);
            _fadingTrails = new List<PCTrail>();
            _t = transform;
            _emit = Emit;
        }

        protected virtual void Start()
        {
            
        }

        protected virtual void LateUpdate()
        {
            if(_hasRenderer)
                return;


            _hasRenderer = true;
            

            foreach (KeyValuePair<Material, List<PCTrail>> keyValuePair in _matToTrailList)
            {
                CombineInstance[] combineInstances = new CombineInstance[keyValuePair.Value.Count];

                for (int i = 0; i < keyValuePair.Value.Count; i++)
                {
                    combineInstances[i] = new CombineInstance
                    {
                        mesh = keyValuePair.Value[i].Mesh,
                        subMeshIndex = 0,
                        transform = Matrix4x4.identity
                    };
                }

                Mesh combinedMesh = new Mesh();
                combinedMesh.CombineMeshes(combineInstances, true, false);
                _toClean.Add(combinedMesh);

                DrawMesh(combinedMesh, keyValuePair.Key);

                keyValuePair.Value.Clear();
            }
        }

        protected virtual void Update()
        {
            if (_hasRenderer)
            {
                _hasRenderer = false;

                if (_toClean.Count > 0)
                {
                    foreach (Mesh mesh in _toClean)
                    {
                        if (Application.isEditor)
                            DestroyImmediate(mesh, true);
                        else
                            Destroy(mesh);
                    }
                }

                _toClean.Clear();

            }

            if (_matToTrailList.ContainsKey(TrailData.TrailMaterial) == false)
            {
                _matToTrailList.Add(TrailData.TrailMaterial, new List<PCTrail>());
            }
            
            CheckEmitChange();

            if(_activeTrail != null)
            {
                UpdatePoints(Time.deltaTime, _activeTrail);
                GenerateMesh(_activeTrail);
                _matToTrailList[TrailData.TrailMaterial].Add(_activeTrail);
            }
            
             
            for (int i = _fadingTrails.Count-1; i >= 0; i--)
            {
                if (_fadingTrails[i] == null || _fadingTrails[i].Points.Any(a => a.TimeActive() < TrailData.Lifetime) == false)
                {
                    if (_fadingTrails[i] != null)
                        _fadingTrails[i].Dispose();

                    _fadingTrails.RemoveAt(i);
                    continue;
                }

                UpdatePoints(Time.deltaTime, _fadingTrails[i]);
                GenerateMesh(_fadingTrails[i]);

                _matToTrailList[TrailData.TrailMaterial].Add(_fadingTrails[i]);
            }
        }

        protected virtual void OnDestroy()
        {
            GlobalTrailRendererCount--;

            if(GlobalTrailRendererCount == 0)
            {
                if(_toClean != null && _toClean.Count > 0)
                {
                    foreach (Mesh mesh in _toClean)
                    {
                        if (Application.isEditor)
                            DestroyImmediate(mesh, true);
                        else
                            Destroy(mesh);
                    }
                }

                _toClean = null;
                _matToTrailList.Clear();
                _matToTrailList = null;
            }

            if (_activeTrail != null)
            {
                _activeTrail.Dispose();
                _activeTrail = null;
            }

            if (_fadingTrails != null)
            {
                foreach (PCTrail fadingTrail in _fadingTrails)
                {
                    if (fadingTrail != null)
                        fadingTrail.Dispose();
                }

                _fadingTrails.Clear();
            }
        }

        protected virtual void OnStopEmit()
        {
            
        }

        protected virtual void OnStartEmit()
        {
        }

        protected virtual void Reset()
        {
            if(TrailData == null)
                TrailData = new PCTrailRendererData();

            TrailData.ColorOverLife = new Gradient();
            TrailData.Lifetime = 1;
            TrailData.SizeOverLife = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            MaxNumberOfPoints = 50;
        }

        protected virtual void InitialiseNewPoint(PCTrailPoint newPoint)
        {

        }

        protected virtual void UpdatePoint(PCTrailPoint point, float deltaTime)
        {

        }

        protected void AddPoint(PCTrailPoint newPoint, Vector3 pos)
        {
            if (_activeTrail == null)
                return;

            newPoint.Position = pos;
            newPoint.PointNumber = _activeTrail.Points.Count == 0 ? 0 : _activeTrail.Points[_activeTrail.Points.Count - 1].PointNumber + 1;
            InitialiseNewPoint(newPoint);

            newPoint.SetDistanceFromStart(_activeTrail.Points.Count == 0
                                              ? 0
                                              : _activeTrail.Points[_activeTrail.Points.Count - 1].GetDistanceFromStart() + Vector3.Distance(_activeTrail.Points[_activeTrail.Points.Count - 1].Position, pos));

            if(TrailData.UseForwardOverride)
            {
                newPoint.Forward = TrailData.ForwardOverrideRelative
                                       ? _t.TransformDirection(TrailData.ForwardOverride.normalized)
                                       : TrailData.ForwardOverride.normalized;
            }

            _activeTrail.Points.Add(newPoint);
        }

        private void GenerateMesh(PCTrail trail)
        {
            trail.Mesh.Clear(false);

            Vector3 camForward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;

            if(TrailData.UseForwardOverride)
            {
                camForward = TrailData.ForwardOverride.normalized;
            }

            trail.activePointCount = NumberOfActivePoints(trail);

            if (trail.activePointCount < 2)
                return;


            int vertIndex = 0;
            for (int i = 0; i < trail.Points.Count; i++)
            {
                PCTrailPoint p = trail.Points[i];
                float timeAlong = p.TimeActive()/TrailData.Lifetime;

                if(p.TimeActive() > TrailData.Lifetime)
                {
                    continue;
                }

                if (TrailData.UseForwardOverride && TrailData.ForwardOverrideRelative)
                    camForward = p.Forward;

                Vector3 cross = Vector3.zero;

                if (i < trail.Points.Count - 1)
                {
                    cross =
                        Vector3.Cross((trail.Points[i + 1].Position - p.Position).normalized, camForward).
                            normalized;
                }
                else
                {
                    cross =
                        Vector3.Cross((p.Position - trail.Points[i - 1].Position).normalized, camForward).
                            normalized;
                }

                Color c = TrailData.StretchColorToFit ? TrailData.ColorOverLife.Evaluate(1 - ((float)vertIndex / (float)trail.activePointCount / 2f)) : TrailData.ColorOverLife.Evaluate(timeAlong);
                float s = TrailData.StretchSizeToFit ? TrailData.SizeOverLife.Evaluate(1 - ((float)vertIndex / (float)trail.activePointCount / 2f)) : TrailData.SizeOverLife.Evaluate(timeAlong);
                trail.verticies[vertIndex] = p.Position + cross * s;

                if(TrailData.MaterialTileLength <= 0)
                {
                    trail.uvs[vertIndex] = new Vector2((float)vertIndex / (float)trail.activePointCount / 2f, 0);
                }
                else
                {
                    trail.uvs[vertIndex] = new Vector2(p.GetDistanceFromStart() / TrailData.MaterialTileLength, 0);
                }

                trail.normals[vertIndex] = camForward;
                trail.colors[vertIndex] = c;
                vertIndex++;
                trail.verticies[vertIndex] = p.Position - cross * s;

                if (TrailData.MaterialTileLength <= 0)
                {
                    trail.uvs[vertIndex] = new Vector2((float)vertIndex / (float)trail.activePointCount / 2f, 1);
                }
                else
                {
                    trail.uvs[vertIndex] = new Vector2(p.GetDistanceFromStart() / TrailData.MaterialTileLength, 1);
                }

                trail.normals[vertIndex] = camForward;
                trail.colors[vertIndex] = c;

                vertIndex++;
            }

            Vector2 finalPosition = trail.verticies[vertIndex-1];
            for(int i = vertIndex; i < trail.verticies.Length; i++)
            {
                trail.verticies[i] = finalPosition;
            }

            int indIndex = 0;
            for (int pointIndex = 0; pointIndex < 2 * (trail.activePointCount - 1); pointIndex++)
            {
                if(pointIndex%2==0)
                {
                    trail.indicies[indIndex] = pointIndex;
                    indIndex++;
                    trail.indicies[indIndex] = pointIndex + 1;
                    indIndex++;
                    trail.indicies[indIndex] = pointIndex + 2;
                }
                else
                {
                    trail.indicies[indIndex] = pointIndex + 2;
                    indIndex++;
                    trail.indicies[indIndex] = pointIndex + 1;
                    indIndex++;
                    trail.indicies[indIndex] = pointIndex;
                }

                indIndex++;
            }

            int finalIndex = trail.indicies[indIndex-1];
            for (int i = indIndex; i < trail.indicies.Length; i++)
            {
                trail.indicies[i] = finalIndex;
            }

            trail.Mesh.vertices = trail.verticies;
            trail.Mesh.SetIndices(trail.indicies, MeshTopology.Triangles, 0);
            trail.Mesh.uv = trail.uvs;
            trail.Mesh.normals = trail.normals;
            trail.Mesh.colors = trail.colors;
        }

        private void DrawMesh(Mesh trailMesh, Material trailMaterial)
        {
            Graphics.DrawMesh(trailMesh, Matrix4x4.identity, trailMaterial, gameObject.layer);
        }

        private void UpdatePoints(float deltaTime, PCTrail line)
        {
            for (int i = 0; i < line.Points.Count; i++)
            {
                line.Points[i].Update(deltaTime);
                UpdatePoint(line.Points[i], deltaTime);
            }
        }

        private void CheckEmitChange()
        {
            if (_emit != Emit)
            {
                _emit = Emit;
                if (_emit)
                {
                    OnStartEmit();
                    _activeTrail = new PCTrail(MaxNumberOfPoints);
                }
                else
                {
                    OnStopEmit();
                    _fadingTrails.Add(_activeTrail);
                    _activeTrail = null;
                }
            }
        }

        private int NumberOfActivePoints(PCTrail line)
        {
            int count = 0;
            for (int index = 0; index < line.Points.Count; index++)
            {
                if (line.Points[index].TimeActive() < TrailData.Lifetime) count++;
            }
            return count;
        }

        /// <summary>
        /// Insert a trail into this trail renderer. 
        /// </summary>
        /// <param name="from">The start position of the trail.</param>
        /// <param name="to">The end position of the trail.</param>
        /// <param name="distanceBetweenPoints">Distance between each point on the trail</param>
        public void CreateTrail(Vector3 from, Vector3 to, float distanceBetweenPoints)
        {
            float distanceBetween = Vector3.Distance(from, to);

            Vector3 dirVector = to - from;
            dirVector = dirVector.normalized;

            float currentLength = 0;

            CircularBuffer<PCTrailPoint> newLine = new CircularBuffer<PCTrailPoint>(MaxNumberOfPoints);
            int pointNumber = 0;
            while (currentLength < distanceBetween) 
            {
                PCTrailPoint newPoint = new PCTrailPoint();
                newPoint.PointNumber = pointNumber;
                newPoint.Position = from + dirVector*currentLength;
                newLine.Add(newPoint);
                InitialiseNewPoint(newPoint);

                pointNumber++;

                if (distanceBetweenPoints <= 0)
                    break;
                else
                    currentLength += distanceBetweenPoints;
            }

            PCTrailPoint lastPoint = new PCTrailPoint();
            lastPoint.PointNumber = pointNumber;
            lastPoint.Position = to;
            newLine.Add(lastPoint);
            InitialiseNewPoint(lastPoint);

            PCTrail newTrail = new PCTrail(MaxNumberOfPoints);
            newTrail.Points = newLine;

            _fadingTrails.Add(newTrail);
        }
        
        /// <summary>
        /// Clears all active trails from the system.
        /// </summary>
        /// <param name="emitState">Desired emit state after clearing</param>
        public void ClearSystem(bool emitState)
        {
            if(_activeTrail != null)
            {
                _activeTrail.Dispose();
                _activeTrail = null;
            }

            if (_fadingTrails != null)
            {
                foreach (PCTrail fadingTrail in _fadingTrails)
                {
                    if (fadingTrail != null)
                        fadingTrail.Dispose();
                }

                _fadingTrails.Clear();
            }

            Emit = emitState;
            _emit = !emitState;

            CheckEmitChange();
        }

        /// <summary>
        /// Get the number of active seperate trail segments.
        /// </summary>
        public int NumSegments()
        {
            int num = 0;
            if (_activeTrail != null && NumberOfActivePoints(_activeTrail) != 0)
                num++;

            num += _fadingTrails.Count;
            return num;
        }
    }

    public class PCTrail : System.IDisposable
    {
        public CircularBuffer<PCTrailPoint> Points;
        public Mesh Mesh;

        public Vector3[] verticies;
        public Vector3[] normals;
        public Vector2[] uvs; 
        public Color[] colors; 
        public int[] indicies;
        public int activePointCount;

        public PCTrail(int numPoints)
        {
            Mesh = new Mesh();
            Mesh.MarkDynamic();

            verticies = new Vector3[2 * numPoints];
            normals = new Vector3[2 * numPoints];
            uvs = new Vector2[2 * numPoints];
            colors = new Color[2 * numPoints];
            indicies = new int[2 * (numPoints) * 3];

            Points = new CircularBuffer<PCTrailPoint>(numPoints);
        }

        #region Implementation of IDisposable

        public void Dispose()
        {
            if(Mesh != null)
            {
                if(Application.isEditor)
                    Object.DestroyImmediate(Mesh, true);
                else
                    Object.Destroy(Mesh);
            }

            Points.Clear();
            Points = null;
        }

        #endregion
    }

    public class PCTrailPoint  
    {
        public Vector3 Forward;
        public Vector3 Position;
        public int PointNumber;

        private float _timeActive = 0;
        private float _distance;

        public virtual void Update(float deltaTime)
        {
            _timeActive += deltaTime;
        }

        public float TimeActive()
        {
            return _timeActive;
        }

        public void SetDistanceFromStart(float distance)
        {
            _distance = distance;
        }

        public float GetDistanceFromStart()
        {
            return _distance;
        }
    }

    [System.Serializable]
    public class PCTrailRendererData
    {
        public Material TrailMaterial;
        public float Lifetime = 1;
        public AnimationCurve SizeOverLife = new AnimationCurve();
        public Gradient ColorOverLife;
        public bool StretchSizeToFit;
        public bool StretchColorToFit;
        public float MaterialTileLength = 0;
        public bool UseForwardOverride;
        public Vector3 ForwardOverride;
        public bool ForwardOverrideRelative;
    }
}


