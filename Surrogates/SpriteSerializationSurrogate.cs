using UnityEngine;
using System.Runtime.Serialization;

namespace Gameclaw {
    internal class SpriteSerializationSurrogate : ISerializationSurrogate {

        // Method called to serialize a Vector3 object
        public void GetObjectData(System.Object obj, SerializationInfo info, StreamingContext context) {
            Sprite sp = (Sprite)obj;
            info.AddValue("bytes", sp.texture.EncodeToPNG());
        }

        // Method called to deserialize a Vector3 object
        public System.Object SetObjectData(System.Object obj, SerializationInfo info,
                                           StreamingContext context, ISurrogateSelector selector) {
            Sprite sp = (Sprite)obj;
            sp = DataController.GetPNGFromBytes((byte[])info.GetValue("bytes", typeof( byte[] )));
            obj = sp;
            return obj;
        }
    }
}
