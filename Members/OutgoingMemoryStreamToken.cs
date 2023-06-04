using Mirror;

namespace Gameclaw {
    internal class OutgoingMemoryStreamToken {
        public Result result;
        public NetworkConnection connection;

        public OutgoingMemoryStreamToken(Result result, NetworkConnection connection) {
            this.connection = connection;
            this.result = result;
        }
    }
}
