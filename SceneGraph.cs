using UnityEngine;

namespace YNode
{
    /// <summary> Lets you instantiate a node graph in the scene. This allows you to reference in-scene objects. </summary>
    public class SceneGraph : MonoBehaviour
    {
        public NodeGraph? graph;
    }

    /// <summary> Derive from this class to create a SceneGraph with a specific graph type. </summary>
    public class SceneGraph<T> : SceneGraph where T : NodeGraph
    {
        public new T? graph
        {
            get => base.graph as T;
            set => base.graph = value;
        }
    }
}
