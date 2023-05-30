using UnityEngine;
using System.Runtime.Serialization;
 
namespace Gameclaw {
    internal class Vector3IntSerializationSurrogate : ISerializationSurrogate {

        // Method called to serialize a Vector3 object
        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {
            Vector3Int v3 = (Vector3Int)obj;
            info.AddValue("x", v3.x);
            info.AddValue("y", v3.y);
            info.AddValue("z", v3.z);
        }

        // Method called to deserialize a Vector3 object
        public System.Object SetObjectData(System.Object obj, SerializationInfo info,
                                           StreamingContext context, ISurrogateSelector selector) {
            Vector3Int v3 = (Vector3Int)obj;
            v3.x = (int)info.GetValue("x", typeof( int ));
            v3.y = (int)info.GetValue("y", typeof( int ));
            v3.z = (int)info.GetValue("z", typeof( int ));
            obj = v3;
            return obj;
        }
    }
}
