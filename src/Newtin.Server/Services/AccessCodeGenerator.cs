using System.Security.Cryptography;

namespace Newtin.Server.Services;

public static class AccessCodeGenerator
{
    // Sin 0, O, 1, I ni L para evitar confusiones al dictar o escribir el código.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public static string Generate() =>
        string.Create(Length, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}