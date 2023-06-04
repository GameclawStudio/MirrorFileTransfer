using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mirror;

namespace Gameclaw {
    internal class IncomingMemoryStreamHandler {
        const int Timeout = 60000; // Set in milliseconds
        
        public int finalId = -1;
        public int lastChunkId;
        public string identifier = "";
        public bool serialized;
        public int millisecondsSinceLastChunk = 0;
        public FileTransferProgress progressTracker = new FileTransferProgress();
        public MemoryStream stream = new MemoryStream();
        public Dictionary<int, byte[]> queuedChunks = new Dictionary<int, byte[]>();
        public NetworkConnection connection_serverUseOnly;

        async void TimeoutCheck() {
            while (progressTracker.ProgressAction != ProgressAction.Finished) {
                await Task.Delay(1000);
                millisecondsSinceLastChunk += 1000;
                
                // If timed out dispose the stream and set the status to finished
                if (millisecondsSinceLastChunk >= Timeout) {
                    FileTransferInternal.LogMessage("An incoming file request timed out. It was more than 60 seconds since it received any data.", LogType.Error);
                    progressTracker.ProgressAction = ProgressAction.TimedOut;
                    stream?.Dispose();
                    break;
                }
            }
        }

        public IncomingMemoryStreamHandler(string identifier, bool serialized) {
            this.identifier = identifier;
            this.serialized = serialized;
            
            // Begin timeout checking
            TimeoutCheck();
        }
    }
}
