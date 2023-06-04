using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace Gameclaw {
    public struct FileChunk : NetworkMessage {
        public byte[] data;
        public string md5;
        public string identifier;
        public int index;
        public long fileSize;
        public bool serialized;
        public bool final;
    }
}
