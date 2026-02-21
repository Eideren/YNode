using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;


public class SceneGUIProxy
{
#if UNITY_EDITOR
    private UnityEditor.IMGUI.Controls.BoxBoundsHandle m_BoundsHandle = new();
#endif

    public static SceneGUIProxy Instance = new();

    private readonly Stack<AutoUndoer> _trackers = new();

    public Matrix4x4 matrix
    {
        get
        {
            #if UNITY_EDITOR
            return UnityEditor.Handles.matrix;
            #else
            return Matrix4x4.identity;
            #endif
        }
        set
        {
            #if UNITY_EDITOR
            UnityEditor.Handles.matrix = value;
            #endif
        }
    }

    public Vector3 PositionHandle(Vector3 position, Quaternion rotation)
    {
        #if UNITY_EDITOR
        UnityEditor.EditorGUI.BeginChangeCheck();
        position = UnityEditor.Handles.PositionHandle(position, rotation);
        if (UnityEditor.EditorGUI.EndChangeCheck())
            RecordTrackedObject();
        #endif
        return position;
    }

    public Quaternion RotationHandle(Quaternion rotation, Vector3 position)
    {
        #if UNITY_EDITOR
        UnityEditor.EditorGUI.BeginChangeCheck();
        rotation = UnityEditor.Handles.RotationHandle(rotation, position);
        if (UnityEditor.EditorGUI.EndChangeCheck())
            RecordTrackedObject();
        #endif
        return rotation;
    }

    public Vector3 ScaleHandle(Vector3 scale, Vector3 position, Quaternion rotation)
    {
        #if UNITY_EDITOR
        UnityEditor.EditorGUI.BeginChangeCheck();
        scale = UnityEditor.Handles.ScaleHandle(scale, position, rotation);
        if (UnityEditor.EditorGUI.EndChangeCheck())
            RecordTrackedObject();
        #endif
        return scale;
    }

    public Bounds Bounds(Bounds bounds)
    {
        #if UNITY_EDITOR
        UnityEditor.EditorGUI.BeginChangeCheck();
        m_BoundsHandle.center = bounds.center;
        m_BoundsHandle.size = bounds.size;
        m_BoundsHandle.DrawHandle();
        bounds.center = m_BoundsHandle.center;
        bounds.size = m_BoundsHandle.size;
        if (UnityEditor.EditorGUI.EndChangeCheck())
            RecordTrackedObject();
        #endif
        return bounds;
    }

    public void Color(Color color)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        #endif
        GUI.color = color;
    }

    public void Arrow(Vector3 pos, Quaternion rotation, float size)
    {
#if UNITY_EDITOR
        if (Event.current.type == EventType.Repaint)
        {
            UnityEditor.Handles.ArrowHandleCap(0, pos, rotation, size, EventType.Repaint);
        }
#endif
    }

    public void Line(Vector3 start, Vector3 end)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.DrawLine(start, end);
        #endif
    }

    public void DottedLine(Vector3 start, Vector3 end, float screenspaceSize = 4)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.DrawDottedLine(start, end, screenspaceSize);
        #endif
    }

    public void WireCube(Vector3 center, Vector3 size)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.DrawWireCube(center, size);
        #endif
    }

    public void WireCube(Bounds bounds)
    {
        #if UNITY_EDITOR
        UnityEditor.Handles.DrawWireCube(bounds.center, bounds.size);
        #endif
    }

    public void WireSphere(Vector3 center, float radius)
    {
        #if UNITY_EDITOR
        if (Event.current.type != EventType.Repaint)
            return;

        if (s_SphereLines == null)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s_SphereLines = ConvertToLineMesh(obj.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(obj);
        }

        s_ApplyWireMaterial.Invoke(UnityEditor.Handles.zTest);
        /*Shader.SetGlobalColor("_HandleColor", UnityEditor.Handles.color * new Color(1f, 1f, 1f, 0.5f));
        Shader.SetGlobalFloat("_HandleSize", 1f);
        UnityEditor.HandleUtility.handleMaterial.SetFloat("_HandleZTest", (float)UnityEditor.Handles.zTest);
        UnityEditor.HandleUtility.handleMaterial.SetPass(0);*/
        Graphics.DrawMeshNow(s_SphereLines, UnityEditor.Handles.matrix * Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * (radius * 2f)));

        #endif
    }


    public void Label(Vector3 position, string text, Color? color = null)
    {
        #if UNITY_EDITOR
        var c = GUI.color;
        if (color != null)
            GUI.color = color.Value;
        UnityEditor.Handles.Label(position, text);
        if (color != null)
            GUI.color = c;
        #endif
    }

    public void RecordObject(Object obj, string name)
    {
        #if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(obj, name);
        #endif
    }

    public void SetDirty(Object obj)
    {
        #if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(obj);
        #endif
    }

    private static Mesh? s_SphereLines;
    private static Action<CompareFunction> s_ApplyWireMaterial, s_ApplyDottedWireMaterial;

    static Mesh ConvertToLineMesh(Mesh mesh)
    {
        var tris = mesh.triangles;
        List<int> lineIndices = new List<int>(tris.Length * 2);
        for (int i = 0; i < tris.Length; i += 3)
        {
            lineIndices.Add(tris[i]);
            lineIndices.Add(tris[i + 1]);

            lineIndices.Add(tris[i + 1]);
            lineIndices.Add(tris[i + 2]);

            lineIndices.Add(tris[i + 2]);
            lineIndices.Add(tris[i]);
        }
        var lineMesh = new Mesh
        {
            vertices = mesh.vertices,
            uv = mesh.uv,
            normals = mesh.normals,
            tangents = mesh.tangents
        };
        lineMesh.SetIndices(lineIndices, MeshTopology.Lines, 0, true);
        return lineMesh;
    }

    private void RecordTrackedObject()
    {
        if (_trackers.TryPeek(out var tracker) && tracker.UndoTarget != null)
        {
            RecordObject(tracker.UndoTarget, tracker.UndoPrefix);
        }
    }

    public void BeginChangeCheck()
    {
#if UNITY_EDITOR
        UnityEditor.EditorGUI.BeginChangeCheck();
#endif
    }

    public bool EndChangeCheck()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorGUI.EndChangeCheck();
#else
        return false;
#endif
    }

    public AutoUndoer AutoUndo(Object undoTarget, string undoPrefix) => new(this, undoTarget, undoPrefix);

    public struct AutoUndoer : IDisposable
    {
        public readonly Object UndoTarget;
        public readonly string UndoPrefix;
        public readonly SceneGUIProxy Proxy;

        public AutoUndoer(SceneGUIProxy proxy, Object undoTarget, string undoPrefix)
        {
            Proxy = proxy;
            UndoTarget = undoTarget;
            UndoPrefix = undoPrefix;

            proxy._trackers.Push(this);
            Proxy.BeginChangeCheck();
        }

        public void Dispose()
        {
            Proxy._trackers.Pop();
            if (Proxy.EndChangeCheck() && UndoTarget != null)
            {
                Proxy.SetDirty(UndoTarget);
            }
        }
    }

    public TempState TempChanges() => new();

    public struct TempState : IDisposable
    {
        public readonly Color Color;
        public readonly Color BackgroundColor;
        public readonly Color ContentColor;
        public readonly Color HandleColor;
        public readonly bool Enabled;
        public readonly Matrix4x4 Matrix;

        public TempState()
        {
            Color = GUI.color;
            BackgroundColor = GUI.backgroundColor;
            ContentColor = GUI.contentColor;
            Enabled = GUI.enabled;
            Matrix = GUI.matrix;
            #if UNITY_EDITOR
            HandleColor = UnityEditor.Handles.color;
            #endif
        }

        public void Dispose()
        {
            GUI.color = Color;
            GUI.backgroundColor = BackgroundColor;
            GUI.contentColor = ContentColor;
            GUI.enabled = Enabled;
            GUI.matrix = Matrix;
            #if UNITY_EDITOR
            UnityEditor.Handles.color = HandleColor;
            #endif
        }
    }

    static SceneGUIProxy()
    {
        #if UNITY_EDITOR
        s_ApplyDottedWireMaterial ??= (Action<CompareFunction>)typeof(UnityEditor.HandleUtility)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(x => x.Name == "ApplyDottedWireMaterial" && x.GetParameters().Length == 1)
            .CreateDelegate(typeof(Action<CompareFunction>));
        s_ApplyWireMaterial ??= (Action<CompareFunction>)typeof(UnityEditor.HandleUtility)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .First(x => x.Name == "ApplyWireMaterial" && x.GetParameters().Length == 1)
            .CreateDelegate(typeof(Action<CompareFunction>));
        #endif
    }
}
