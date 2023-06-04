using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Mirror;

namespace Gameclaw {
    public static class FileTransfer {

        public const bool AllowLogMessages = true;
        public const bool AllowVerboseLogMessages = false;

        /// <summary> This will get invoked when the server or client begins receiving chunks of data for
        /// a file. The FileTransferProgress can be polled regularly to check the status of the transfer
        /// as well as the progress </summary>
        public static Action<FileTransferProgress> OnBeginRecievingFile;
        /// <summary> This will get invoked when using the SendData overload that takes a serializable object </summary>
        /// <remarks>The string is the identifier passed into the SendData method</remarks>
        public static Action<object, string> OnRecieveObject;
        /// <summary> This will get invoked when using the SendData overload that takes a stream </summary>
        /// <remarks>The string is the identifier passed into the SendData method</remarks>
        public static Action<Stream, string> OnRecieveStream;
        /// <summary> This will get invoked when the server or client begins receiving data but fails
        /// before it can get the entire file </summary>
        /// <remarks>The string is the identifier passed into the SendData method</remarks>
        public static Action<string> OnFailedToRecieveFile;
        /// <summary>
        /// This will send the specified data from a client to the server or a server to a client.
        /// It cannot send from client to client.
        /// </summary>
        /// <param name="conn">The connection id of the client sending the data</param>
        /// <param name="data">the data you want to send. Must be serializable with the <see cref="BinaryFormatter"/></param>
        /// <param name="identifier">this is a custom string that you will receive to inform the
        /// server what type of file they received and how to process it (Entirely up to you)</param>
        /// <param name="result">The callback to be invoked when the process is finished</param>
        /// <returns>A FileTransferProgress reference that can be used to check the progress and
        /// current action being performed <seealso cref="FileTransferProgress"/></returns>
        /// <remarks>The <see cref="BinaryFormatter"/> cannot serialize/deserialize MonoBehaviour
        /// members without a surrogate. Surrogates included with this FileTransfer are: Sprite,
        /// Vector2, Vector2Int, Vector3, Vector3Int. You can look at the provided surrogates as
        /// examples if you'd like to include your own. They will also need to be added to the
        /// <see cref="GetBinaryFormatter"/> method at the bottom of this (FileTransfer.cs) script</remarks>
        public static FileTransferProgress SendDataToConnection(NetworkConnection conn, object data, string identifier, Action<Result> result) {
            FileTransferProgress progressTracker = new FileTransferProgress();
            FileTransferInternal.SendData(conn, progressTracker, data, identifier, result);
            return progressTracker;
        }
        
        /// <summary>
        /// This will send the specified data from a client to the server or a server to a client.
        /// It cannot send from client to client.
        /// </summary>
        /// <param name="conn">The connection id of the client sending the data</param>
        /// <param name="data">the data you want to send. Can be any type derived from a Stream,
        /// such as FileStream or MemoryStream (So long as the stream can Seek)</param>
        /// <param name="identifier">this is a custom string that you will receive to inform the
        /// server what type of file they received and how to process it (Entirely up to you)</param>
        /// <param name="result">The callback to be invoked when the process is finished</param>
        /// <returns>A FileTransferProgress reference that can be used to check the progress and
        /// current action being performed <seealso cref="FileTransferProgress"/></returns>
        public static FileTransferProgress SendDataToConnection(NetworkConnection conn, Stream data, string identifier, Action<Result> result) {
            FileTransferProgress progressTracker = new FileTransferProgress();
            FileTransferInternal.SendData(conn, progressTracker, data, identifier, result);
            return progressTracker;
        }

        public static void BeginListening() {
            FileTransferInternal.Setup();
        }
        public static void StopListening() {
            FileTransferInternal.Shutdown();
        }
    }
}
