namespace ServiceLib.Tests.V2pn;

/// <summary>A release signed with a throwaway key, the way tools/release.ps1 signs real ones.</summary>
internal sealed class TestRelease
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    public static byte[] Manifest(string version, params (string Name, byte[] Content)[] files)
    {
        var list = new JsonArray();
        foreach (var (name, content) in files)
        {
            list.Add(new JsonObject
            {
                ["name"] = name,
                ["size"] = content.Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)),
            });
        }
        return Encoding.UTF8.GetBytes(new JsonObject { ["version"] = version, ["files"] = list }.ToJsonString());
    }

    public string Sign(byte[] manifest) =>
        Convert.ToBase64String(_key.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
}
