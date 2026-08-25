// ============================================================================
// MgDrop.Server — Models/TransferResult.cs
// Record immuable représentant le résultat d'un transfert de fichier reçu.
// ============================================================================

namespace MgDrop.Server.Models;

/// <summary>
/// Résultat renvoyé après la réception complète d'un fichier via TCP.
/// Utilise un record C# pour l'immuabilité et l'égalité structurelle.
/// </summary>
public sealed record TransferResult(
    string FileName,
    string SavedPath,
    long FileSizeBytes,
    TimeSpan Duration,
    bool Success,
    string? ErrorMessage = null
)
{
    /// <summary>
    /// Taille du fichier formatée de manière lisible (Ko, Mo, Go).
    /// </summary>
    public string FormattedSize => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} octets",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} Ko",
        < 1024 * 1024 * 1024 => $"{FileSizeBytes / (1024.0 * 1024.0):F1} Mo",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} Go"
    };
}
