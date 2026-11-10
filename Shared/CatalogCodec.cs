using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Pokemon.Protocol;

// POKEMON_CATALOG 메시지의 본문: JSON(UTF-8) → Brotli 압축 → Base64.
// 포켓몬 649마리·기술 559개 목록은 같은 필드 이름과 타입 정보가 계속 반복되어 압축이 잘 된다.
// 프로토콜이 줄바꿈으로 메시지를 나누는 텍스트라서 압축한 바이트는 Base64로 바꿔 한 줄에 담는다.
public static class CatalogCodec
{
    // 한글을 \uXXXX(6바이트)로 바꾸지 않고 UTF-8(3바이트) 그대로 쓴다.
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    // 서버 시작 시 한 번만 만든다. 가장 작게 압축하는 단계라 수백 ms가 걸릴 수 있다.
    public static string Encode(PokemonCatalogResponse catalog, out int jsonBytes)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(catalog, Options);
        jsonBytes = json.Length;
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize))
            brotli.Write(json);
        return Convert.ToBase64String(compressed.ToArray());
    }

    public static PokemonCatalogResponse Decode(string base64)
    {
        try
        {
            using var compressed = new MemoryStream(Convert.FromBase64String(base64));
            using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<PokemonCatalogResponse>(brotli, Options)
                ?? throw new InvalidDataException("포켓몬 목록이 비어 있습니다.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException or IOException)
        {
            throw new InvalidDataException("포켓몬 목록을 해석할 수 없습니다.", ex);
        }
    }
}
