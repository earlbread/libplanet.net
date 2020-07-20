using System.Linq;
using System.Security.Cryptography;
using Secp256k1Net;

namespace Libplanet.Crypto
{
    public class Secp256K1CryptoBackend : ICryptoBackend
    {
        private readonly Secp256k1 _instance = new Secp256k1();
        private readonly object _instanceLock = new object();

        public byte[] Sign(HashDigest<SHA256> messageHash, PrivateKey privateKey)
        {
            lock (_instanceLock)
            {
                var secp256K1Signature = new byte[Secp256k1.SIGNATURE_LENGTH];
                var privateKeyBytes = new byte[Secp256k1.PRIVKEY_LENGTH];

                privateKey.ByteArray.CopyTo(
                    privateKeyBytes,
                    Secp256k1.PRIVKEY_LENGTH - privateKey.ByteArray.Length);

                _instance.Sign(
                    secp256K1Signature,
                    messageHash.ToByteArray(),
                    privateKeyBytes);

                var signature = new byte[Secp256k1.SERIALIZED_DER_SIGNATURE_MAX_SIZE];
                _instance.SignatureSerializeDer(
                    signature,
                    secp256K1Signature,
                    out int signatureLength);

                return signature.Take(signatureLength).ToArray();
            }
        }

        public bool Verify(
            HashDigest<SHA256> messageHash,
            byte[] signature,
            PublicKey publicKey)
        {
            lock (_instanceLock)
            {
                var secp256K1Signature = new byte[Secp256k1.SIGNATURE_LENGTH];
                _instance.SignatureParseDer(secp256K1Signature, signature);

                byte[] secp256K1PublicKey = new byte[Secp256k1.PUBKEY_LENGTH];
                byte[] serializedPublicKey = publicKey.Format(false);
                _instance.PublicKeyParse(secp256K1PublicKey, serializedPublicKey);

                return _instance.Verify(
                    secp256K1Signature,
                    messageHash.ToByteArray(),
                    secp256K1PublicKey);
            }
        }
    }
}
