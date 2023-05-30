using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

namespace Gameclaw {
    internal static class FileTransferInternal {
        
#region Internal       
        static Dictionary<string, Result> outgoingStreamsWaitingForConfirmation = new Dictionary<string, Result>();
        static Dictionary<string, IncomingMemoryStreamHandler> incomingMemoryStreams = new Dictionary<string, IncomingMemoryStreamHandler>();
        
        public static async void SendData(NetworkConnectionToClient conn, FileTransferProgress progressTracker, object data, bool toClient, string identifier, Action<Result> callback) {
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

                // Get md5 to confirm the final result and identify chunks
                string md5 = GenerateMD5(stream);
                outgoingStreamsWaitingForConfirmation.Add(md5, Result.Waiting);

                int chunkSize = 4096;
                long totalRead = 0;
                long streamLength = stream.Length;
                int index = 0;

                LogMessage($"Begun sending chunks (bytes in Stream: {streamLength})", LogType.Log);
                progressTracker.ProgressAction = ProgressAction.Sending;

                byte[] dataInBytes = new byte[chunkSize];
                stream.Seek(0, SeekOrigin.Begin);
                while (true) {
                    index++;

                    chunkSize = stream.Read(dataInBytes, 0, dataInBytes.Length);
                    if (chunkSize > 0) {
                        LogMessage($"Sending chunk: {index}", LogType.Verbose);
                        if (toClient) {
                            Target_SendStreamData(conn, dataInBytes, index, md5, identifier, streamLength, true, false);
                        }
                        else {
                            Cmd_SendStreamData(conn, dataInBytes, index, md5, identifier, streamLength, true, false);
                        }
                        totalRead += dataInBytes.Length;
                        progressTracker.Progress = totalRead / (float)streamLength;
                    }
                    else {
                        progressTracker.Progress = 1f;
                        LogMessage($"Sending final chunk notification: {index - 1}", LogType.Verbose);

                        if (toClient) {
                            Target_SendStreamData(conn, null, index - 1, md5, identifier, streamLength, true, true);
                        }
                        else {
                            Cmd_SendStreamData(conn, null, index - 1, md5, identifier, streamLength, true, true);
                        }
                        break;
                    }
                }
                // inform progress status
                progressTracker.ProgressAction = ProgressAction.WaitingForConfirmation;

                // Wait time before timing out (in milliseconds)
                int waitLimit = 10000;
                int tickLength = 10;
                int ticks = 0;

                while (true) {
                    if (outgoingStreamsWaitingForConfirmation.TryGetValue(md5, out Result callbackResult)) {
                        if (callbackResult != Result.Waiting) {
                            result = callbackResult;
                            break;
                        }
                    }
                    
                    ticks++;

                    if (ticks * tickLength > waitLimit) {
                        outgoingStreamsWaitingForConfirmation.Remove(md5);
                        LogMessage($"Failed to confirm result of transfer (Timed out)", LogType.Error);
                        break;
                    }
                    
                    await Task.Delay(tickLength);
                }
            }

            Invoke: ;
            progressTracker.ProgressAction = ProgressAction.Finished;
            callback?.Invoke(result);
        }       
        public static async void SendData(NetworkConnectionToClient conn, FileTransferProgress progressTracker, Stream stream, bool toClient, string identifier, Action<Result> callback) {
            // setup result for callback
            Result result = new Result();

            if (NetworkManager.singleton == null || !NetworkManager.singleton.isNetworkActive) {
                result = Result.Failed;
                LogMessage("Failed because the Network is not active", LogType.Error);
                goto Invoke;
            }

            using (stream) {
                // inform progress status
                progressTracker.ProgressAction = ProgressAction.Serializing;

                // Get md5 to confirm the final result and identify chunks
                string md5 = GenerateMD5(stream);
                
                outgoingStreamsWaitingForConfirmation.Add(md5, Result.Waiting);

                int chunkSize = 4096;
                long totalRead = 0;
                long streamLength = stream.Length;
                int index = 0;

                LogMessage($"Begun sending chunks (bytes in Stream: {streamLength})", LogType.Log);
                progressTracker.ProgressAction = ProgressAction.Sending;

                byte[] dataInBytes = new byte[chunkSize];
                stream.Seek(0, SeekOrigin.Begin);
                while (true) {
                    index++;

                    chunkSize = await stream.ReadAsync(dataInBytes, 0, dataInBytes.Length);
                    if (chunkSize > 0) {
                        LogMessage($"Sending chunk: {index}", LogType.Verbose);
                        if (toClient) {
                            Target_SendStreamData(conn, dataInBytes, index, md5, identifier, streamLength, false, false);
                        }
                        else {
                            Cmd_SendStreamData(conn, dataInBytes, index, md5, identifier, streamLength, false, false);
                        }
                        totalRead += dataInBytes.Length;
                        progressTracker.Progress = totalRead / (float)streamLength;
                    }
                    else {
                        progressTracker.Progress = 1f;
                        LogMessage($"Sending final chunk notification: {index - 1}", LogType.Verbose);

                        if (toClient) {
                            Target_SendStreamData(conn, null, index - 1, md5, identifier, streamLength, false, true);
                        }
                        else {
                            Cmd_SendStreamData(conn, null, index - 1, md5, identifier, streamLength, false, true);
                        }
                        break;
                    }
                }
                // inform progress status
                progressTracker.ProgressAction = ProgressAction.WaitingForConfirmation;

                // Wait time before timing out (in milliseconds)
                int waitLimit = 10000;
                int tickLength = 10;
                int ticks = 0;

                while (true) {
                    await Task.Delay(tickLength);
                    
                    if (outgoingStreamsWaitingForConfirmation.TryGetValue(md5, out Result callbackResult)) {
                        if (callbackResult != Result.Waiting) {
                            result = callbackResult;
                            break;
                        }
                    }
                    
                    ticks++;

                    if (ticks * tickLength > waitLimit) {
                        outgoingStreamsWaitingForConfirmation.Remove(md5);
                        LogMessage($"Failed to confirm result of transfer (Timed out)", LogType.Error);
                        break;
                    }
                }
            }

            Invoke:
            progressTracker.ProgressAction = ProgressAction.Finished;
            callback?.Invoke(result);
        }
        
        static void InformTransferResult(string md5, Result result) {
            if (outgoingStreamsWaitingForConfirmation.ContainsKey(md5)) {
                outgoingStreamsWaitingForConfirmation[md5] = result;
            }
        }
        

        [Command]
        static void Cmd_InformTransferResult(string md5, Result result) {
            if (outgoingStreamsWaitingForConfirmation.ContainsKey(md5)) {
                outgoingStreamsWaitingForConfirmation[md5] = result;
            }
        }

        [TargetRpc]
        static void Target_InformTransferResult(NetworkConnection target, string md5, Result result) {
            if (outgoingStreamsWaitingForConfirmation.ContainsKey(md5)) {
                outgoingStreamsWaitingForConfirmation[md5] = result;
            }
        }

        [Command]
        static void Cmd_SendStreamData(NetworkConnection sender, byte[] bytes, int index, string md5, string identifier, long streamSize, bool serialized, bool final) {
            if (ReceiveStreamData(bytes, index, md5, identifier, streamSize, serialized, final)) {
                Target_ReceiveFinalChunk(new NetworkConnectionToClient(sender.connectionId), md5);
            }
        }

        [TargetRpc]
        static void Target_SendStreamData(NetworkConnection target, byte[] bytes, int index, string md5, string identifier, long streamSize, bool serialized, bool final) {
            if (ReceiveStreamData(bytes, index, md5, identifier, streamSize, serialized, final)) {
                Cmd_ReceiveFinalChunk(target, md5);
            }
        }

        static bool ReceiveStreamData(byte[] bytes, int index, string md5, string identifier, long streamSize, bool serialized, bool final) {
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
                LogMessage("An incoming file request timed out. It was more than 60 seconds since it received any data.", LogType.Error);
                FileTransfer.OnFailedToRecieveFile?.Invoke(identifier);
                return false;
            }

            // cache finalId if it's final
            if (final) {
                incomingMemoryStreams[md5].finalId = index;
                LogMessage($"Received final chunk Id: {index}", LogType.Verbose);
            }

            // check chunk and write to stream
            if (bytes != null) {
                if (index == incomingMemoryStreams[md5].lastChunkId + 1) {
                    LogMessage($"Adding chunk to stream: {index}", LogType.Verbose);
                    incomingMemoryStreams[md5].lastChunkId = index;
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
                return true;
            }
            return false;
        }

        [Command]
        static void Cmd_ReceiveFinalChunk(NetworkConnection target, string md5) {
            // check if the stream was successful with the md5
            string finalmd5 = GenerateMD5(incomingMemoryStreams[md5].stream);
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
                        Target_InformTransferResult(target, md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"Failed to deserialize data from stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        Target_InformTransferResult(target, md5, Result.Failed);
                        incomingMemoryStreams[md5].stream?.Dispose();
                    }
                } 
                // STREAM
                else
                {
                    try {
                        incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
                        FileTransfer.OnRecieveStream?.Invoke(incomingMemoryStreams[md5].stream, incomingMemoryStreams[md5].identifier);
                        Target_InformTransferResult(target, md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"Failed finalise data stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        Target_InformTransferResult(target, md5, Result.Failed);
                        incomingMemoryStreams[md5].stream?.Dispose();
                    }
                }
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
            }
            else {
                // Failed
                LogMessage($"transfer failed due to mismatched MD5", LogType.Error);
                Target_InformTransferResult(target, md5, Result.Failed);
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
            }
        }

        [TargetRpc]
        static void Target_ReceiveFinalChunk(NetworkConnection target, string md5) {
            // check if the stream was successful with the md5
            string finalmd5 = GenerateMD5(incomingMemoryStreams[md5].stream);
            if (finalmd5 == md5) {
                // Succeeded
                LogMessage("stream succeeded with matching MD5 hash", LogType.Log);
                
                if (incomingMemoryStreams[md5].serialized) {
                    incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Deserializing;

                    BinaryFormatter formatter = GetBinaryFormatter();

                    try {
                        incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
                        object deserializedStream = formatter.Deserialize(incomingMemoryStreams[md5].stream);
                        FileTransfer.OnRecieveObject?.Invoke(deserializedStream, incomingMemoryStreams[md5].identifier);
                        Cmd_InformTransferResult(md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"[FileTransfer]: Failed to deserialize data from stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        Cmd_InformTransferResult(md5, Result.Failed);
                    }
                }
                // STREAM
                else
                {
                    try {
                        incomingMemoryStreams[md5].stream.Seek(0, SeekOrigin.Begin);
                        FileTransfer.OnRecieveStream?.Invoke(incomingMemoryStreams[md5].stream, incomingMemoryStreams[md5].identifier);
                        Cmd_InformTransferResult(md5, Result.Succeeded);
                    }
                    catch(Exception e) {
                        LogMessage($"Failed finalise data stream. Caught exception: {e.Message}\nStacktrace: {e.StackTrace}", LogType.Error);
                        FileTransfer.OnFailedToRecieveFile?.Invoke(incomingMemoryStreams[md5].identifier);
                        Cmd_InformTransferResult(md5, Result.Failed);
                        incomingMemoryStreams[md5].stream?.Dispose();
                    }
                }
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
            }
            else {
                // Failed
                LogMessage($"transfer failed due to mismatched MD5", LogType.Error);
                Cmd_InformTransferResult(md5, Result.Failed);
                incomingMemoryStreams[md5].progressTracker.ProgressAction = ProgressAction.Finished;
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
        static string GenerateMD5(Stream data) {
            using MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    
        //---------------------------------[ Binary Formatter ]--------------------------------------//
        static BinaryFormatter GetBinaryFormatter()
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
