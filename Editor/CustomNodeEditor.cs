namespace YNode.Editor
{
    public interface ICustomNodeEditor<T> where T : INodeValue
    {
        public T Value { get; }
    }
}
