// ============================================================================
// MgDrop.Server — Models/TransferProtocol.cs
// Définit le protocole de communication entre le client Flutter et le serveur.
// Le client envoie d'abord un en-tête fixe, puis le flux binaire du fichier.
// ============================================================================

namespace MgDrop.Server.Models;

/// <summary>
/// Constantes du protocole de transfert MG Drop.
/// 
/// FORMAT DU FLUX TCP (envoyé par le client Flutter) :
/// ┌──────────────────────────────────────────────────────┐
/// │  4 octets  │  Longueur du nom de fichier (int32 BE)  │
/// │  N octets  │  Nom du fichier (UTF-8)                 │
/// │  8 octets  │  Taille du fichier en octets (int64 BE) │
/// │  M octets  │  Données binaires du fichier            │
/// └──────────────────────────────────────────────────────┘
/// </summary>
public static class TransferProtocol
{
    /// <summary>Taille du buffer de lecture/écriture (8 Ko — bon compromis vitesse/mémoire).</summary>
    public const int BufferSize = 8 * 1024;

    /// <summary>Port TCP par défaut pour l'écoute.</summary>
    public const int DefaultPort = 8080;

    /// <summary>Taille maximale autorisée pour le nom du fichier (sécurité).</summary>
    public const int MaxFileNameLength = 512;

    /// <summary>Taille maximale autorisée pour un fichier (4 Go).</summary>
    public const long MaxFileSize = 4L * 1024 * 1024 * 1024;
}
