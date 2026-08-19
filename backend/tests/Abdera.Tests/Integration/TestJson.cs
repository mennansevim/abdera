using System.Text.Json;
using System.Text.Json.Serialization;

namespace Abdera.Tests.Integration;

// Program.cs enum'ları string olarak serileştirecek şekilde ayarlanmış
// (JsonStringEnumConverter) - test istemcisinin JSON'u okurken kullandığı seçenekler
// de aynı sözleşmeyi bilmeli, yoksa "Admin" gibi string değerleri enum'a çeviremez.
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
