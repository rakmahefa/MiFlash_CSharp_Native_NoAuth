namespace MiFlash.Core.Auth;

/// <summary>
/// Authentification EDL (M1.2). Le flux NoAuth observé dans
/// <c>SerialPortDevice.dlAuth()</c> est :
/// 1. envoi <c>&lt;sig TargetName="req" verbose="1"/&gt;</c> (ATTENTE du NAK d'auth) ;
/// 2. envoi <c>AUTHP</c> avec <c>size_in_bytes</c> = taille du blob ;
/// 3. attente ACK, puis écriture du blob brut sur le port ;
/// 4. attente ACK final.
/// v1 : <see cref="StaticBlobAuthenticator"/> (blob statique 256 o).
/// Un signer externe (PKCS#11, service) pourra se brancher sans toucher au moteur (D4).
/// </summary>
public interface IEdlAuthenticator
{
    /// <summary>Taille du blob, annoncée via <c>AUTHP size_in_bytes</c>.</summary>
    int BlobSize { get; }

    /// <summary>Blob de signature brut, écrit sur le port après l'ACK de AUTHP.</summary>
    byte[] GetAuthBlob();
}
