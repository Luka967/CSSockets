namespace CSSockets.Http.Primitives
{
    public delegate void AsyncCreationHandler<T>(T item);
    public interface IAsyncOutputter<T>
    {
        event AsyncCreationHandler<T> OnOutput;
        T Next();
    }
    public interface IQueueableAsyncOutputter<T> : IAsyncOutputter<T>
    {
        int QueuedCount { get; }
    }
}
