using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace Gameclaw {
    public struct FileResult : NetworkMessage {
        public string md5;
        public int result;
    }
}
