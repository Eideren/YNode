namespace YNode
{
    public interface INodeUpgrader<T> : INodeUpgrader where T : INodeValue
    {
        void Replace(T original);
        void INodeUpgrader.Replace(INodeValue original) => Replace((T)original);
    }

    public interface INodeUpgrader
    {
        void Replace(INodeValue original);
    }
}
