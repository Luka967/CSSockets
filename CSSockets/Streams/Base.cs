namespace CSSockets.Streams
{
    /// <summary>
    /// Represents a delegate for methods that get called when a major event happens in a stream.
    /// </summary>
    public delegate void ControlHandler();
    /// <summary>
    /// Represents a delegate for methods that handle incoming data immediately upon arrival.
    /// </summary>
    /// <param name="data">The incoming data.</param>
    public delegate void DataHandler(byte[] data);

    /// <summary>
    /// Represents an object that can be disposed.
    /// </summary>
    public interface IEndable
    {
        /// <summary>
        /// Fired after the object fully disposes.
        /// </summary>
        event ControlHandler OnEnd;
        /// <summary>
        /// Determines whether the object has been disposed or not.
        /// </summary>
        bool Ended { get; }
        /// <summary>
        /// Tries to dispose of the object.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool End();
    }
    /// <summary>
    /// Represents an object that has another layer of disposal.
    /// </summary>
    public interface IFinishable
    {
        /// <summary>
        /// Determines whether the object has done the first dispose or not.
        /// </summary>
        bool Finished { get; }
        /// <summary>
        /// Tries to dispose of the object.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Finish();
    }
    /// <summary>
    /// Represents a buffered stream that will fire an event when no outgoing data is buffered.
    /// </summary>
    public interface IDrainable
    {
        /// <summary>
        /// Fired when no outgoing data is buffered.
        /// </summary>
        event ControlHandler OnDrain;
    }
    /// <summary>
    /// Represents a buffered stream that can be paused.
    /// </summary>
    public interface IPausable
    {
        /// <summary>
        /// Determines whether the stream will block the calling thread if it calls a blocking read.
        /// </summary>
        bool IsPaused { get; }
        /// <summary>
        /// Tries to pause the stream.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Pause();
        /// <summary>
        /// Tries to resume / unpause the stream.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Resume();
    }
    /// <summary>
    /// Represents a buffered stream that can be corked.
    /// </summary>
    public interface ICorkable
    {
        /// <summary>
        /// Determines whether the stream will block the calling thread if it calls a blocking write.
        /// </summary>
        bool IsCorked { get; }
        /// <summary>
        /// Tries to cork the stream.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Cork();
        /// <summary>
        /// Tries to uncork the stream.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Uncork();
    }
    /// <summary>
    /// Represents a readable stream that can automatically forward its incoming data to another writable stream.
    /// </summary>
    public interface IPiping
    {
        /// <summary>
        /// Determines the writable stream the readable is piped to.
        /// </summary>
        IWritable PipedTo { get; }
        /// <summary>
        /// Tries to set the writable stream it's forwarding data to.
        /// </summary>
        /// <param name="to">The writable stream.</param>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Pipe(IWritable to);
        /// <summary>
        /// Tries to unset the writable stream it's forwarding data to.
        /// </summary>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Unpipe();
    }
    /// <summary>
    /// Represents a writable stream that can cancel out the data forwarding to itself coming from a readable.
    /// </summary>
    public interface IPipable
    {
        /// <summary>
        /// Tries to unpipe itself from the readable stream.
        /// </summary>
        /// <param name="from">The readable stream.</param>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Unpipe(IReadable from);
    }

    /// <summary>
    /// Represents a readable stream.
    /// </summary>
    public interface IReadable : IEndable, IPiping
    {
        /// <summary>
        /// The amount of octets that have been read.
        /// </summary>
        ulong ReadCount { get; }
        /// <summary>
        /// Fired when data arrives.
        /// </summary>
        event DataHandler OnData;
        /// <summary>
        /// Fired when forwarding data fails.
        /// </summary>
        event ControlHandler OnFail;
        /// <summary>
        /// Performs a blocking read returning everything buffered.
        /// </summary>
        /// <returns>The data that was buffered.</returns>
        byte[] Read();
        /// <summary>
        /// Performs a blocking read returning set size data.
        /// </summary>
        /// <param name="length">The length of the data.</param>
        /// <returns>The data.</returns>
        byte[] Read(ulong length);
        /// <summary>
        /// Performs a non-blocking read returning set size data.
        /// </summary>
        /// <param name="destination">The data buffer to write to.</param>
        /// <returns>The amount of read octets.</returns>
        ulong Read(byte[] destination);
    }
    /// <summary>
    /// Represents a writable stream.
    /// </summary>
    public interface IWritable : IEndable, IPipable
    {
        /// <summary>
        /// The amount of octets that have been written.
        /// </summary>
        ulong WriteCount { get; }
        /// <summary>
        /// Performs a blocking write with a specified data buffer.
        /// </summary>
        /// <param name="source">The data buffer to copy from.</param>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Write(byte[] source);
        /// <summary>
        /// Performs a blocking write with a specified part of a data buffer.
        /// </summary>
        /// <param name="source">The data buffer to copy from.</param>
        /// <param name="start">The index to start copying from.</param>
        /// <param name="end">The index to stop copying from.</param>
        /// <returns>A boolean indicating if the operation was successful.</returns>
        bool Write(byte[] source, ulong start, ulong end);
    }
    /// <summary>
    /// Represents a double-decker on which both read and write operations are possible.
    /// </summary>
    public interface IDuplex : IReadable, IWritable { }

    /// <summary>
    /// Represents a readable stream that buffers data before processing it further.
    /// </summary>
    public interface IBufferedReadable : IReadable, IPausable
    {
        /// <summary>
        /// The amount of octets that will immediately be read.
        /// </summary>
        ulong BufferedReadable { get; }
    }
    /// <summary>
    /// Represents a writable stream that buffers data before processing it further.
    /// </summary>
    public interface IBufferedWritable : IWritable, IDrainable, ICorkable
    {
        /// <summary>
        /// The amount of octets that will immediately be written.
        /// </summary>
        ulong BufferedWritable { get; }
    }
    /// <summary>
    /// Represents a duplex stream that buffers data before processing it further.
    /// </summary>
    public interface IBufferedDuplex : IBufferedReadable, IBufferedWritable { }
}
