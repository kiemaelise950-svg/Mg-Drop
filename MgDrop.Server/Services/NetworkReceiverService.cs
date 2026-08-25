// ============================================================================
// MgDrop.Server — Services/NetworkReceiverService.cs
// Module 1 : Serveur TCP asynchrone pour la réception de fichiers.
//
// ► Détecte l'IP locale Wi-Fi du PC.
// ► Écoute sur le port 8080 (ou fallback dynamique).
// ► Lit le flux binaire selon le protocole MG Drop (en-tête + données).
// ► Sauvegarde dans le dossier Téléchargements de l'utilisateur.
// ► Buffer de 8 Ko pour ne jamais saturer la RAM.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using MgDrop.Server.Models;

namespace MgDrop.Server.Services;

/// <summary>
/// Service de réception de fichiers via TCP Socket.
/// Conçu pour être injecté dans un conteneur DI ou utilisé en standalone.
/// </summary>
public sealed class NetworkReceiverService : IDisposable
{
    // ── Champs privés ──────────────────────────────────────────────────────
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly string _downloadFolder;

    // ── Événements (pour le binding WPF) ─────────────────────────────
    /// <summary>Déclenché lorsqu'un fichier est totalement reçu et écrit sur le disque.</summary>
    public event EventHandler<string>? FileReceived;

    /// <summary>Déclenché pour signaler la progression du transfert (0.0 → 1.0).</summary>
    public event Action<double, string>? OnProgress;

    // ── Propriétés publiques ───────────────────────────────────────────────
    /// <summary>Adresse IPv4 locale détectée sur la carte Wi-Fi active.</summary>
    public string LocalIpAddress { get; private set; } = "127.0.0.1";

    /// <summary>Port effectif sur lequel le serveur écoute.</summary>
    public int ListeningPort { get; private set; } = TransferProtocol.DefaultPort;

    /// <summary>Indique si le serveur est en cours d'écoute.</summary>
    public bool IsRunning { get; private set; }

    // ── Constructeur ───────────────────────────────────────────────────────
    public NetworkReceiverService()
    {
        // Ciblage dynamique du dossier Téléchargements Windows
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _downloadFolder = Path.Combine(userProfile, "Downloads");

        // Sécurité : crée le dossier si absent (cas très rare)
        if (!Directory.Exists(_downloadFolder))
        {
            Directory.CreateDirectory(_downloadFolder);
            Console.WriteLine($"[MG Drop] 📁 Dossier Téléchargements créé : {_downloadFolder}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // MÉTHODE PUBLIQUE — Démarrage du serveur TCP
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Démarre le serveur TCP en écoute asynchrone.
    /// Détecte l'IP locale, bind sur le port, et attend les connexions entrantes.
    /// </summary>
    public async Task StartAsync(CancellationToken externalToken = default)
    {
        if (IsRunning)
        {
            Console.WriteLine("[MG Drop] ⚠️  Le serveur TCP est déjà en cours d'exécution.");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = _cts.Token;

        try
        {
            // ── Étape 1 : Détection de l'IP locale Wi-Fi ──────────────────
            LocalIpAddress = DetectLocalWifiIpAddress();
            Console.WriteLine($"[MG Drop] 🌐 Adresse IP locale détectée : {LocalIpAddress}");

            // ── Étape 2 : Tentative de bind sur le port ───────────────────
            ListeningPort = BindToAvailablePort();
            Console.WriteLine($"[MG Drop] 🔌 Serveur TCP démarré sur {LocalIpAddress}:{ListeningPort}");
            Console.WriteLine($"[MG Drop] 📂 Les fichiers seront sauvegardés dans : {_downloadFolder}");
            Console.WriteLine("[MG Drop] ⏳ En attente de connexion du smartphone...");
            Console.WriteLine();

            IsRunning = true;

            // ── Étape 3 : Boucle d'écoute principale ──────────────────────
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Attente asynchrone d'une connexion entrante
                    using var client = await _listener!.AcceptTcpClientAsync(token);
                    var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "inconnu";
                    Console.WriteLine($"[MG Drop] 📲 Connexion entrante depuis : {remoteEndpoint}");

                    // Traitement du fichier reçu (dans un contexte isolé)
                    await HandleClientConnectionAsync(client, token);
                }
                catch (OperationCanceledException)
                {
                    // Arrêt propre demandé
                    break;
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[MG Drop] ❌ Erreur réseau lors de l'acceptation : {ex.Message}");
                    Console.WriteLine("[MG Drop] 🔄 Reprise de l'écoute...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop] 💀 Erreur fatale du serveur TCP : {ex.Message}");
            throw;
        }
        finally
        {
            Stop();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // MÉTHODE PUBLIQUE — Arrêt du serveur
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Arrête proprement le serveur TCP.</summary>
    public void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;
        Console.WriteLine("[MG Drop] 🛑 Serveur TCP arrêté.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // MÉTHODES PRIVÉES — Logique interne
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Détecte l'adresse IPv4 de la carte réseau Wi-Fi active du PC.
    /// Fallback : prend la première adresse IPv4 non-loopback trouvée.
    /// </summary>
    private static string DetectLocalWifiIpAddress()
    {
        try
        {
            // Priorité 1 : Chercher spécifiquement une interface Wi-Fi active
            var wifiInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    ni.GetIPProperties().UnicastAddresses
                        .Any(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork));

            if (wifiInterface is not null)
            {
                var wifiAddress = wifiInterface.GetIPProperties().UnicastAddresses
                    .First(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Address.ToString();

                Console.WriteLine($"[MG Drop] 📡 Interface Wi-Fi trouvée : {wifiInterface.Name}");
                return wifiAddress;
            }

            // Priorité 2 : Fallback sur toute interface réseau active (Ethernet, etc.)
            Console.WriteLine("[MG Drop] ⚠️  Aucune interface Wi-Fi trouvée, recherche d'alternatives...");

            var fallbackInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.GetIPProperties().UnicastAddresses
                        .Any(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork));

            if (fallbackInterface is not null)
            {
                var fallbackAddress = fallbackInterface.GetIPProperties().UnicastAddresses
                    .First(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Address.ToString();

                Console.WriteLine($"[MG Drop] 🔌 Interface réseau fallback : {fallbackInterface.Name}");
                return fallbackAddress;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop] ❌ Erreur lors de la détection IP : {ex.Message}");
        }

        Console.WriteLine("[MG Drop] ⚠️  Impossible de détecter l'IP locale, utilisation de 127.0.0.1");
        return "127.0.0.1";
    }

    /// <summary>
    /// Tente de bind le TcpListener sur le port par défaut (8080).
    /// Si le port est occupé, tente un port dynamique assigné par l'OS.
    /// </summary>
    private int BindToAvailablePort()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, TransferProtocol.DefaultPort);
            _listener.Start();
            return TransferProtocol.DefaultPort;
        }
        catch (SocketException)
        {
            Console.WriteLine($"[MG Drop] ⚠️  Port {TransferProtocol.DefaultPort} occupé, attribution d'un port dynamique...");
            _listener?.Stop();

            // Port 0 = l'OS assigne un port libre automatiquement
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            var dynamicPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Console.WriteLine($"[MG Drop] ✅ Port dynamique attribué : {dynamicPort}");
            return dynamicPort;
        }
    }

    /// <summary>
    /// Gère une connexion client entrante : lit l'en-tête du protocole,
    /// puis reçoit les données binaires du fichier avec un buffer de 8 Ko.
    /// </summary>
    private async Task HandleClientConnectionAsync(TcpClient client, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        string fileName = "fichier_inconnu";
        string savedPath = "";

        try
        {
            await using var networkStream = client.GetStream();

            // ── Lecture de l'en-tête du protocole ─────────────────────────
            // 1. Longueur du nom de fichier (4 octets, Big Endian)
            var fileNameLengthBytes = new byte[4];
            await ReadExactAsync(networkStream, fileNameLengthBytes, token);
            var fileNameLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(fileNameLengthBytes, 0));

            if (fileNameLength <= 0 || fileNameLength > TransferProtocol.MaxFileNameLength)
            {
                Console.WriteLine($"[MG Drop] ❌ Nom de fichier invalide (longueur : {fileNameLength})");
                return;
            }

            // 2. Nom du fichier (N octets, UTF-8)
            var fileNameBytes = new byte[fileNameLength];
            await ReadExactAsync(networkStream, fileNameBytes, token);
            fileName = Encoding.UTF8.GetString(fileNameBytes);

            // Nettoyage du nom de fichier pour la sécurité (suppression de path traversal)
            fileName = Path.GetFileName(fileName);
            Console.WriteLine($"[MG Drop] 📄 Fichier annoncé : \"{fileName}\"");

            // 3. Taille du fichier (8 octets, Big Endian)
            var fileSizeBytes = new byte[8];
            await ReadExactAsync(networkStream, fileSizeBytes, token);
            var fileSize = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(fileSizeBytes, 0));

            if (fileSize <= 0 || fileSize > TransferProtocol.MaxFileSize)
            {
                Console.WriteLine($"[MG Drop] ❌ Taille de fichier invalide : {fileSize} octets");
                return;
            }

            var formattedSize = FormatSize(fileSize);
            Console.WriteLine($"[MG Drop] 📊 Taille annoncée : {formattedSize}");

            // ── Gestion des doublons de nom de fichier ────────────────────
            savedPath = GetUniqueFilePath(_downloadFolder, fileName);

            // ── Réception des données binaires avec buffer de 8 Ko ────────
            Console.WriteLine("[MG Drop] ⬇️  Réception en cours...");
            OnProgress?.Invoke(0.0, fileName);

            var buffer = new byte[TransferProtocol.BufferSize];
            long totalBytesReceived = 0;
            int lastPercentReported = -1;

            await using (var fileStream = new FileStream(
                savedPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                TransferProtocol.BufferSize,
                useAsync: true))
            {
                while (totalBytesReceived < fileSize)
                {
                    token.ThrowIfCancellationRequested();

                    // Calculer combien d'octets il reste à lire
                    var bytesToRead = (int)Math.Min(
                        TransferProtocol.BufferSize,
                        fileSize - totalBytesReceived);

                    var bytesRead = await networkStream.ReadAsync(
                        buffer.AsMemory(0, bytesToRead), token);

                    if (bytesRead == 0)
                    {
                        // La connexion a été coupée prématurément
                        throw new IOException(
                            $"Connexion interrompue après {totalBytesReceived}/{fileSize} octets");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    totalBytesReceived += bytesRead;

                    // Calcul et affichage de la progression (chaque 1%)
                    var percentComplete = (int)(totalBytesReceived * 100 / fileSize);
                    if (percentComplete != lastPercentReported)
                    {
                        lastPercentReported = percentComplete;
                        var progress = totalBytesReceived / (double)fileSize;
                        OnProgress?.Invoke(progress, fileName);

                        // Affichage console tous les 10%
                        if (percentComplete % 10 == 0)
                        {
                            Console.Write($"\r[MG Drop] ⬇️  Progression : {percentComplete}% ({FormatSize(totalBytesReceived)} / {formattedSize})");
                        }
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine(); // Nouvelle ligne après la barre de progression
            Console.WriteLine($"[MG Drop] ✅ Fichier reçu et sauvegardé !");
            Console.WriteLine($"[MG Drop]    → Chemin : {savedPath}");
            Console.WriteLine($"[MG Drop]    → Taille : {formattedSize}");
            Console.WriteLine($"[MG Drop]    → Durée  : {stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine();

            // Notification via événement standard C#
            FileReceived?.Invoke(this, savedPath);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"\n[MG Drop] ⚠️  Transfert annulé pour \"{fileName}\".");
            CleanupPartialFile(savedPath);
        }
        catch (IOException ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"\n[MG Drop] ❌ Erreur I/O pendant le transfert de \"{fileName}\" : {ex.Message}");
            CleanupPartialFile(savedPath);
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"\n[MG Drop] ❌ Erreur réseau pendant le transfert de \"{fileName}\" : {ex.Message}");
            CleanupPartialFile(savedPath);
        }
    }

    // ── Utilitaires ────────────────────────────────────────────────────────

    /// <summary>
    /// Lit exactement N octets depuis le flux réseau.
    /// Indispensable car NetworkStream.ReadAsync peut retourner moins que demandé.
    /// </summary>
    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead), token);

            if (bytesRead == 0)
                throw new IOException("Connexion fermée par le client avant la fin de la lecture de l'en-tête.");

            totalRead += bytesRead;
        }
    }

    /// <summary>
    /// Génère un chemin de fichier unique si un fichier du même nom existe déjà.
    /// Ex: "photo.jpg" → "photo (1).jpg" → "photo (2).jpg"
    /// </summary>
    private static string GetUniqueFilePath(string directory, string fileName)
    {
        var fullPath = Path.Combine(directory, fileName);

        if (!File.Exists(fullPath))
            return fullPath;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        int counter = 1;

        do
        {
            fullPath = Path.Combine(directory, $"{nameWithoutExt} ({counter}){extension}");
            counter++;
        } while (File.Exists(fullPath));

        return fullPath;
    }

    /// <summary>Supprime un fichier partiellement reçu après une erreur.</summary>
    private static void CleanupPartialFile(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
                Console.WriteLine($"[MG Drop] 🗑️  Fichier partiel supprimé : {path}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MG Drop] ⚠️  Impossible de supprimer le fichier partiel : {ex.Message}");
        }
    }

    /// <summary>Formate une taille en octets en chaîne lisible.</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} octets",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} Ko",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} Mo",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} Go"
    };

    // ── IDisposable ────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
