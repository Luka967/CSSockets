namespace WebSockets.Streams
{
    /// <summary>
    /// An alternative for IDisposable, with a boolean indicator.
    /// </summary>
    public interface IEndable
    {
        /// <summary>
        /// Determines if this object has been disposed.
        /// </summary>
        bool Ended { get; }
        /// <summary>
        /// Releases the unmanaged memory this object controls.
        /// </summary>
        void End();
    }

    /// <summary>
    /// Binary data handler.
    /// </summary>
    /// <param name="data">The data.</param>
    public delegate void DataHandler(byte[] data);

    /// <summary>
    /// Represents a stream whose incoming data flow can be paused.
    /// </summary>
    public interface IPausable
    {
        /// <summary>
        /// Specifies stream's pause state.
        /// </summary>
        bool Paused { get; }
        /// <summary>
        /// Force the stream to buffer new incoming data.
        /// </summary>
        void Pause();
        /// <summary>
        /// Release buffered data and don't buffer new data.
        /// </summary>
        void Resume();
    }

    /// <summary>
    /// Represents a stream whose outgoing data flow can be paused.
    /// </summary>
    public interface ICorkable
    {
        /// <summary>
        /// Specifies stream's cork state.
        /// </summary>
        bool Corked { get; }
        /// <summary>
        /// Force the stream to buffer new outgoing data.
        /// </summary>
        void Cork();
        /// <summary>
        /// Release buffered data and don't buffer new data.
        /// </summary>
        void Uncork();
    }

    /// <summary>
    /// Represents a node.js-like blocking readable stream.
    /// </summary>
    public interface IReadable : IEndable
    {
        /// <summary>
        /// Specifies the IWritable the stream will automatically write data to.
        /// </summary>
        IWritable PipedTo { get; }

        /// <summary>
        /// Incoming data handler.
        /// </summary>
        event DataHandler OnData;

        /// <summary>
        /// Automatically write new incoming data to this IWritable stream.
        /// </summary>
        /// <param name="to">The stream to write to.</param>
        void Pipe(IWritable to);
        /// <summary>
        /// Stop writing data to the before specified IWritable stream.
        /// </summary>
        void Unpipe();

        /// <summary>
        /// Reads an arbritrary amount of data, usually the first available.
        /// </summary>
        /// <returns>The binary data.</returns>
        byte[] Read();
        /// <summary>
        /// Reads data with a specified size. Blocks the thread until exactly <paramref name="length"/> data has been read.
        /// </summary>
        /// <param name="length">The length of the data to read.</param>
        /// <returns>The data.</returns>
        byte[] Read(int length);
    }

    /// <summary>
    /// Represents a node.js-like blocking writable stream.
    /// </summary>
    public interface IWritable : IEndable
    {
        /// <summary>
        /// Stop the IReadable from writing data to this IWritable stream.
        /// </summary>
        void Unpipe(IReadable from);

        /// <summary>
        /// Write data to the stream.
        /// </summary>
        /// <param name="data">The data to write.</param>
        void Write(byte[] data);
        /// <summary>
        /// Write part of a byte array to the stream.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <param name="offset">The index to start copying data from.</param>
        /// <param name="length">The amount of bytes to copy.</param>
        void Write(byte[] data, int offset, int length);
    }

    /// <summary>
    /// Represents an IReadable which has an underlying buffer.
    /// </summary>
    public interface IBufferedReadable : IReadable, IPausable
    {
        /// <summary>
        /// Read bytes counter.
        /// </summary>
        long ReadBytes { get; }
        /// <summary>
        /// Returns the size of buffered incoming data.
        /// </summary>
        int IncomingBuffered { get; }
    }

    /// <summary>
    /// Represents an IWritable which has an underlying buffer.
    /// </summary>
    public interface IBufferedWritable : IWritable, ICorkable
    {
        /// <summary>
        /// Written bytes counter.
        /// </summary>
        long WrittenBytes { get; }
        /// <summary>
        /// Returns the size of buffered outgoing data.
        /// </summary>
        int OutgoingBuffered { get; }
    }

    /// <summary>
    /// Represents a blocking duplex stream that queues incoming and outgoing data in one buffer.
    /// </summary>
    public interface IUnifiedDuplex : IReadable, IWritable, IPausable
    {
        /// <summary>
        /// Written bytes counter.
        /// </summary>
        long ProcessedBytes { get; }
        /// <summary>
        /// Returns the size of buffered incoming data.
        /// </summary>
        int Buffered { get; }
    }

    /// <summary>
    /// Represents a blocking duplex stream.
    /// </summary>
    public interface IDuplex : IReadable, IWritable
    {
        /// <summary>
        /// Determines if the readable part of this duplex has ended.
        /// </summary>
        bool ReadableEnded { get; }
        /// <summary>
        /// Determines if the writable part of this duplex has ended.
        /// </summary>
        bool WritableEnded { get; }
    }

    /// <summary>
    /// Represents a node.js-like blocking duplex stream that distinctly buffers incoming and outgoing data.
    /// </summary>
    public interface IBufferedDuplex : IBufferedReadable, IBufferedWritable
    {
        /// <summary>
        /// Determines if the readable part of this duplex has ended.
        /// </summary>
        bool ReadableEnded { get; }
        /// <summary>
        /// Determines if the writable part of this duplex has ended.
        /// </summary>
        bool WritableEnded { get; }
    }
}
