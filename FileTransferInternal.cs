using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Gameclaw {
    internal static class FileTransferInternal {

        const int BufferSize = 4096; // 4 KiB
        const int MillisecondsBeforeYield = 16; // less then 1 frame of time at 60 FPS
        const long MaxBytesPerSecond = 2097152; // 2 MiB
        
        public static void Setup() {
                NetworkServer.RegisterHandler<FileChunk>(ReceiveFileChunk);
                NetworkServer.RegisterHandler<FileResult>(ReceiveFileResult);
                NetworkClient.RegisterHandler<FileChunk>(ReceiveFileChunk);
                NetworkClient.RegisterHandler<FileResult>(ReceiveFileResult);
        }

        public static void Shutdown() {
                NetworkServer.UnregisterHandler<FileChunk>();
                NetworkServer.UnregisterHandler<FileResult>();
                NetworkClient.UnregisterHandler<FileChunk>();
                NetworkClient.UnregisterHandler<FileResult>();
        }
        
        public static Dictionary<string, OutgoingMemoryStreamToken> outgoingStreamsWaitingForConfirmation = new Dictionary<string, OutgoingMemoryStreamToken>();
        public static Dictionary<string, IncomingMemoryStreamHandler> incomingMemoryStreams = new Dictionary<string, IncomingMemoryStreamHandler>();

#region Internal
        public static async void SendData(NetworkConnection connection, FileTransferProgress progressTracker, object data, string identifier, Action<Result> callback) {
            // setup result for callback
            Result result = new Result();

            if (NetworkManager.singleton == null || !NetworkManager.singleton.isNetworkActive) {
                result = Result.Failed;
                LogMessage("Failed because the Network is not active", LogType.Error);
                goto Invoke;
            }

            using (MemoryStream stream = new MemoryStream()) {
                // inform progress status
                progressTracker.ProgressAction = ProgressAction.Serializing;

                // Get Binary formatter
                BinaryFormatter formatter = GetBinaryFormatter();

                // Attempt to serialize the specified data
                try {
                    formatter.Serialize(stream, data);
                }
                catch(Exception e) {
                    LogMessage($"Failed to serialize the specified data. Refer to the following BinaryFormatter exception:\n{e.Message}", LogType.Error);
                    result = Result.Failed;
                    goto Invoke;
                }

                string md5 = await SendStreamInChunks(connection, stream, progressTracker, identifier);
                if (progressTracker.ProgressAction == ProgressAction.Finished) {
                    // FAILURE already being sent
                    result = Result.Failed;
                    goto Invoke;
                }
                result = await WaitForOutgoingStreamResult(md5);
            }

            Invoke:
            progressTracker.ProgressAction = ProgressAction.Finished;
            callback?.Invoke(result);
        }
        public static async void SendData(NetworkConnection connection, FileTransferProgress progressTracker, Stream stream, string identifier, Action<Result> callback) {
            // setup result for callback
            Result result = new Result();

            if (NetworkManager.singleton == null || !NetworkManager.singleton.isNetworkActive) {
                result = Result.Failed;
                LogMessage("Failed because the Network is not active", LogType.Error);
                goto Invoke;
            }

            string md5 = await SendStreamInChunks(connection, stream, progressTracker, identifier);
            if (progressTracker.ProgressAction == ProgressAction.Finished) {
                // FAILURE already being sent
                result = Result.Failed;
                goto Invoke;
            }
            result = await WaitForOutgoingStreamResult(md5);
            
            
            Invoke:
            progressTracker.ProgressAction = ProgressAction.Finished;
            callback?.Invoke(result);
        }
        static async Task<string> SendStreamInChunks(NetworkConnection connection, Stream stream, FileTransferProgress progressTracker, string identifier) {
            using (stream) {

                // Get md5 to confirm the final result and identify chunks
                string md5 = await GenerateMD5(stream);
                if (outgoingStreamsWaitingForConfirmation.ContainsKey(md5)) {
                    LogMessage($"Failed, the same file is already being sent", LogType.Error);
                    progressTracker.ProgressAction = ProgressAction.Finished;
                    return md5;
                }
                outgoingStreamsWaitingForConfirmation.Add(md5, new OutgoingMemoryStreamToken(Result.Waiting, connection));

                int chunkSize = BufferSize;
                long totalRead = 0;
                long streamLength = stream.Length;
                int index = 0;

                LogMessage($"Begun sending chunks (bytes in Stream: {streamLength})", LogType.Log);
                progressTracker.ProgressAction = ProgressAction.Sending;

                Stopwatch yieldCheck = new Stopwatch();
                yieldCheck.Start();
                int maxBytesPerSecondThrottleTime = (int)(1000 / (MaxBytesPerSecond / chunkSize));
                
                byte[] dataInBytes = new byte[chunkSize];
                stream.Seek(0, SeekOrigin.Begin);
                FileChunk chunk = new FileChunk();
                chunk.fileSize = streamLength;
                chunk.identifier = identifier;
                chunk.md5 = md5;
                chunk.serialized = false;
                while (true) {
                    index++;
                    chunk.index = index;
                    chunkSize = stream.Read(dataInBytes, 0, dataInBytes.Length);
                    if (chunkSize > 0) {
                        Array.Resize(ref dataInBytes, chunkSize);
                        chunk.data = dataInBytes;
                        chunk.final = false;
                        LogMessage($"Sending chunk: {index}", LogType.Verbose);
                        totalRead += dataInBytes.Length;
                        progressTracker.Progress = totalRead / (float)streamLength;
                    }
                    else {
                        chunk.data = null;
                        chunk.final = true;
                        progressTracker.Progress = 1f;
                        LogMessage($"Sending final chunk notification: {index - 1}", LogType.Verbose);
                    }
                    connection.Send(chunk);
                    if (chunkSize <= 0) break;

                    await Task.Delay(maxBytesPerSecondThrottleTime);
                    if (yieldCheck.ElapsedMilliseconds >= MillisecondsBeforeYield) {
                        yieldCheck.Restart();
                        await Task.Yield();
                    }
                }
                
                yieldCheck.Stop();
                
                // inform progress status
                progressTracker.ProgressAction = ProgressAction.WaitingForConfirmation;

                return md5;
            }
        }
        static async Task<Result> WaitForOutgoingStreamResult(string md5) {
            // Wait time before timing out (in milliseconds)
            int waitLimit = 10000;
            int tickLength = 10;
            int ticks = 0;
            Result result = Result.Waiting;
            while (true) {
                if (outgoingStreamsWaitingForConfirmation.TryGetValue(md5, out var callbackResult)) {
                    if (callbackResult.result != Result.Waiting) {
                        result = callbackResult.result;
                        break;
                    }
                }

                ticks++;

                if (ticks * tickLength > waitLimit) {
                    outgoingStreamsWaitingForConfirmation.Remove(md5);
                    result = Result.Failed;
                    LogMessage($"Failed to confirm result of transfer (Timed out)", LogType.Error);
                    break;
                }

                await Task.Delay(tickLength);
            }
            return result;
        }
        
        public static void ReceiveFileChunk(FileChunk chunk) {
            ReceiveStreamData(chunk.data, chunk.index, chunk.md5, chunk.identifier, chunk.fileSize, chunk.serialized, chunk.final);
        }
        public static void ReceiveFileChunk(NetworkConnection conn, FileChunk chunk) {
            ReceiveStreamData(chunk.data, chunk.index, chunk.md5, chunk.identifier, chunk.fileSize, chunk.serialized, chunk.final);
            if (incomingMemoryStreams.ContainsKey(chunk.md5)) {
                incomingMemoryStreams[chunk.md5].connection_serverUseOnly = conn;
            }
        }
        
        public static void ReceiveFileResult(FileResult result) {
            if (outgoingStreamsWaitingForConfirmation.ContainsKey(result.md5)) {
                outgoingStreamsWaitingForConfirmation[result.md5].result = (Result)result.result;
            }
        }
        public static void ReceiveFileResult(NetworkConnection conn, FileResult result) {
            if (outgoingStreamsWaitingForConfirmation.ContainsKey(result.md5)) {
                outgoingStreamsWaitingForConfirmation[result.md5].result = (Result)result.result;
            }
        }

        static void ReceiveStreamData(byte[] bytes, int index, string md5, string identifier, long streamSize, bool serialized, bool final) {
            LogMessage($"Received chunk: [{index}:{bytes?.Length}]", LogType.Verbose);

            // Receiving stream data
            float progress = 0f; //<-- use this to track progress

            // open new stream if not created
            if (!incomingMemoryStreams.ContainsKey(md5)) {
                incomingMemoryStreams.Add(md5, new IncomingMemoryStreamHandler(identifier, serialized));
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Recieving;
                FileTransfer.OnBeginRecievingFile?.Invoke(incomingMemoryStreams[md5].progressTracker);
            }

            if (incomingMemoryStreams[md5].progressTracker.ProgressAction == ProgressAction.TimedOut) {
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
                FileTransfer.OnFailedToRecieveFile?.Invoke(identifier);
                return;
            }

            // cache finalId if it's final
            if (final) {
                incomingMemoryStreams[md5].finalId = index;
                if (bytes == null) {
                    // If the last chunk is empty we can reduce the final index by 1
                    incomingMemoryStreams[md5].finalId--;
                }
                LogMessage($"Received final chunk Id: {index}", LogType.Verbose);
            }

            // check chunk and write to stream
            if (bytes != null) {
                if (index == incomingMemoryStreams[md5].lastChunkId + 1) {
                    LogMessage($"Adding chunk to stream: {index}", LogType.Verbose);
                    incomingMemoryStreams[md5].lastChunkId = index;
                    incomingMemoryStreams[md5].millisecondsSinceLastChunk = 0;
                    incomingMemoryStreams[md5].stream.Write(bytes, 0, bytes.Length);
                    progress = incomingMemoryStreams[md5].stream.Length / (float)streamSize;
                }
                else {
                    LogMessage($"Adding chunk to queue: {index}", LogType.Verbose);
                    incomingMemoryStreams[md5].queuedChunks.Add(index, bytes);
                }
            }

            // check queued chunks to add (these are chunks that may have been received out of order)
            while (incomingMemoryStreams[md5].queuedChunks.ContainsKey(incomingMemoryStreams[md5].lastChunkId + 1)) {
                int nextIndex = incomingMemoryStreams[md5].lastChunkId + 1;
                LogMessage($"Adding chunk to stream: {nextIndex}", LogType.Verbose);
                byte[] chunkBytes = incomingMemoryStreams[md5].queuedChunks[nextIndex];
                incomingMemoryStreams[md5].stream.Write(chunkBytes, 0, chunkBytes.Length);
                progress = incomingMemoryStreams[md5].stream.Length / (float)streamSize;
                incomingMemoryStreams[md5].lastChunkId = nextIndex;
                incomingMemoryStreams[md5].queuedChunks.Remove(nextIndex);
            }
            incomingMemoryStreams[md5].progressTracker.Progress = progress;

            // check if this was the last chunk
            if (incomingMemoryStreams[md5].lastChunkId == incomingMemoryStreams[md5].finalId) {
                incomingMemoryStreams[md5].progressTracker.Progress = 1f;
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Deserializing;
                ProcessFile(md5);
            }
        }

        public static async void ProcessFile(string md5) {
            LogMessage($"Processing received file, size {incomingMemoryStreams[md5].stream.Length}", LogType.Log);
            // check if the stream was successful with the md5
            incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
            string finalmd5 = await GenerateMD5(incomingMemoryStreams[md5].stream);
            LogMessage($"Comparing MD5s:\n{md5}\n{finalmd5}", LogType.Log);
            if (finalmd5 == md5) {
                // Succeeded
                LogMessage("stream succeeded with matching MD5 hash", LogType.Log);

                // BINARY FORMATTER
                if (incomingMemoryStreams[md5].serialized) {
                    incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Deserializing;

                    BinaryFormatter formatter = GetBinaryFormatter();

                    try {
                        incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
                        object deserializedStream = formatter.Deserialize(incomingMemoryStreams[md5].stream);
                        incomingMemoryStreams[md5].stream?.Dispose();
                        FileTransfer.OnRecieveObject?.Invoke(deserializedStream, incomingMemoryStreams[md5].identifier);
                        
                        InformSenderOfResult(md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"Failed to deserialize data from stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        
                        InformSenderOfResult(md5, Result.Failed);
                        incomingMemoryStreams[md5].stream?.Dispose();
                    }
                } 
                // STREAM
                else
                {
                    try {
                        incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
                        FileTransfer.OnRecieveStream?.Invoke(incomingMemoryStreams[md5].stream, incomingMemoryStreams[md5].identifier);
                        
                        InformSenderOfResult(md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"Failed finalise data stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        
                        InformSenderOfResult(md5, Result.Failed);
                        incomingMemoryStreams[md5].stream?.Dispose();
                    }
                }
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
            }
            else {
                // Failed
                LogMessage($"transfer failed due to mismatched MD5", LogType.Error);
                
                InformSenderOfResult(md5, Result.Failed);
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
            }
            if (incomingMemoryStreams.ContainsKey(md5)) {
                incomingMemoryStreams.Remove(md5);
            }
        }

        static void InformSenderOfResult(string md5, Result result) {
            LogMessage($"Informing sender of result {result.ToString()}", LogType.Log);
            if (!NetworkClient.localPlayer.isServer) {
                NetworkClient.Send(new FileResult {
                    result = (int)result,
                    md5 = md5
                });
            }
            else {
                incomingMemoryStreams[md5].connection_serverUseOnly.Send(new FileResult {
                    result = (int)result,
                    md5 = md5
                });
            }
        }
#endregion // Internal
        
#region Utility
        
        //-------------------------------------[ Logging ]-------------------------------------------//
        public static void LogMessage(string message, LogType type) {
            if (FileTransfer.AllowLogMessages) {
                string prefix = "<color=white>[FileTransfer]</color>: ";
                switch (type) {
                    case LogType.Verbose:
                        if (FileTransfer.AllowVerboseLogMessages) {
                            Debug.Log($"{prefix}{message}");
                        }
                        break;
                    case LogType.Log:
                        Debug.Log($"{prefix}{message}");
                        break;
                    case LogType.Warning:
                        Debug.LogWarning($"{prefix}{message}");
                        break;
                    case LogType.Error:
                        Debug.LogError($"{prefix}{message}");
                        break;
                }
            }
        }

        //-----------------------------------[ Generate MD5 ]----------------------------------------//
        public static async Task<string> GenerateMD5(Stream data) {
            using MD5 md5 = MD5.Create();
            byte[] hash = await Task.Run(() => md5.ComputeHash(data));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    
        //---------------------------------[ Binary Formatter ]--------------------------------------//
        public static BinaryFormatter GetBinaryFormatter()
        {
            BinaryFormatter formatter = new BinaryFormatter();

            SurrogateSelector surrogateSelector = new SurrogateSelector();
            Vector3SerializationSurrogate vector3SS = new Vector3SerializationSurrogate();
            Vector2SerializationSurrogate vector2SS = new Vector2SerializationSurrogate();
            Vector3IntSerializationSurrogate vector3ISS = new Vector3IntSerializationSurrogate();
            Vector2IntSerializationSurrogate vector2ISS = new Vector2IntSerializationSurrogate();
            SpriteSerializationSurrogate spriteSS = new SpriteSerializationSurrogate();

            surrogateSelector.AddSurrogate(typeof(Vector3), new StreamingContext(StreamingContextStates.All), vector3SS);
            surrogateSelector.AddSurrogate(typeof(Vector2), new StreamingContext(StreamingContextStates.All), vector2SS);
            surrogateSelector.AddSurrogate(typeof(Vector3Int), new StreamingContext(StreamingContextStates.All), vector3ISS);
            surrogateSelector.AddSurrogate(typeof(Vector2Int), new StreamingContext(StreamingContextStates.All), vector2ISS);
            surrogateSelector.AddSurrogate(typeof(Sprite), new StreamingContext(StreamingContextStates.All), spriteSS);
            formatter.SurrogateSelector = surrogateSelector;

            return formatter;
        }

#endregion // Utility

    }
}
