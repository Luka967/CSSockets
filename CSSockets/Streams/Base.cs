namespace CSSockets.Streams
{
    public delegate void ControlHandler();
    public delegate void DataHandler(byte[] data);
    public delegate void OutputHandler<T>(T item);

    public interface IEndable
    {
        bool Ended { get; }
        bool End();
    }
    public interface IFinishable
    {
        bool Finished { get; }
        bool Finish();
    }

    public interface IReadable
    {
        event DataHandler OnData;
        event ControlHandler OnFail;

        IWritable PipedTo { get; }
        bool Pipe(IWritable to);
        bool Burst(IWritable to);
        bool Unpipe();

        ulong BufferedReadable { get; }
        ulong ReadCount { get; }

        ulong Read(byte[] destination, ulong start = 0);
        byte[] Read(ulong length);
        byte[] Read();
    }
    public interface IWritable
    {
        ulong WriteCount { get; }

        bool Unpipe(IReadable from);
        bool Write(byte[] source);
        bool Write(byte[] source, ulong start);
        bool Write(byte[] source, ulong start, ulong end);
    }
    public interface IDuplex : IReadable, IWritable { }

    public interface ICollector<T>
    {
        event OutputHandler<T> OnCollect;
    }
    public interface ITransform<T>
    {
        bool Write(T source);
    }
}
