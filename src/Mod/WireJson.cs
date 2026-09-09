using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Rune.Mod
{
    // Unity's runtime JSON serializer omits nested companion and plan arrays here.
    internal static class WireJson
    {
        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        public static T Read<T>(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
