using System.Text;

namespace Newtin.Server.Persistence;

public static class TextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] StripUtf8Bom(byte[] data) =>
        data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? data[3..] : data;

    /// <summary>UTF-8 (con o sin BOM) o UTF-16; si no es UTF-8 válido, se lee como Windows Latin-1 para no perder las tildes.</summary>
    public static string Decode(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode.GetString(data, 2, data.Length - 2);
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

        var offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        try
        {
            return StrictUtf8.GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data, offset, data.Length - offset);
        }
    }
}